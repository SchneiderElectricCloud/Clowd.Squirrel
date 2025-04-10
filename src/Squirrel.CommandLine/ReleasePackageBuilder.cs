﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using NuGet.Versioning;
using Squirrel.MarkdownSharp;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;

namespace Squirrel.CommandLine
{
    internal interface IReleasePackage
    {
        string InputPackageFile { get; }
        string ReleasePackageFile { get; }
        SemanticVersion Version { get; }
    }

    internal class ReleasePackageBuilder : IEnableLogger, IReleasePackage
    {
        private Lazy<ZipPackage> _package;

        public ReleasePackageBuilder(string inputPackageFile, bool isReleasePackage = false)
        {
            InputPackageFile = inputPackageFile;
            _package = new Lazy<ZipPackage>(() => new ZipPackage(inputPackageFile));

            if (isReleasePackage) {
                ReleasePackageFile = inputPackageFile;
            }
        }

        public string InputPackageFile { get; protected set; }

        public string ReleasePackageFile { get; protected set; }

        public string Id => ReleaseEntry.ParseEntryFileName(InputPackageFile).PackageName;

        public SemanticVersion Version => ReleaseEntry.ParseEntryFileName(InputPackageFile).Version;

        internal string CreateReleasePackage(string outputFile, Func<string, string> releaseNotesProcessor = null, Action<string, ZipPackage> contentsPostProcessHook = null)
        {
            return CreateReleasePackage((i, p) => {
                contentsPostProcessHook?.Invoke(i, p);
                return outputFile;
            }, releaseNotesProcessor);
        }

        internal string CreateReleasePackage(Func<string, ZipPackage, string> contentsPostProcessHook, Func<string, string> releaseNotesProcessor = null)
        {
            releaseNotesProcessor = releaseNotesProcessor ?? (x => (new Markdown()).Transform(x));

            if (ReleasePackageFile != null) {
                return ReleasePackageFile;
            }

            var package = _package.Value;

            // just in-case our parsing is more-strict than nuget.exe and
            // the 'releasify' command was used instead of 'pack'.
            NugetUtil.ThrowIfInvalidNugetId(package.Id);

            // we can tell from here what platform(s) the package targets but given this is a
            // simple package we only ever expect one entry here (crash hard otherwise)
            var frameworks = package.Frameworks;
            if (frameworks.Count() > 1) {
                var platforms = frameworks
                    .Aggregate(new StringBuilder(), (sb, f) => sb.Append(f.ToString() + "; "));

                throw new InvalidOperationException(String.Format(
                    "The input package file {0} targets multiple platforms - {1} - and cannot be transformed into a release package.", InputPackageFile, platforms));

            } else if (!frameworks.Any()) {
                throw new InvalidOperationException(String.Format(
                    "The input package file {0} targets no platform and cannot be transformed into a release package.", InputPackageFile));
            }

            // CS - docs say we don't support dependencies. I can't think of any reason allowing this is useful.
            if (package.DependencySets.Any()) {
                throw new InvalidOperationException(String.Format(
                     "The input package file {0} must have no dependencies.", InputPackageFile));
            }

            this.Log().Info("Creating release from input package {0}", InputPackageFile);

            using (Utility.GetTempDirectory(out var tempPath)) {
                var tempDir = new DirectoryInfo(tempPath);

                extractZipWithEscaping(InputPackageFile, tempPath).Wait();

                var specPath = tempDir.GetFiles("*.nuspec").First().FullName;

                this.Log().Info("Removing unnecessary data");
                removeDependenciesFromPackageSpec(specPath);

                if (releaseNotesProcessor != null) {
                    renderReleaseNotesMarkdown(specPath, releaseNotesProcessor);
                }

                addDeltaFilesToContentTypes(tempDir.FullName);

                var outputFile = contentsPostProcessHook.Invoke(tempPath, package);

                EasyZip.CreateZipFromDirectory(outputFile, tempPath);

                ReleasePackageFile = outputFile;

                this.Log().Info("Package created at {0}", outputFile);
                return ReleasePackageFile;
            }
        }

        internal static string GetSuggestedFileName(string id, string version, string runtime, bool delta = false)
        {
            var tail = delta ? "delta" : "full";
            if (String.IsNullOrEmpty(runtime)) {
                return String.Format("{0}-{1}-{2}.nupkg", id, version, tail);
            } else {
                return String.Format("{0}-{1}-{2}-{3}.nupkg", id, version, runtime, tail);
            }
        }

        /// <summary>
        /// Given a list of releases and a specified release package, returns the release package
        /// directly previous to the specified version.
        /// </summary>
        internal static ReleasePackageBuilder GetPreviousRelease(IEnumerable<ReleaseEntry> releaseEntries, IReleasePackage package, string targetDir, RID compatibleRid)
        {
            if (releaseEntries == null || !releaseEntries.Any()) return null;
            return Utility.FindCompatibleVersions(releaseEntries, compatibleRid)
                .Where(x => x.IsDelta == false)
                .Where(x => x.Version < package.Version)
                .OrderByDescending(x => x.Version)
                .Select(x => new ReleasePackageBuilder(Path.Combine(targetDir, x.Filename), true))
                .FirstOrDefault();
        }

        static Task extractZipWithEscaping(string zipFilePath, string outFolder)
        {
            return Task.Run(() => {
                using (var fs = File.OpenRead(zipFilePath))
                using (var za = new ZipArchive(fs))
                    foreach (var entry in za.Entries) {
                        var parts = entry.FullName.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        var fullTargetFile = Path.Combine(outFolder, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);
                        var isDirectory = entry.IsDirectory();

                        Utility.Retry(() => {
                            if (isDirectory) {
                                Directory.CreateDirectory(fullTargetFile);
                            } else {
                                entry.ExtractToFile(fullTargetFile, true);
                            }
                        }, 5);
                    }
            });
        }

        void renderReleaseNotesMarkdown(string specPath, Func<string, string> releaseNotesProcessor)
        {
            var doc = new XmlDocument();
            doc.Load(specPath);

            var metadata = doc.DocumentElement.ChildNodes
                .OfType<XmlElement>()
                .First(x => x.Name.ToLowerInvariant() == "metadata");

            var releaseNotes = metadata.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(x => x.Name.ToLowerInvariant() == "releasenotes");

            if (releaseNotes == null || String.IsNullOrWhiteSpace(releaseNotes.InnerText)) {
                this.Log().Info("No release notes found in {0}", specPath);
                return;
            }

            var releaseNotesHtml = doc.CreateElement("releaseNotesHtml");
            releaseNotesHtml.InnerText = String.Format("<![CDATA[\n" + "{0}\n" + "]]>",
                releaseNotesProcessor(releaseNotes.InnerText));
            metadata.AppendChild(releaseNotesHtml);

            doc.Save(specPath);
        }

        void removeDependenciesFromPackageSpec(string specPath)
        {
            var xdoc = new XmlDocument();
            xdoc.Load(specPath);

            var metadata = xdoc.DocumentElement.FirstChild;
            var dependenciesNode = metadata.ChildNodes.OfType<XmlElement>().FirstOrDefault(x => x.Name.ToLowerInvariant() == "dependencies");
            if (dependenciesNode != null) {
                metadata.RemoveChild(dependenciesNode);
            }

            xdoc.Save(specPath);
        }

        static internal void addDeltaFilesToContentTypes(string rootDirectory)
        {
            var doc = new XmlDocument();
            var path = Path.Combine(rootDirectory, ContentType.ContentTypeFileName);
            doc.Load(path);

            ContentType.Merge(doc);
            ContentType.Clean(doc);

            using (var sw = new StreamWriter(path, false, Encoding.UTF8)) {
                doc.Save(sw);
            }
        }
    }
}

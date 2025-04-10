﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NuGet.Versioning;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary>
    /// Describes the requested release notes text format.
    /// </summary>
    public enum ReleaseNotesFormat
    {
        /// <summary> The original markdown release notes. </summary>
        Markdown = 0,
        /// <summary> Release notes translated into HTML. </summary>
        Html = 1,
    }

    /// <summary>
    /// Represents a Squirrel release, as described in a RELEASES file - usually also with an 
    /// accompanying package containing the files needed to apply the release.
    /// </summary>
    public interface IReleaseEntry
    {
        /// <summary> The SHA1 checksum of the update package containing this release. </summary>
        string SHA1 { get; }

        /// <summary> The filename of the update package containing this release. </summary>
        string Filename { get; }

        /// <summary> The size in bytes of the update package containing this release. </summary>
        long Filesize { get; }

        /// <summary> Whether this package represents a full update, or a delta update. </summary>
        bool IsDelta { get; }

        /// <summary> The unparsed text used to construct this release. </summary>
        string EntryAsString { get; }

        /// <summary> The version of this release. </summary>
        SemanticVersion Version { get; }

        /// <summary> The name or Id of the package containing this release. </summary>
        string PackageName { get; }

        /// <summary> 
        /// The percentage of users this package has been released to. This release
        /// may or may not be applied if the current user is not in the staging group.
        /// </summary>
        float? StagingPercentage { get; }

        /// <summary> 
        /// The runtime identifier parsed from the file name. 
        /// Used to determine if this package is suitable for the current operating system.
        /// </summary>
        RID Rid { get; }

        /// <summary>
        /// Given a local directory containing a package corresponding to this release, returns the 
        /// correspoding release notes from within the package.
        /// </summary>
        string GetReleaseNotes(string packageDirectory, ReleaseNotesFormat format);

        /// <summary>
        /// Given a local directory containing a package corresponding to this release, 
        /// returns the iconUrl specified in the package.
        /// </summary>
        Uri GetIconUrl(string packageDirectory);
    }

    /// <inheritdoc cref="IReleaseEntry" />
    [DataContract]
    public class ReleaseEntry : IEnableLogger, IReleaseEntry
    {
        /// <inheritdoc />
        [DataMember] public string SHA1 { get; protected set; }
        /// <inheritdoc />
        [DataMember] public string BaseUrl { get; protected set; }
        /// <inheritdoc />
        [DataMember] public string Filename { get; protected set; }
        /// <inheritdoc />
        [DataMember] public string Query { get; protected set; }
        /// <inheritdoc />
        [DataMember] public long Filesize { get; protected set; }
        /// <inheritdoc />
        [DataMember] public bool IsDelta { get; protected set; }
        /// <inheritdoc />
        [DataMember] public float? StagingPercentage { get; protected set; }
        /// <inheritdoc />
        [DataMember] public RID Rid { get; protected set; }

        /// <summary>
        /// Create a new instance of <see cref="ReleaseEntry"/>.
        /// </summary>
        protected ReleaseEntry(string sha1, string filename, long filesize, string baseUrl = null, string query = null, float? stagingPercentage = null)
        {
            Contract.Requires(sha1 != null && sha1.Length == 40);
            Contract.Requires(filename != null);
            Contract.Requires(filename.Contains(Path.DirectorySeparatorChar) == false);
            Contract.Requires(filesize > 0);

            SHA1 = sha1; BaseUrl = baseUrl; Filename = filename; Query = query; Filesize = filesize; StagingPercentage = stagingPercentage;

            var identity = ParseEntryFileName(Filename);
            Version = identity.Version;
            PackageName = identity.PackageName;
            IsDelta = identity.IsDelta;
            Rid = identity.Rid;
        }

        /// <inheritdoc />
        [IgnoreDataMember]
        public string EntryAsString {
            get {
                if (StagingPercentage != null) {
                    return String.Format("{0} {1}{2} {3} # {4}", SHA1, BaseUrl, Filename, Filesize, stagingPercentageAsString(StagingPercentage.Value));
                } else {
                    return String.Format("{0} {1}{2} {3}", SHA1, BaseUrl, Filename, Filesize);
                }
            }
        }

        /// <inheritdoc />
        [IgnoreDataMember]
        public SemanticVersion Version { get; }

        /// <inheritdoc />
        [IgnoreDataMember]
        public string PackageName { get; }

        /// <inheritdoc />
        public string GetReleaseNotes(string packageDirectory, ReleaseNotesFormat format)
        {
            var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
            return format switch {
                ReleaseNotesFormat.Markdown => zp.ReleaseNotes,
                ReleaseNotesFormat.Html => zp.ReleaseNotesHtml,
                _ => null,
            };
        }

        /// <inheritdoc />  
        public Uri GetIconUrl(string packageDirectory)
        {
            var zp = new ZipPackage(Path.Combine(packageDirectory, Filename));
            return zp.IconUrl;
        }

        static readonly Regex entryRegex = new Regex(@"^([0-9a-fA-F]{40})\s+(\S+)\s+(\d+)[\r]*$");
        static readonly Regex commentRegex = new Regex(@"\s*#.*$");
        static readonly Regex stagingRegex = new Regex(@"#\s+(\d{1,3})%$");

        /// <summary>
        /// Parses an string entry from a RELEASES file and returns a <see cref="ReleaseEntry"/>.
        /// </summary>
        public static ReleaseEntry ParseReleaseEntry(string entry)
        {
            Contract.Requires(entry != null);

            float? stagingPercentage = null;
            var m = stagingRegex.Match(entry);
            if (m != null && m.Success) {
                stagingPercentage = Single.Parse(m.Groups[1].Value) / 100.0f;
            }

            entry = commentRegex.Replace(entry, "");
            if (String.IsNullOrWhiteSpace(entry)) {
                return null;
            }

            m = entryRegex.Match(entry);
            if (!m.Success) {
                throw new Exception("Invalid release entry: " + entry);
            }

            if (m.Groups.Count != 4) {
                throw new Exception("Invalid release entry: " + entry);
            }

            string filename = m.Groups[2].Value;

            // Split the base URL and the filename if an URI is provided,
            // throws if a path is provided
            string baseUrl = null;
            string query = null;

            if (Utility.IsHttpUrl(filename)) {
                var uri = new Uri(filename);
                var path = uri.LocalPath;
                var authority = uri.GetLeftPart(UriPartial.Authority);

                if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(authority)) {
                    throw new Exception("Invalid URL");
                }

                var indexOfLastPathSeparator = path.LastIndexOf("/") + 1;
                baseUrl = authority + path.Substring(0, indexOfLastPathSeparator);
                filename = path.Substring(indexOfLastPathSeparator);

                if (!String.IsNullOrEmpty(uri.Query)) {
                    query = uri.Query;
                }
            }

            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) > -1) {
                throw new Exception("Filename can either be an absolute HTTP[s] URL, *or* a file name");
            }

            long size = Int64.Parse(m.Groups[3].Value);
            return new ReleaseEntry(m.Groups[1].Value, filename, size, baseUrl, query, stagingPercentage);
        }

        /// <summary>
        /// Checks if the current user is eligible for the current staging percentage.
        /// </summary>
        public bool IsStagingMatch(Guid? userId)
        {
            // A "Staging match" is when a user falls into the affirmative
            // bucket - i.e. if the staging is at 10%, this user is the one out
            // of ten case.
            if (!StagingPercentage.HasValue) return true;
            if (!userId.HasValue) return false;

            uint val = BitConverter.ToUInt32(userId.Value.ToByteArray(), 12);

            double percentage = ((double) val / (double) UInt32.MaxValue);
            return percentage < StagingPercentage.Value;
        }

        /// <summary>
        /// Parse the contents of a RELEASES file into a list of <see cref="ReleaseEntry"/>'s.
        /// </summary>
        public static IEnumerable<ReleaseEntry> ParseReleaseFile(string fileContents)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null)
                .ToArray();

            return ret.Any(x => x == null) ? new ReleaseEntry[0] : ret;
        }

        /// <summary>
        /// Parse the contents of a RELEASES file into a list of <see cref="ReleaseEntry"/>'s,
        /// with any staging-uneligible releases removed.
        /// </summary>
        public static IEnumerable<ReleaseEntry> ParseReleaseFileAndApplyStaging(string fileContents, Guid? userToken)
        {
            if (String.IsNullOrEmpty(fileContents)) {
                return new ReleaseEntry[0];
            }

            fileContents = Utility.RemoveByteOrderMarkerIfPresent(fileContents);

            var ret = fileContents.Split('\n')
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Select(ParseReleaseEntry)
                .Where(x => x != null && x.IsStagingMatch(userToken))
                .ToArray();

            return ret.Any(x => x == null) ? null : ret;
        }

        /// <summary>
        /// Write a list of <see cref="ReleaseEntry"/>'s to a stream
        /// </summary>
        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, Stream stream)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(stream != null);

            using (var sw = new StreamWriter(stream, Encoding.UTF8)) {
                sw.Write(String.Join("\n", releaseEntries
                    .OrderBy(x => x.Version)
                    .ThenByDescending(x => x.IsDelta)
                    .Select(x => x.EntryAsString)));
            }
        }

        /// <summary>
        /// Write a list of <see cref="ReleaseEntry"/>'s to a local file
        /// </summary>
        public static void WriteReleaseFile(IEnumerable<ReleaseEntry> releaseEntries, string path)
        {
            Contract.Requires(releaseEntries != null && releaseEntries.Any());
            Contract.Requires(!String.IsNullOrEmpty(path));

            using (var f = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                WriteReleaseFile(releaseEntries, f);
            }
        }

        /// <summary>
        /// Generates a <see cref="ReleaseEntry"/> from a local update package file (such as a nupkg).
        /// </summary>
        public static ReleaseEntry GenerateFromFile(Stream file, string filename, string baseUrl = null)
        {
            Contract.Requires(file != null && file.CanRead);
            Contract.Requires(!String.IsNullOrEmpty(filename));

            var hash = Utility.CalculateStreamSHA1(file);
            return new ReleaseEntry(hash, filename, file.Length, baseUrl);
        }

        /// <summary>
        /// Generates a <see cref="ReleaseEntry"/> from a local update package file (such as a nupkg).
        /// </summary>
        public static ReleaseEntry GenerateFromFile(string path, string baseUrl = null)
        {
            using (var inf = File.OpenRead(path)) {
                return GenerateFromFile(inf, Path.GetFileName(path), baseUrl);
            }
        }

        /// <summary>
        /// Generates a list of <see cref="ReleaseEntry"/>'s from a local directory containing
        /// package files. Also writes/updates a RELEASES file in the specified directory
        /// to match the packages the are currently present.
        /// </summary>
        /// <param name="releasePackagesDir">The local directory to read and update</param>
        /// <returns>The list of packages in the directory</returns>
        public static List<ReleaseEntry> BuildReleasesFile(string releasePackagesDir)
        {
            var packagesDir = new DirectoryInfo(releasePackagesDir);

            // Generate release entries for all of the local packages
            var entriesQueue = new ConcurrentQueue<ReleaseEntry>();
            Parallel.ForEach(packagesDir.GetFiles("*.nupkg"), x => {
                using (var file = x.OpenRead()) {
                    entriesQueue.Enqueue(GenerateFromFile(file, x.Name));
                }
            });

            // Write the new RELEASES file to a temp file then move it into
            // place
            var entries = entriesQueue.ToList();
            using var _ = Utility.GetTempFileName(out var tempFile);

            using (var of = File.OpenWrite(tempFile)) {
                if (entries.Count > 0) WriteReleaseFile(entries, of);
            }

            var target = Path.Combine(packagesDir.FullName, "RELEASES");
            if (File.Exists(target)) {
                File.Delete(target);
            }

            File.Move(tempFile, target);
            return entries;
        }

        static string stagingPercentageAsString(float percentage)
        {
            return String.Format("{0:F0}%", percentage * 100.0);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Filename;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Filename.GetHashCode();
        }

        static readonly Regex _suffixRegex = new Regex(@"(-full|-delta)?\.nupkg$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _versionStartRegex = new Regex(@"[\.-](0|[1-9]\d*)\.(0|[1-9]\d*)($|[^\d])", RegexOptions.Compiled);
        static readonly Regex _ridRegex = new Regex(@"-(?<os>osx|win)\.?(?<ver>[\d\.]+)?(?:-(?<arch>(?:x|arm)\d{2}))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal class EntryNameInfo
        {
            public string PackageName { get; set; }
            public SemanticVersion Version { get; set; }
            public bool IsDelta { get; set; }
            public RID Rid { get; set; }

            public EntryNameInfo()
            {
            }

            public EntryNameInfo(string packageName, SemanticVersion version, bool isDelta, RID rid)
            {
                PackageName = packageName;
                Version = version;
                IsDelta = isDelta;
                Rid = rid;
            }
        }

        /// <summary>
        /// Takes a filename such as 'My-Cool3-App-1.0.1-build.23-full.nupkg' and separates it into 
        /// it's name and version (eg. 'My-Cool3-App', and '1.0.1-build.23'). Returns null values if 
        /// the filename can not be parsed.
        /// </summary>
        internal static EntryNameInfo ParseEntryFileName(string fileName)
        {
            if (!fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                return new EntryNameInfo(null, null, false, null);

            bool delta = Path.GetFileNameWithoutExtension(fileName).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);

            var nameAndVer = _suffixRegex.Replace(Path.GetFileName(fileName), "");

            var match = _versionStartRegex.Match(nameAndVer);
            if (!match.Success)
                return new EntryNameInfo(null, null, delta, null);

            var verIdx = match.Index;
            var name = nameAndVer.Substring(0, verIdx);
            var version = nameAndVer.Substring(verIdx + 1);

            RID rid = null;
            var ridMatch = _ridRegex.Match(version);

            if (ridMatch.Success) {
                rid = RID.Parse(ridMatch.Value.TrimStart('-'));
                version = version.Substring(0, ridMatch.Index);
            }

            var semVer = NuGetVersion.Parse(version);
            return new EntryNameInfo(name, semVer, delta, rid);
        }
    }
}

﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Squirrel.Tests.TestHelpers;
using Xunit;

namespace Squirrel.Tests
{
    public class CheckForUpdateTests
    {
        [Fact(Skip = "Rewrite this to be an integration test")]
        public void NewReleasesShouldBeDetected()
        {
            Assert.Fail("Rewrite this to be an integration test");
            /*
            string localReleasesFile = Path.Combine(".", "theApp", "packages", "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.OpenRead())
                .Returns(File.OpenRead(IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOh")));

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
            var result = default(UpdateInfo);

            using (fixture) {
                result = fixture.CheckForUpdate().First();
            }

            Assert.NotNull(result);
            Assert.Equal(1, result.ReleasesToApply.Single().Version.Major);
            Assert.Equal(1, result.ReleasesToApply.Single().Version.Minor);
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public void CorruptedReleaseFileMeansWeStartFromScratch()
        {
            Assert.Fail("Rewrite this to be an integration test");

            /*
            string localPackagesDir = Path.Combine(".", "theApp", "packages");
            string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.Exists).Returns(true);
            fileInfo.Setup(x => x.OpenRead())
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes("lol this isn't right")));

            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.Setup(x => x.Exists).Returns(true);

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
            fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
            using (fixture) {
                fixture.CheckForUpdate().First();
            }

            fs.Verify(x => x.CreateDirectoryRecursive(localPackagesDir), Times.Once());
            fs.Verify(x => x.DeleteDirectoryRecursive(localPackagesDir), Times.Once());
            */
        }

        [Fact(Skip = "Rewrite this to be an integration test")]
        public void CorruptRemoteFileShouldThrowOnCheck()
        {
            Assert.Fail("Rewrite this to be an integration test");

            /*
            string localPackagesDir = Path.Combine(".", "theApp", "packages");
            string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.Setup(x => x.Exists).Returns(false);

            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.Setup(x => x.Exists).Returns(true);

            var fs = new Mock<IFileSystemFactory>();
            fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
            fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
            fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

            var urlDownloader = new Mock<IUrlDownloader>();
            urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
                .Returns(Observable.Return("lol this isn't right"));

            var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);

            using (fixture) {
                Assert.Throws<Exception>(() => fixture.CheckForUpdate().First());   
            }
            */
        }

        [Fact(Skip = "TODO")]
        public void IfLocalVersionGreaterThanRemoteWeRollback()
        {
            throw new NotImplementedException();
        }

        [Fact(Skip = "TODO")]
        public void IfLocalAndRemoteAreEqualThenDoNothing()
        {
            throw new NotImplementedException();
        }

        [Theory]
        [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec MyCoolApp-1.0.nupkg 1004502", "MyCoolApp", null)]
        [InlineData(@"0000000000000000000000000000000000000000  https://www.test.org/Folder/MyCoolApp-1.2-delta.nupkg?query=param  1231953", "MyCoolApp", "https://www.test.org/Folder")]
        public async Task WebSourceRequestsExpectedUrls(string releaseEntry, string releaseName, string baseUrl)
        {
            baseUrl = baseUrl ?? "https://example.com/files";
            var dl = new FakeDownloader();
            var source = new Sources.SimpleWebSource("https://example.com/files", dl);

            dl.MockedResponseBytes = Encoding.UTF8.GetBytes(releaseEntry);
            var releases = await source.GetReleaseFeed(null, null);
            Assert.True(releases.Count() == 1);
            Assert.Equal(releaseName, releases[0].PackageName);
            Assert.Equal("https://example.com/files/RELEASES", new Uri(dl.LastUrl).GetLeftPart(UriPartial.Path));

            await source.DownloadReleaseEntry(releases[0], "test", null);
            Assert.Equal(baseUrl + "/" + releases[0].Filename, new Uri(dl.LastUrl).GetLeftPart(UriPartial.Path));
        }

        [Theory]
        [InlineData("http://example.com", "MyPackage.nupkg", "http://example.com/MyPackage.nupkg")]
        [InlineData("http://example.com?auth=hello", "MyPackage.nupkg", "http://example.com/MyPackage.nupkg?auth=hello")]
        [InlineData("http://example.com?auth=hello", "https://my.packages.domain/MyPackage-1.0.0.nupkg", "https://my.packages.domain/MyPackage-1.0.0.nupkg")]
        public async Task SimpleWebSourcePreservesQueryParametersAndAbsoluteReleaseUri(string baseUri, string releaseUri, string expectedPackageUrl)
        {
            var dl = new FakeDownloader();
            var source = new Sources.SimpleWebSource(baseUri, dl);
            var baseKvp = HttpUtility.ParseQueryString(new Uri(baseUri).Query);
            var baseDict = baseKvp.AllKeys.Where(k => k != null).ToDictionary(k => k, k => baseKvp[k]);

            var releaseEntry = $"94689fede03fed7ab59c24337673a27837f0c3ec {releaseUri} 1004502";
            dl.MockedResponseBytes = Encoding.UTF8.GetBytes(releaseEntry);

            var releases = await source.GetReleaseFeed();
            var expected = new Uri(baseUri).GetLeftPart(UriPartial.Path).TrimEnd('/') + "/RELEASES";
            Assert.StartsWith(expected, dl.LastUrl);

            // check that each query parameter in base url is in the releases string
            var releasesUri = new Uri(dl.LastUrl);
            var releasesKvp = HttpUtility.ParseQueryString(releasesUri.Query);
            var releasesDict = releasesKvp.AllKeys.Where(k => k != null).ToDictionary(k => k, k => releasesKvp[k]);
            foreach (var kvp in baseDict) {
                Assert.Equal(releasesDict[kvp.Key], kvp.Value);
            }

            await source.DownloadReleaseEntry(releases[0], "test", null);
            Assert.Equal(expectedPackageUrl, dl.LastUrl);
        }
    }
}

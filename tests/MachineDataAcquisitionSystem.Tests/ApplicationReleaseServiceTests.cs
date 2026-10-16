using System;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ApplicationReleaseServiceTests : IDisposable
    {
        private readonly ReleaseNotesTestDirectory files = new ReleaseNotesTestDirectory();

        [Fact]
        public void Installed_version_is_used_instead_of_the_assembly_version()
        {
            files.Write("installed-version.txt", "1.2.3.4\r\n");

            Assert.Equal(new Version(1, 2, 3, 4), new ApplicationReleaseService(files.Path).CurrentVersion);
        }

        [Fact]
        public void Deployments_without_an_installed_version_use_the_application_assembly_version()
        {
            Assert.Equal(typeof(Form1).Assembly.GetName().Version,
                new ApplicationReleaseService(files.Path).CurrentVersion);
        }

        [Fact]
        public void History_is_sorted_by_numeric_version_and_preserves_chinese_multiline_notes()
        {
            files.Write("installed-version.txt", "1.0.0.10");
            files.Write("Resources/CHANGELOG.md",
                "# 更新日志\r\n\r\n## 未发布\r\n- 尚未发布的计划。\r\n\r\n" +
                "## 1.0.0.9 - 2026-10-15\r\n- 历史功能。\r\n\r\n" +
                "## 1.0.0.10 - 2026-10-16\r\n- 显示当前版本。\r\n\r\n### 修复\r\n- 保留中文与换行。\r\n");

            var history = new ApplicationReleaseService(files.Path).Releases;

            Assert.Equal(new[] { "1.0.0.10", "1.0.0.9" }, history.Select(release => release.Version.ToString()));
            Assert.Equal("2026-10-16", history[0].ReleaseDate);
            Assert.Contains("显示当前版本。", history[0].Notes);
            Assert.Contains("### 修复" + Environment.NewLine + "- 保留中文与换行。", history[0].Notes);
            Assert.DoesNotContain(history, release => release.Notes.Contains("尚未发布"));
        }

        [Fact]
        public void Missing_changelog_still_provides_the_current_version_and_an_empty_notes_message()
        {
            files.Write("installed-version.txt", "1.0.0.2");

            var release = Assert.Single(new ApplicationReleaseService(files.Path).Releases);

            Assert.Equal(new Version(1, 0, 0, 2), release.Version);
            Assert.Equal("该版本暂无更新说明", release.Notes);
        }

        [Fact]
        public void Missing_current_version_notes_does_not_hide_older_release_history()
        {
            files.Write("installed-version.txt", "1.0.0.3");
            files.Write("Resources/CHANGELOG.md", "## 1.0.0.1 - 2026-10-15\n- 共享目录更新。\n");

            var history = new ApplicationReleaseService(files.Path).Releases;

            Assert.Equal(new[] { "1.0.0.3", "1.0.0.1" }, history.Select(release => release.Version.ToString()));
            Assert.Equal("该版本暂无更新说明", history[0].Notes);
            Assert.Contains("共享目录更新。", history[1].Notes);
        }

        [Fact]
        public void A_version_is_notified_once_and_a_later_installed_version_is_notified_again()
        {
            files.Write("installed-version.txt", "1.0.0.1");
            var first = new ApplicationReleaseService(files.Path);
            Assert.True(first.ShouldShowUpdateNotice());

            first.MarkUpdateNoticeShown();

            Assert.False(new ApplicationReleaseService(files.Path).ShouldShowUpdateNotice());
            files.Write("installed-version.txt", "1.0.0.3");
            var upgraded = new ApplicationReleaseService(files.Path);
            Assert.True(upgraded.ShouldShowUpdateNotice());
            upgraded.MarkUpdateNoticeShown();
            Assert.False(new ApplicationReleaseService(files.Path).ShouldShowUpdateNotice());
        }

        public void Dispose() => files.Dispose();
    }

    internal sealed class ReleaseNotesTestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "AcquisitionReleaseNotesTests", Guid.NewGuid().ToString("N"));

        public ReleaseNotesTestDirectory()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "Resources"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "UpdateCache"));
        }

        public void Write(string relative, string content)
        {
            string target = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            File.WriteAllText(target, content);
        }

        public void Dispose()
        {
            File.Delete(System.IO.Path.Combine(Path, "installed-version.txt"));
            File.Delete(System.IO.Path.Combine(Path, "Resources", "CHANGELOG.md"));
            File.Delete(System.IO.Path.Combine(Path, "UpdateCache", "last-notified-version.txt"));
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class UpdateHistoryFormTests
    {
        [Fact]
        public void Automatic_notice_is_modeless_and_is_not_opened_again_for_the_same_version()
        {
            RunInSta(() =>
            {
                using (var files = new ReleaseNotesTestDirectory())
                using (var owner = CreateOwner())
                {
                    files.Write("installed-version.txt", "1.0.0.2");
                    var releases = new ApplicationReleaseService(files.Path);
                    owner.Show();

                    UpdateHistoryForm.ShowUpdateNotice(owner, releases);
                    Application.DoEvents();

                    var notice = Assert.IsType<UpdateHistoryForm>(Assert.Single(owner.OwnedForms));
                    Assert.True(notice.Visible);
                    Assert.False(notice.Modal);
                    Assert.True(owner.Enabled);
                    Assert.False(new ApplicationReleaseService(files.Path).ShouldShowUpdateNotice());
                    notice.Close();

                    UpdateHistoryForm.ShowUpdateNotice(owner, new ApplicationReleaseService(files.Path));

                    Assert.Empty(owner.OwnedForms);
                }
            });
        }

        [Fact]
        public void Manual_history_selects_current_version_and_can_display_older_notes_without_marking_a_notice()
        {
            RunInSta(() =>
            {
                using (var files = new ReleaseNotesTestDirectory())
                using (var owner = CreateOwner())
                {
                    files.Write("installed-version.txt", "1.0.0.2");
                    files.Write("Resources/CHANGELOG.md",
                        "## 1.0.0.1 - 2026-10-15\n- 旧版采集功能。\n\n" +
                        "## 1.0.0.2 - 2026-10-16\n- 新版更新提示。\n");
                    var releases = new ApplicationReleaseService(files.Path);
                    owner.Show();
                    using (var window = new UpdateHistoryForm(releases))
                    {
                        window.Show(owner);
                        var versions = (ListBox)window.Controls.Find("releaseVersionList", true).Single();
                        var notes = (RichTextBox)window.Controls.Find("releaseNotesText", true).Single();

                        Assert.Equal(new Version(1, 0, 0, 2), ((ReleaseNote)versions.SelectedItem).Version);
                        Assert.Contains("新版更新提示。", notes.Text);
                        versions.SelectedIndex = 1;
                        Assert.Contains("旧版采集功能。", notes.Text);
                        Assert.True(releases.ShouldShowUpdateNotice());
                        window.Close();
                    }
                }
            });
        }

        private static Form CreateOwner() => new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000)
        };

        private static void RunInSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "更新日志界面测试超时。");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

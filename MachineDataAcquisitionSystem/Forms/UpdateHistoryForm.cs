using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;

namespace MachineDataAcquisitionSystem.Forms
{
    internal sealed class UpdateHistoryForm : Form
    {
        public UpdateHistoryForm(ApplicationReleaseService releases)
        {
            Text = "更新日志 - v" + releases.CurrentVersion;
            ClientSize = new Size(800, 540);
            MinimumSize = new Size(680, 440);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 10F);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.Controls.Add(new Label
            {
                Text = "当前版本：v" + releases.CurrentVersion,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            }, 0, 0);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = new Size(768, 420),
                SplitterDistance = 238,
                Margin = Padding.Empty
            };
            var versions = new ListBox
            {
                Name = "releaseVersionList",
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                DisplayMember = nameof(ReleaseNote.DisplayTitle)
            };
            var notes = new RichTextBox
            {
                Name = "releaseNotesText",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            versions.SelectedIndexChanged += (sender, args) =>
            {
                var release = (ReleaseNote)versions.SelectedItem;
                string date = release.ReleaseDate.Length == 0 ? string.Empty : "发布日期：" + release.ReleaseDate + Environment.NewLine;
                notes.Text = "版本：v" + release.Version + Environment.NewLine + date + Environment.NewLine
                    + (release.Notes.Length == 0 ? "该版本暂无更新说明" : release.Notes);
            };
            versions.Items.AddRange(releases.Releases.Cast<object>().ToArray());
            versions.SelectedItem = releases.Releases.First(release => release.Version.Equals(releases.CurrentVersion));
            split.Panel1.Controls.Add(versions);
            split.Panel2.Controls.Add(notes);
            layout.Controls.Add(split, 0, 1);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 10, 0, 0)
            };
            var close = new Button { Text = "关闭", Size = new Size(96, 32), Margin = Padding.Empty };
            close.Click += (sender, args) => Close();
            footer.Controls.Add(close);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
        }

        internal static void ShowUpdateNotice(Form owner, ApplicationReleaseService releases)
        {
            if (!releases.ShouldShowUpdateNotice()) return;

            var window = new UpdateHistoryForm(releases);
            window.Text = "更新说明 - v" + releases.CurrentVersion;
            window.Shown += (sender, args) => releases.MarkUpdateNoticeShown();
            window.Show(owner);
        }
    }
}

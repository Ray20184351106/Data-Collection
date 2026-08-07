using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core.Mapping;

namespace MachineDataAcquisitionSystem.Forms
{
    public sealed class LegacyMigrationConflictDialog : Form
    {
        private readonly List<ChoiceRow> _rows = new List<ChoiceRow>();

        public LegacyMigrationConflictDialog(LegacyMigrationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            Text = "解析脚本迁移冲突";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 420);
            Size = new Size(900, 560);
            ShowInTaskbar = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(840, 0),
                Text = "检测到同一机台和扩展名存在多个启用脚本。程序不会自动挑选，请逐项指定要保留为发布版本的脚本。"
            }, 0, 0);

            var choicesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            foreach (LegacyBindingConflict conflict in report.Conflicts)
            {
                var row = new ChoiceRow(conflict);
                _rows.Add(row);
                choicesPanel.Controls.Add(row.Panel);
            }
            root.Controls.Add(choicesPanel, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            var cancel = new Button { Text = "取消并退出", AutoSize = true, DialogResult = DialogResult.Cancel };
            var confirm = new Button { Text = "确认选择并迁移", AutoSize = true };
            confirm.Click += Confirm_Click;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(confirm);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
            AcceptButton = confirm;
            CancelButton = cancel;
        }

        public IReadOnlyCollection<LegacyBindingChoice> Choices { get; private set; }

        private void Confirm_Click(object sender, EventArgs e)
        {
            if (_rows.Any(row => row.ComboBox.SelectedItem == null))
            {
                MessageBox.Show("每一项冲突都必须明确选择一个脚本。", "尚未完成选择", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Choices = _rows.Select(row => new LegacyBindingChoice
            {
                MachineId = row.Conflict.MachineId,
                NormalizedExtension = row.Conflict.NormalizedExtension,
                SelectedScriptId = ((CandidateItem)row.ComboBox.SelectedItem).Id
            }).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class ChoiceRow
        {
            public ChoiceRow(LegacyBindingConflict conflict)
            {
                Conflict = conflict;
                Panel = new TableLayoutPanel
                {
                    Width = 820,
                    Height = 72,
                    ColumnCount = 2,
                    Margin = new Padding(0, 8, 0, 8)
                };
                Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
                Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                Panel.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = "机台 " + conflict.MachineId + " / " + conflict.NormalizedExtension
                }, 0, 0);
                ComboBox = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (LegacyScriptCandidate candidate in conflict.Candidates)
                    ComboBox.Items.Add(new CandidateItem(candidate));
                Panel.Controls.Add(ComboBox, 1, 0);
            }

            public LegacyBindingConflict Conflict { get; private set; }
            public TableLayoutPanel Panel { get; private set; }
            public ComboBox ComboBox { get; private set; }
        }

        private sealed class CandidateItem
        {
            private readonly LegacyScriptCandidate _candidate;

            public CandidateItem(LegacyScriptCandidate candidate)
            {
                _candidate = candidate;
            }

            public long Id { get { return _candidate.ParseScriptId; } }

            public override string ToString()
            {
                return "#" + _candidate.ParseScriptId + " " + _candidate.ScriptName +
                       "（模型 " + _candidate.ModelId + "）";
            }
        }
    }
}

using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class ModelConfigForm
    {
        private const string MappingTargetFieldColumn = "MappingTargetField";
        private const string MappingTargetTypeColumn = "MappingTargetType";
        private const string MappingRequiredColumn = "MappingRequired";
        private const string MappingDescriptionColumn = "MappingDescription";
        private const string MappingRoleColumn = "MappingRole";
        private const string MappingScopeColumn = "MappingScope";
        private const string MappingKeyColumn = "MappingKey";
        private const string MappingLocatorTypeColumn = "MappingLocatorType";
        private const string MappingLocatorValueColumn = "MappingLocatorValue";
        private const string MappingRowOffsetColumn = "MappingRowOffset";
        private const string MappingColumnOffsetColumn = "MappingColumnOffset";
        private const string MappingValueColumnColumn = "MappingValueColumn";
        private const string MappingDataRowOffsetColumn = "MappingDataRowOffset";
        private const string MappingTransformsColumn = "MappingTransforms";
        private const string MappingValueMapColumn = "MappingValueMap";
        private const string MappingDefaultValueColumn = "MappingDefaultValue";
        private const string MappingConfirmationColumn = "MappingConfirmation";
        private const string MappingHumanConfirmedColumn = "MappingHumanConfirmed";
        private const string MappingPreviewCellColumn = "MappingPreviewCell";
        private const string MappingPreviewValueColumn = "MappingPreviewValue";
        private const string MappingPreviewConvertedValueColumn = "MappingPreviewConvertedValue";
        private const string MappingPreviewResultColumn = "MappingPreviewResult";
        private const int MappingSampleAutoScrollEdge = 28;

        private bool _mappingUiInitialized;
        private bool _mappingDataLoaded;
        private bool _mappingSuppressEvents;
        private bool _mappingApplyingSuggestion;
        private bool _mappingDirty;
        private string _mappingCleanEditorStateHash;
        private string _mappingCleanTemplateSignature;
        private bool _mappingAiBusy;
        private int _mappingSelectedListIndex = -1;

        private ComboBox _mappingModelCombo;
        private ComboBox _mappingDetailModelCombo;
        private TextBox _mappingParentCidTextBox;
        private Button _mappingNewMasterModelButton;
        private Label _mappingFileNameInfoLabel;
        private Label _mappingImageRootLabel;
        private Label _mappingImagePathFieldLabel;
        private TextBox _mappingImageRootTextBox;
        private ComboBox _mappingImagePathFieldCombo;
        private Label _mappingCsvEncodingLabel;
        private Label _mappingCsvDelimiterLabel;
        private ComboBox _mappingCsvEncodingCombo;
        private ComboBox _mappingCsvDelimiterCombo;
        private Label _mappingCsvHeaderRowLabel;
        private Label _mappingCsvFirstDataRowLabel;
        private NumericUpDown _mappingCsvHeaderRowNumber;
        private NumericUpDown _mappingCsvFirstDataRowNumber;
        private ComboBox _mappingSheetCombo;
        private TextBox _mappingRuleNameTextBox;
        private TextBox _mappingSamplePathTextBox;
        private Label _mappingStatusLabel;
        private Label _mappingExtensionLabel;
        private Button _mappingBrowseButton;
        private Button _mappingPickLocatorButton;
        private Button _mappingLocalAssistButton;
        private Button _mappingAiAssistButton;
        private Button _mappingFrameTableButton;
        private Button _mappingSaveButton;
        private Button _mappingDeleteButton;
        private FlowLayoutPanel _mappingMachinePanel;
        private DataGridView _mappingSampleGrid;
        private DataGridView _mappingRecordsGrid;
        private SplitContainer _mappingCenterSplit;
        private ToolTip _mappingToolTip;
        private ComboBox _mappingModeCombo;
        private System.Windows.Forms.Timer _mappingSampleAutoScrollTimer;

        private ParseRuleStore _mappingRuleStore;
        private ExcelMappingPreviewService _mappingPreviewService;
        private CsvMappingPreviewService _mappingCsvPreviewService;
        private LocalMappingAssistant _mappingLocalAssistant;
        private MappingWorkbookSnapshot _mappingSnapshot;
        private MappingRuleDefinition _mappingCurrentDefinition;
        private ParseRuleVersion _mappingCurrentVersion;
        private string _mappingTemplateSignature;
        private AiMappingClientOptions _mappingAiOptions;
        private string _mappingAiUnavailableReason;
        private CancellationTokenSource _mappingAiCancellation;
        private int _mappingPickTargetRowIndex = -1;
        private int _mappingPickAnchorSampleRowIndex = -1;
        private int _mappingPickAnchorSampleColumnIndex = -1;
        private MappingCellSnapshot _mappingPickAnchorCell;
        private RepeatedRowDefinition _mappingRepeatedRows;
        private int _mappingSampleDragAnchorRowIndex = -1;
        private int _mappingSampleDragAnchorColumnIndex = -1;
        private int _mappingSampleDragEndRowIndex = -1;
        private int _mappingSampleDragEndColumnIndex = -1;
        private Point _mappingSampleMouseDownPoint;
        private bool _mappingSampleDragging;

        private void InitializeMappingUi()
        {
            if (_mappingUiInitialized) return;
            _mappingUiInitialized = true;

            panel2.SuspendLayout();
            panel3.SuspendLayout();
            splitContainer3.Panel2.SuspendLayout();
            try
            {
                panel2.Height = 268;
                panel3.Height = 62;
                btnNewMappingScript.Text = "+ 新建映射";

                _mappingDeleteButton = new Button
                {
                    Dock = DockStyle.Bottom,
                    Height = 42,
                    Text = "删除映射",
                    BackColor = Color.MistyRose
                };
                splitContainer3.Panel1.Controls.Add(_mappingDeleteButton);

                BuildMappingHeader();
                BuildMappingCenter();
                BuildMappingFooter();
                ConfigureMappingGrid();
                WireMappingEvents();
                UpdateSourceMappingControls();
            }
            finally
            {
                splitContainer3.Panel2.ResumeLayout(true);
                panel3.ResumeLayout(true);
                panel2.ResumeLayout(true);
            }
        }

        private void BuildMappingHeader()
        {
            panel2.Controls.Clear();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 6,
                Padding = new Padding(6, 4, 6, 2)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            _mappingModelCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingDetailModelCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingParentCidTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "PARENT_CID"
            };
            _mappingNewMasterModelButton = new Button
            {
                Dock = DockStyle.Fill,
                Text = "新建主模型"
            };
            _mappingFileNameInfoLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "文件名：未加载样本"
            };
            _mappingRuleNameTextBox = new TextBox { Dock = DockStyle.Fill };
            _mappingStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "尚未加载"
            };
            _mappingSamplePathTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = SystemColors.Window
            };
            _mappingBrowseButton = new Button
            {
                Dock = DockStyle.Fill,
                Text = "选择样本..."
            };
            _mappingExtensionLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "扩展名：-"
            };
            _mappingSheetCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 116
            };
            _mappingModeCombo.Items.Add(new MappingModeChoice(MappingRecordMode.SingleRecord, "单条记录"));
            _mappingModeCombo.Items.Add(new MappingModeChoice(MappingRecordMode.RepeatingRows, "重复行表格"));
            _mappingModeCombo.Items.Add(new MappingModeChoice(MappingRecordMode.MasterDetail, "主表 + 子表"));
            _mappingModeCombo.Items.Add(new MappingModeChoice(MappingRecordMode.ImageFileName, "图片文件名"));
            _mappingModeCombo.SelectedIndex = 0;
            _mappingImageRootLabel = CreateMappingLabel("共享目录：");
            _mappingImageRootTextBox = new TextBox { Dock = DockStyle.Fill };
            _mappingImagePathFieldLabel = CreateMappingLabel("路径字段：");
            _mappingImagePathFieldCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingCsvEncodingLabel = CreateMappingLabel("CSV 编码：");
            _mappingCsvEncodingCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingCsvEncodingCombo.Items.AddRange(new object[] { "UTF-8", "GB18030", "GBK" });
            _mappingCsvEncodingCombo.SelectedIndex = 0;
            _mappingCsvDelimiterLabel = CreateMappingLabel("分隔符：");
            _mappingCsvDelimiterCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mappingCsvDelimiterCombo.Items.AddRange(new object[] { "逗号 (,)" , "分号 (;)" , "Tab", "竖线 (|)" });
            _mappingCsvDelimiterCombo.SelectedIndex = 0;
            _mappingCsvHeaderRowLabel = CreateMappingLabel("表头行：");
            _mappingCsvHeaderRowNumber = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = ExcelMappingPreviewService.MaximumRows,
                Value = 1
            };
            _mappingCsvFirstDataRowLabel = CreateMappingLabel("首数据行：");
            _mappingCsvFirstDataRowNumber = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = ExcelMappingPreviewService.MaximumRows,
                Value = 2
            };
            _mappingLocalAssistButton = new Button
            {
                AutoSize = true,
                Height = 28,
                Text = "本地辅助"
            };
            _mappingPickLocatorButton = new Button
            {
                AutoSize = true,
                Height = 28,
                Text = "点选定位"
            };
            _mappingAiAssistButton = new Button
            {
                AutoSize = true,
                Height = 28,
                Text = "AI 自动填映射"
            };
            _mappingFrameTableButton = new Button
            {
                AutoSize = true,
                Height = 28,
                Text = "框选表格"
            };

            layout.Controls.Add(CreateMappingLabel("模型："), 0, 0);
            layout.Controls.Add(_mappingModelCombo, 1, 0);
            layout.Controls.Add(CreateMappingLabel("规则名："), 2, 0);
            layout.Controls.Add(_mappingRuleNameTextBox, 3, 0);
            layout.Controls.Add(CreateMappingLabel("状态："), 4, 0);
            layout.Controls.Add(_mappingStatusLabel, 5, 0);
            layout.SetColumnSpan(_mappingStatusLabel, 3);

            layout.Controls.Add(CreateMappingLabel("样本："), 0, 1);
            layout.Controls.Add(_mappingSamplePathTextBox, 1, 1);
            layout.SetColumnSpan(_mappingSamplePathTextBox, 5);
            layout.Controls.Add(_mappingBrowseButton, 6, 1);
            layout.Controls.Add(_mappingExtensionLabel, 7, 1);

            layout.Controls.Add(CreateMappingLabel("工作表："), 0, 2);
            layout.Controls.Add(_mappingSheetCombo, 1, 2);
            var assistants = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            assistants.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 7, 3, 0),
                Text = "模式："
            });
            assistants.Controls.Add(_mappingModeCombo);
            assistants.Controls.Add(_mappingPickLocatorButton);
            assistants.Controls.Add(_mappingFrameTableButton);
            assistants.Controls.Add(_mappingLocalAssistButton);
            assistants.Controls.Add(_mappingAiAssistButton);
            var hint = new Label
            {
                AutoSize = true,
                Margin = new Padding(12, 7, 0, 0),
                ForeColor = Color.DimGray,
                Text = "辅助功能只填空白且未确认项，不会自动保存或发布"
            };
            assistants.Controls.Add(hint);
            layout.Controls.Add(assistants, 2, 2);
            layout.SetColumnSpan(assistants, 6);

            _mappingMachinePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                Margin = new Padding(0)
            };
            layout.Controls.Add(CreateMappingLabel("子模型："), 0, 4);
            layout.Controls.Add(_mappingDetailModelCombo, 1, 4);
            layout.Controls.Add(CreateMappingLabel("关联字段："), 2, 4);
            layout.Controls.Add(_mappingParentCidTextBox, 3, 4);
            layout.Controls.Add(_mappingNewMasterModelButton, 4, 4);
            layout.Controls.Add(_mappingFileNameInfoLabel, 5, 4);
            layout.SetColumnSpan(_mappingFileNameInfoLabel, 3);

            layout.Controls.Add(_mappingImageRootLabel, 0, 5);
            layout.Controls.Add(_mappingImageRootTextBox, 1, 5);
            layout.SetColumnSpan(_mappingImageRootTextBox, 3);
            layout.Controls.Add(_mappingImagePathFieldLabel, 4, 5);
            layout.Controls.Add(_mappingImagePathFieldCombo, 5, 5);
            layout.SetColumnSpan(_mappingImagePathFieldCombo, 3);

            layout.Controls.Add(_mappingCsvEncodingLabel, 0, 5);
            layout.Controls.Add(_mappingCsvEncodingCombo, 1, 5);
            layout.Controls.Add(_mappingCsvDelimiterLabel, 2, 5);
            layout.Controls.Add(_mappingCsvDelimiterCombo, 3, 5);
            layout.Controls.Add(_mappingCsvHeaderRowLabel, 4, 5);
            layout.Controls.Add(_mappingCsvHeaderRowNumber, 5, 5);
            layout.Controls.Add(_mappingCsvFirstDataRowLabel, 6, 5);
            layout.Controls.Add(_mappingCsvFirstDataRowNumber, 7, 5);

            layout.Controls.Add(CreateMappingLabel("适用机台："), 0, 3);
            layout.Controls.Add(_mappingMachinePanel, 1, 3);
            layout.SetColumnSpan(_mappingMachinePanel, 7);

            panel2.Controls.Add(layout);
        }

        private void BuildMappingCenter()
        {
            Control middleParent = dataGridView1.Parent;
            middleParent.Controls.Remove(dataGridView1);

            _mappingSampleGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = true,
                RowHeadersWidth = 56,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = SystemColors.Window
            };
            _mappingRecordsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = SystemColors.Window
            };
            _mappingSampleAutoScrollTimer = new System.Windows.Forms.Timer(components)
            {
                Interval = 80
            };

            _mappingCenterSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                SplitterDistance = 170,
                Panel1MinSize = 90,
                Panel2MinSize = 140
            };
            var sampleTabs = new TabControl
            {
                Dock = DockStyle.Fill
            };
            var samplePage = new TabPage("源文件样本（可框选）");
            var recordsPage = new TabPage("采集结果预览");
            samplePage.Controls.Add(_mappingSampleGrid);
            recordsPage.Controls.Add(_mappingRecordsGrid);
            sampleTabs.TabPages.Add(samplePage);
            sampleTabs.TabPages.Add(recordsPage);
            var mappingGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "字段映射"
            };
            dataGridView1.Dock = DockStyle.Fill;
            mappingGroup.Controls.Add(dataGridView1);
            _mappingCenterSplit.Panel1.Controls.Add(sampleTabs);
            _mappingCenterSplit.Panel2.Controls.Add(mappingGroup);

            middleParent.Controls.Remove(panel2);
            middleParent.Controls.Remove(panel3);
            var editorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, panel2.Height));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, panel3.Height));
            panel2.Dock = DockStyle.Fill;
            panel3.Dock = DockStyle.Fill;
            editorLayout.Controls.Add(panel2, 0, 0);
            editorLayout.Controls.Add(_mappingCenterSplit, 0, 1);
            editorLayout.Controls.Add(panel3, 0, 2);
            middleParent.Controls.Add(editorLayout);
        }

        private void BuildMappingFooter()
        {
            panel3.Controls.Clear();
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(8, 8, 8, 5)
            };
            _mappingSaveButton = CreateMappingActionButton("保存", 100);
            actions.Controls.Add(_mappingSaveButton);
            panel3.Controls.Add(actions);
        }

        private static Label CreateMappingLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Text = text
            };
        }

        private static Button CreateMappingActionButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 36,
                Margin = new Padding(6, 0, 0, 0)
            };
        }

        private void ConfigureMappingGrid()
        {
            dataGridView1.AutoGenerateColumns = false;
            dataGridView1.Columns.Clear();
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.AllowUserToResizeRows = false;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.MultiSelect = false;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridView1.EditMode = DataGridViewEditMode.EditOnEnter;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView1.BackgroundColor = SystemColors.Window;

            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingTargetFieldColumn, "目标字段", 130, true));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingTargetTypeColumn, "类型", 70, true));
            dataGridView1.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = MappingRequiredColumn,
                HeaderText = "必填",
                Width = 50,
                ReadOnly = true
            });
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingDescriptionColumn, "说明", 150, true));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingRoleColumn, "归属", 65, true));
            var scope = new DataGridViewComboBoxColumn
            {
                Name = MappingScopeColumn,
                HeaderText = "来源范围",
                Width = 85,
                FlatStyle = FlatStyle.Flat
            };
            scope.Items.AddRange("公共字段", "明细列");
            dataGridView1.Columns.Add(scope);
            dataGridView1.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = MappingKeyColumn,
                HeaderText = "关键列",
                Width = 60
            });

            var locatorType = new DataGridViewComboBoxColumn
            {
                Name = MappingLocatorTypeColumn,
                HeaderText = "定位方式",
                Width = 115,
                FlatStyle = FlatStyle.Flat
            };
            locatorType.Items.AddRange(
                "cell", "labelOffset", "rowKey", "headerColumn", "rowColumn",
                "fileNameFull", "fileNameStem", "fileNameSegment");
            dataGridView1.Columns.Add(locatorType);
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingLocatorValueColumn, "单元格 / 标签", 135, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingRowOffsetColumn, "行偏移", 65, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingColumnOffsetColumn, "列偏移", 65, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingValueColumnColumn, "值列", 60, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingDataRowOffsetColumn, "数据行偏移", 85, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingTransformsColumn, "转换（逗号分隔）", 140, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingValueMapColumn, "精确值映射（源=目标;...）", 190, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingDefaultValueColumn, "默认值", 100, false));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingConfirmationColumn, "建议状态", 105, true));
            dataGridView1.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = MappingHumanConfirmedColumn,
                HeaderText = "人工确认",
                Width = 70
            });
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingPreviewCellColumn, "来源单元格", 90, true));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingPreviewValueColumn, "原值", 125, true));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingPreviewConvertedValueColumn, "转换结果", 125, true));
            dataGridView1.Columns.Add(CreateMappingTextColumn(MappingPreviewResultColumn, "验证状态", 110, true));
        }

        private static DataGridViewTextBoxColumn CreateMappingTextColumn(
            string name,
            string headerText,
            int width,
            bool readOnly)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = headerText,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void WireMappingEvents()
        {
            _mappingToolTip = new ToolTip();
            Load += MappingUiForm_Load;
            FormClosed += MappingUiForm_Closed;
            tabControl1.SelectedIndexChanged += MappingTab_SelectedIndexChanged;
            btnNewMappingScript.Click += MappingNewButton_Click;
            listBoxMappingScripts.SelectedIndexChanged += MappingDefinitionList_SelectedIndexChanged;
            _mappingModelCombo.SelectedIndexChanged += MappingModelCombo_SelectedIndexChanged;
            _mappingDetailModelCombo.SelectedIndexChanged += MappingDetailModelCombo_SelectedIndexChanged;
            _mappingParentCidTextBox.TextChanged += MappingParentCidTextBox_TextChanged;
            _mappingNewMasterModelButton.Click += MappingNewMasterModelButton_Click;
            _mappingSheetCombo.SelectedIndexChanged += MappingSheetCombo_SelectedIndexChanged;
            _mappingModeCombo.SelectedIndexChanged += MappingModeCombo_SelectedIndexChanged;
            _mappingImageRootTextBox.TextChanged += MappingImageSetting_Changed;
            _mappingImagePathFieldCombo.SelectedIndexChanged += MappingImageSetting_Changed;
            _mappingCsvEncodingCombo.SelectedIndexChanged += MappingCsvSetting_Changed;
            _mappingCsvDelimiterCombo.SelectedIndexChanged += MappingCsvSetting_Changed;
            _mappingCsvHeaderRowNumber.ValueChanged += MappingCsvSetting_Changed;
            _mappingCsvFirstDataRowNumber.ValueChanged += MappingCsvSetting_Changed;
            _mappingRuleNameTextBox.TextChanged += MappingRuleName_TextChanged;
            _mappingBrowseButton.Click += MappingBrowseButton_Click;
            _mappingPickLocatorButton.Click += MappingPickLocatorButton_Click;
            _mappingFrameTableButton.Click += MappingFrameTableButton_Click;
            _mappingRecordsGrid.CellDoubleClick += MappingRecordsGrid_CellDoubleClick;
            _mappingLocalAssistButton.Click += MappingLocalAssistButton_Click;
            _mappingAiAssistButton.Click += MappingAiAssistButton_Click;
            _mappingSaveButton.Click += MappingSaveButton_Click;
            _mappingDeleteButton.Click += MappingDeleteButton_Click;
            dataGridView1.CellValueChanged += MappingGrid_CellValueChanged;
            dataGridView1.SelectionChanged += MappingGrid_SelectionChanged;
            dataGridView1.CurrentCellDirtyStateChanged += MappingGrid_CurrentCellDirtyStateChanged;
            dataGridView1.DataError += MappingGrid_DataError;
            _mappingSampleGrid.CellClick += MappingSampleGrid_CellClick;
            _mappingSampleGrid.CellMouseDown += MappingSampleGrid_CellMouseDown;
            _mappingSampleGrid.MouseMove += MappingSampleGrid_MouseMove;
            _mappingSampleGrid.MouseUp += MappingSampleGrid_MouseUp;
            _mappingSampleGrid.MouseCaptureChanged += MappingSampleGrid_MouseCaptureChanged;
            _mappingSampleAutoScrollTimer.Tick += MappingSampleAutoScrollTimer_Tick;
        }

        private void MappingUiForm_Load(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabPageMapping)
                ActivateMappingUi();
        }

        private void MappingUiForm_Closed(object sender, FormClosedEventArgs e)
        {
            if (_mappingAiCancellation != null)
            {
                _mappingAiCancellation.Cancel();
                _mappingAiCancellation.Dispose();
                _mappingAiCancellation = null;
            }
            if (_mappingRuleStore != null)
            {
                _mappingRuleStore.Dispose();
                _mappingRuleStore = null;
            }
            if (_mappingSampleAutoScrollTimer != null)
            {
                _mappingSampleAutoScrollTimer.Stop();
                _mappingSampleAutoScrollTimer.Dispose();
                _mappingSampleAutoScrollTimer = null;
            }
        }

        private void MappingTab_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabPageMapping)
                ActivateMappingUi();
        }

        private void ActivateMappingUi()
        {
            if (_mappingDataLoaded)
            {
                RefreshMappingAiConfiguration();
                LoadMappingModels();
                LoadMappingMachineCheckboxes(
                    _mappingCurrentVersion == null ? (long?)null : _mappingCurrentVersion.DefinitionId);
                UpdateMappingCommandState();
                return;
            }
            try
            {
                var connection = new SQLiteConnectionStringBuilder(DatabaseHelper.GetConnectionString());
                _mappingRuleStore = new ParseRuleStore(connection.DataSource);
                _mappingRuleStore.Initialize();
                _mappingPreviewService = new ExcelMappingPreviewService(LoadMappingForbiddenRoots());
                _mappingCsvPreviewService = new CsvMappingPreviewService(LoadMappingForbiddenRoots());
                _mappingLocalAssistant = new LocalMappingAssistant();
                RefreshMappingAiConfiguration();
                LoadMappingModels();
                LoadMappingMachineCheckboxes();
                RefreshMappingDefinitionList(null);
                _mappingDataLoaded = true;

                if (listBoxMappingScripts.Items.Count > 0)
                {
                    _mappingSuppressEvents = true;
                    listBoxMappingScripts.SelectedIndex = 0;
                    _mappingSuppressEvents = false;
                    _mappingSelectedListIndex = 0;
                    LoadMappingVersion(((MappingDefinitionListItem)listBoxMappingScripts.Items[0]).Version);
                }
                else
                {
                    StartNewMapping(false);
                }
            }
            catch (Exception ex)
            {
                SetMappingStatus("映射功能初始化失败：" + ex.Message, Color.Firebrick);
                SetMappingEditorEnabled(false);
            }
            UpdateMappingCommandState();
        }

        private IEnumerable<string> LoadMappingForbiddenRoots()
        {
            List<MachineConfig> machines = MachineConfig.Load();
            if (machines == null || machines.Count == 0)
                machines = SettingsHelper.GenerateDefaultMachineConfigs();

            string[] roots = machines
                .SelectMany(machine => new[] { machine.MonitorPath, machine.SuccessPath, machine.ErrorPath })
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roots.Length == 0)
                throw new MappingValidationException("无法确认采集目录，样本选择功能已按安全策略禁用。");
            return roots;
        }

        private void RefreshMappingAiConfiguration()
        {
            _mappingAiOptions = null;
            _mappingAiUnavailableReason = "AI 自动映射未启用";
            try
            {
                AiMappingConfig config = SettingsHelper.LoadSettings().AiMapping;
                if (config == null || !config.Enabled)
                    return;

                AiMappingClientOptions options = config.ToClientOptions();
                OpenAiCompatibleMappingClient.ValidateOptions(options);
                _mappingAiOptions = options;
                _mappingAiUnavailableReason = null;
            }
            catch (Exception ex)
            {
                _mappingAiUnavailableReason = "AI 配置无效：" + ex.Message;
            }
        }

        private void LoadMappingModels(int? preferredModelId = null)
        {
            MappingModelChoice selectedDetailModel = GetSelectedMappingDetailModel();
            int? preferredDetailModelId = selectedDetailModel == null
                ? (int?)null
                : selectedDetailModel.Id;
            if (!preferredModelId.HasValue)
            {
                MappingModelChoice selectedModel = GetSelectedMappingModel();
                if (selectedModel != null)
                    preferredModelId = selectedModel.Id;
            }

            IReadOnlyList<ModelCatalogItem> models = new ModelCatalogService(DatabaseHelper.GetConnectionString())
                .LoadActiveModels();

            bool previouslySuppressingEvents = _mappingSuppressEvents;
            _mappingSuppressEvents = true;
            _mappingModelCombo.Items.Clear();
            _mappingDetailModelCombo.Items.Clear();
            int selectedIndex = -1;
            int selectedDetailIndex = -1;
            for (int index = 0; index < models.Count; index++)
            {
                ModelCatalogItem model = models[index];
                _mappingModelCombo.Items.Add(new MappingModelChoice
                {
                    Id = model.Id,
                    ModelName = model.ModelName,
                    TableName = model.TableName
                });
                _mappingDetailModelCombo.Items.Add(new MappingModelChoice
                {
                    Id = model.Id,
                    ModelName = model.ModelName,
                    TableName = model.TableName
                });
                if (preferredModelId == model.Id)
                    selectedIndex = index;
                if (preferredDetailModelId == model.Id)
                    selectedDetailIndex = index;
            }
            if (selectedIndex >= 0)
                _mappingModelCombo.SelectedIndex = selectedIndex;
            else if (_mappingModelCombo.Items.Count > 0)
                _mappingModelCombo.SelectedIndex = 0;
            if (selectedDetailIndex >= 0)
                _mappingDetailModelCombo.SelectedIndex = selectedDetailIndex;
            else if (_mappingDetailModelCombo.Items.Count > 1)
                _mappingDetailModelCombo.SelectedIndex = 1;
            else if (_mappingDetailModelCombo.Items.Count > 0)
                _mappingDetailModelCombo.SelectedIndex = 0;
            _mappingSuppressEvents = previouslySuppressingEvents;
        }

        private List<ModelSchemaField> LoadMappingFields(int modelId)
        {
            var fields = new List<ModelSchemaField>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description
FROM ModelFields
WHERE ModelId = @ModelId
ORDER BY Id;";
                command.Parameters.Add("@ModelId", DbType.Int32).Value = modelId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fields.Add(new ModelSchemaField
                        {
                            FieldName = reader.GetString(0),
                            FieldType = reader.GetString(1),
                            FieldLength = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            IsRequired = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                            IsPrimaryKey = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                            IsIdentity = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                            Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                        });
                    }
                }
            }
            return fields;
        }

        private List<MappingMachineChoice> LoadMappingMachines()
        {
            var machines = new List<MappingMachineChoice>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id, MachineName, MachineCode
FROM Machines
ORDER BY SortOrder, Id;";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        machines.Add(new MappingMachineChoice
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Code = reader.GetString(2)
                        });
                    }
                }
            }
            return machines;
        }

        private void LoadMappingMachineCheckboxes(long? definitionId = null)
        {
            if (_mappingMachinePanel == null) return;

            HashSet<string> publishedMachineIds = definitionId.HasValue && _mappingRuleStore != null
                ? new HashSet<string>(
                    _mappingRuleStore.GetPublishedMachineIds(definitionId.Value),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            bool previouslySuppressingEvents = _mappingSuppressEvents;
            _mappingSuppressEvents = true;
            _mappingMachinePanel.SuspendLayout();
            try
            {
                _mappingMachinePanel.Controls.Clear();
                var selectAll = new CheckBox
                {
                    AutoSize = true,
                    Text = "全选",
                    Tag = "all"
                };
                selectAll.CheckedChanged += MappingSelectAllMachines_CheckedChanged;
                _mappingMachinePanel.Controls.Add(selectAll);

                var clearAll = new CheckBox
                {
                    AutoSize = true,
                    Text = "取消全选",
                    Tag = "clear"
                };
                clearAll.CheckedChanged += MappingClearAllMachines_CheckedChanged;
                _mappingMachinePanel.Controls.Add(clearAll);

                foreach (MappingMachineChoice machine in LoadMappingMachines())
                {
                    var checkBox = new CheckBox
                    {
                        AutoSize = true,
                        Text = machine.ToString(),
                        Tag = machine,
                        Checked = publishedMachineIds.Contains(
                            machine.Id.ToString(CultureInfo.InvariantCulture))
                    };
                    checkBox.CheckedChanged += MappingMachine_CheckedChanged;
                    _mappingMachinePanel.Controls.Add(checkBox);
                }
            }
            finally
            {
                _mappingMachinePanel.ResumeLayout(true);
                _mappingSuppressEvents = previouslySuppressingEvents;
            }
        }

        private void MappingSelectAllMachines_CheckedChanged(object sender, EventArgs e)
        {
            var selectAll = sender as CheckBox;
            if (selectAll == null || !selectAll.Checked) return;
            SetAllMappingMachinesChecked(true);
            selectAll.Checked = false;
        }

        private void MappingClearAllMachines_CheckedChanged(object sender, EventArgs e)
        {
            var clearAll = sender as CheckBox;
            if (clearAll == null || !clearAll.Checked) return;
            SetAllMappingMachinesChecked(false);
            clearAll.Checked = false;
        }

        private void SetAllMappingMachinesChecked(bool isChecked)
        {
            foreach (Control control in _mappingMachinePanel.Controls)
            {
                var checkBox = control as CheckBox;
                if (checkBox != null && checkBox.Tag is MappingMachineChoice)
                    checkBox.Checked = isChecked;
            }
            UpdateMappingCommandState();
        }

        private void MappingMachine_CheckedChanged(object sender, EventArgs e)
        {
            if (!_mappingSuppressEvents)
                UpdateMappingCommandState();
        }

        private List<MappingMachineChoice> GetSelectedMappingMachines()
        {
            return _mappingMachinePanel.Controls
                .Cast<Control>()
                .OfType<CheckBox>()
                .Where(checkBox => checkBox.Checked)
                .Select(checkBox => checkBox.Tag as MappingMachineChoice)
                .Where(machine => machine != null)
                .ToList();
        }

        private void RefreshMappingDefinitionList(long? selectedDefinitionId)
        {
            var versions = new List<ParseRuleVersion>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = MappingVersionSelectSql + @"
WHERE v.RuleType = @RuleType
  AND (
      v.VersionNumber = (
          SELECT MAX(v2.VersionNumber)
          FROM ParseRuleVersions v2
          WHERE v2.DefinitionId = v.DefinitionId)
      OR v.VersionNumber = (
          SELECT MAX(v3.VersionNumber)
          FROM ParseRuleVersions v3
          WHERE v3.DefinitionId = v.DefinitionId
            AND v3.Status IN (@ValidatedStatus, @PublishedStatus)))
ORDER BY d.UpdatedTime DESC, d.Id DESC, v.VersionNumber DESC;";
                command.Parameters.Add("@RuleType", DbType.Int32).Value = (int)ParseRuleType.Mapping;
                command.Parameters.Add("@ValidatedStatus", DbType.Int32).Value = (int)ParseRuleStatus.Validated;
                command.Parameters.Add("@PublishedStatus", DbType.Int32).Value = (int)ParseRuleStatus.Published;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        versions.Add(ReadMappingVersion(reader));
                }
            }

            List<MappingDefinitionListItem> items = versions
                .GroupBy(version => version.DefinitionId)
                .Select(group => MappingSavePolicy.SelectPreferredEditorVersion(group))
                .Where(version => version != null)
                .Select(version => new MappingDefinitionListItem(version))
                .ToList();

            _mappingSuppressEvents = true;
            listBoxMappingScripts.BeginUpdate();
            try
            {
                listBoxMappingScripts.Items.Clear();
                foreach (MappingDefinitionListItem item in items)
                    listBoxMappingScripts.Items.Add(item);

                int selectedIndex = -1;
                if (selectedDefinitionId.HasValue)
                {
                    for (int index = 0; index < items.Count; index++)
                    {
                        if (items[index].Version.DefinitionId == selectedDefinitionId.Value)
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }
                listBoxMappingScripts.SelectedIndex = selectedIndex;
                _mappingSelectedListIndex = selectedIndex;
            }
            finally
            {
                listBoxMappingScripts.EndUpdate();
                _mappingSuppressEvents = false;
            }
        }

        private ParseRuleVersion LoadLatestMappingVersion(long definitionId)
        {
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = MappingVersionSelectSql + @"
WHERE d.Id = @DefinitionId
ORDER BY v.VersionNumber DESC
LIMIT 1;";
                command.Parameters.Add("@DefinitionId", DbType.Int64).Value = definitionId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                    return reader.Read() ? ReadMappingVersion(reader) : null;
            }
        }

        private List<ParseRuleVersion> LoadMappingHistory(long definitionId)
        {
            var versions = new List<ParseRuleVersion>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = MappingVersionSelectSql + @"
WHERE d.Id = @DefinitionId
ORDER BY v.VersionNumber DESC;";
                command.Parameters.Add("@DefinitionId", DbType.Int64).Value = definitionId;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        versions.Add(ReadMappingVersion(reader));
                }
            }
            return versions;
        }

        private static readonly string MappingVersionSelectSql = @"
SELECT
    v.Id,
    v.DefinitionId,
    v.VersionNumber,
    v.Revision,
    v.RuleType,
    v.Status,
    d.RuleName,
    d.ModelId,
    d.TargetModelType,
    d.NormalizedExtension,
    v.DefinitionJson,
    v.DerivedScriptCode,
    v.ContentSha256,
    v.ModelSchemaHash,
    v.ValidationSummary,
    v.CreatedTime,
    v.ValidatedTime,
    v.PublishedTime
FROM ParseRuleVersions v
INNER JOIN ParseRuleDefinitions d ON d.Id = v.DefinitionId
";

        private static ParseRuleVersion ReadMappingVersion(SQLiteDataReader reader)
        {
            return new ParseRuleVersion
            {
                Id = reader.GetInt64(0),
                DefinitionId = reader.GetInt64(1),
                VersionNumber = reader.GetInt32(2),
                Revision = reader.GetInt32(3),
                RuleType = (ParseRuleType)reader.GetInt32(4),
                Status = (ParseRuleStatus)reader.GetInt32(5),
                RuleName = reader.GetString(6),
                ModelId = reader.GetInt32(7),
                TargetModelType = reader.GetString(8),
                NormalizedExtension = reader.GetString(9),
                DefinitionJson = reader.GetString(10),
                DerivedScriptCode = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                ContentSha256 = reader.GetString(12),
                ModelSchemaHash = reader.GetString(13),
                ValidationSummary = reader.IsDBNull(14) ? null : reader.GetString(14),
                CreatedTime = reader.GetDateTime(15),
                ValidatedTime = reader.IsDBNull(16) ? (DateTime?)null : reader.GetDateTime(16),
                PublishedTime = reader.IsDBNull(17) ? (DateTime?)null : reader.GetDateTime(17)
            };
        }

        private void MappingNewButton_Click(object sender, EventArgs e)
        {
            StartNewMapping(true);
        }

        private void StartNewMapping(bool confirmDiscard)
        {
            if (confirmDiscard && !ConfirmDiscardMappingChanges()) return;
            CancelMappingPointSelection(false);

            _mappingSuppressEvents = true;
            listBoxMappingScripts.SelectedIndex = -1;
            _mappingSelectedListIndex = -1;
            _mappingCurrentVersion = null;
            _mappingCurrentDefinition = null;
            _mappingTemplateSignature = null;
            _mappingRepeatedRows = null;
            _mappingSnapshot = null;
            _mappingSamplePathTextBox.Clear();
            _mappingSheetCombo.Items.Clear();
            _mappingExtensionLabel.Text = "扩展名：-";
            SelectMappingMode(MappingRecordMode.SingleRecord);
            _mappingParentCidTextBox.Text = "PARENT_CID";
            _mappingImageRootTextBox.Clear();
            _mappingImagePathFieldCombo.Items.Clear();
            SelectCsvOptions(null);
            UpdateSourceMappingControls();
            LoadMappingMachineCheckboxes();
            _mappingModelCombo.Enabled = true;
            if (_mappingModelCombo.Items.Count > 0 && _mappingModelCombo.SelectedIndex < 0)
                _mappingModelCombo.SelectedIndex = 0;
            MappingModelChoice model = GetSelectedMappingModel();
            _mappingRuleNameTextBox.Text = model == null ? string.Empty : model.ModelName + " 表格映射";
            _mappingSuppressEvents = false;

            PopulateMappingRows(model == null ? 0 : model.Id, null);
            AcceptMappingEditorStateAsClean();
            ClearMappingPreviewColumns();
            SetMappingStatus("新映射：请选择只读 Excel 或 CSV 样本并配置字段定位", Color.DimGray);
            UpdateMappingCommandState();
        }

        private void MappingDefinitionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || listBoxMappingScripts.SelectedIndex < 0) return;

            int requestedIndex = listBoxMappingScripts.SelectedIndex;
            RefreshMappingDirtyStateFromEditor();
            if (_mappingDirty && requestedIndex != _mappingSelectedListIndex)
            {
                DialogResult result = MessageBox.Show(
                    "当前映射有未保存修改。是否放弃修改并切换？",
                    "未保存修改",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    _mappingSuppressEvents = true;
                    listBoxMappingScripts.SelectedIndex = _mappingSelectedListIndex;
                    _mappingSuppressEvents = false;
                    return;
                }
            }

            var item = listBoxMappingScripts.SelectedItem as MappingDefinitionListItem;
            if (item == null) return;
            _mappingSelectedListIndex = requestedIndex;
            LoadMappingVersion(item.Version);
        }

        private bool ConfirmDiscardMappingChanges()
        {
            RefreshMappingDirtyStateFromEditor();
            if (!_mappingDirty) return true;
            return MessageBox.Show(
                "当前映射有未保存修改。是否放弃这些修改？",
                "未保存修改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void LoadMappingVersion(ParseRuleVersion version)
        {
            if (version == null) return;
            try
            {
                CancelMappingPointSelection(false);
                MappingRuleDefinition definition = MappingRuleSerializer.Deserialize(version.DefinitionJson);
                _mappingSuppressEvents = true;
                _mappingCurrentVersion = version;
                _mappingCurrentDefinition = definition;
                _mappingTemplateSignature = definition.TemplateSignature;
                _mappingRepeatedRows = definition.RecordMode == MappingRecordMode.MasterDetail &&
                    definition.MasterDetail != null && definition.MasterDetail.Detail != null
                    ? definition.MasterDetail.Detail.RepeatedRows
                    : definition.RepeatedRows;
                SelectMappingMode(definition.RecordMode);

                SelectMappingModel(definition.ModelId);
                if (definition.RecordMode == MappingRecordMode.MasterDetail && definition.MasterDetail != null)
                {
                    SelectMappingModelInCombo(
                        _mappingDetailModelCombo,
                        definition.MasterDetail.Detail.ModelId);
                    _mappingParentCidTextBox.Text = definition.MasterDetail.ParentCidField;
                }
                if (definition.RecordMode == MappingRecordMode.ImageFileName && definition.ImageArchive != null)
                {
                    _mappingImageRootTextBox.Text = definition.ImageArchive.SharedRootPath;
                    PopulateImagePathFieldChoices(definition.ImageArchive.PathTargetField);
                }
                else
                {
                    _mappingImageRootTextBox.Clear();
                    _mappingImagePathFieldCombo.Items.Clear();
                }
                SelectCsvOptions(definition.CsvOptions);
                UpdateSourceMappingControls();
                _mappingModelCombo.Enabled = false;
                _mappingRuleNameTextBox.Text = definition.RuleName;

                if (_mappingSnapshot != null &&
                    !string.Equals(_mappingSnapshot.FileExtension, definition.NormalizedExtension, StringComparison.Ordinal))
                {
                    _mappingSnapshot = null;
                    _mappingSamplePathTextBox.Clear();
                    _mappingSampleGrid.Rows.Clear();
                }
                _mappingExtensionLabel.Text = "扩展名：" + definition.NormalizedExtension;
                LoadMappingSheetChoices(definition.SheetName);
                PopulateMappingRows(definition.ModelId, definition);
                LoadMappingMachineCheckboxes(version.DefinitionId);
                _mappingSuppressEvents = false;

                AcceptMappingEditorStateAsClean();
                ClearMappingPreviewColumns();
                UpdateMappingVersionStatus();
                UpdateMappingCommandState();
            }
            catch (Exception ex)
            {
                _mappingSuppressEvents = false;
                SetMappingStatus("加载映射失败：" + ex.Message, Color.Firebrick);
            }
        }

        private void SelectMappingModel(int modelId)
        {
            for (int index = 0; index < _mappingModelCombo.Items.Count; index++)
            {
                var model = _mappingModelCombo.Items[index] as MappingModelChoice;
                if (model != null && model.Id == modelId)
                {
                    _mappingModelCombo.SelectedIndex = index;
                    return;
                }
            }
            _mappingModelCombo.SelectedIndex = -1;
        }

        private static void SelectMappingModelInCombo(ComboBox combo, int modelId)
        {
            for (int index = 0; index < combo.Items.Count; index++)
            {
                var model = combo.Items[index] as MappingModelChoice;
                if (model != null && model.Id == modelId)
                {
                    combo.SelectedIndex = index;
                    return;
                }
            }
            combo.SelectedIndex = -1;
        }

        private void PopulateMappingRows(int modelId, MappingRuleDefinition definition)
        {
            _mappingApplyingSuggestion = true;
            dataGridView1.Rows.Clear();
            try
            {
                if (GetSelectedMappingMode() == MappingRecordMode.MasterDetail)
                {
                    MappingModelChoice master = GetSelectedMappingModel();
                    MappingModelChoice detail = GetSelectedMappingDetailModel();
                    MappingTargetDefinition persistedMaster = definition == null || definition.MasterDetail == null
                        ? null
                        : definition.MasterDetail.Master;
                    MappingTargetDefinition persistedDetail = definition == null || definition.MasterDetail == null
                        ? null
                        : definition.MasterDetail.Detail;
                    PopulateMappingTargetRows(
                        master == null ? 0 : master.Id,
                        persistedMaster == null ? null : persistedMaster.Fields,
                        true,
                        null);
                    PopulateMappingTargetRows(
                        detail == null ? 0 : detail.Id,
                        persistedDetail == null ? null : persistedDetail.Fields,
                        false,
                        _mappingParentCidTextBox.Text.Trim());
                    return;
                }

                PopulateMappingTargetRows(modelId, definition == null ? null : definition.Fields, false, null);
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
            UpdateImagePathFieldRowState();
        }

        private void PopulateMappingTargetRows(
            int modelId,
            IEnumerable<FieldMappingRule> persistedRules,
            bool isMaster,
            string excludedField)
        {
                List<ModelSchemaField> schemaFields = modelId > 0
                    ? LoadMappingFields(modelId)
                    : new List<ModelSchemaField>();
                var persisted = (persistedRules ?? Enumerable.Empty<FieldMappingRule>())
                    .Where(field => field != null && !string.IsNullOrWhiteSpace(field.TargetField))
                    .ToDictionary(field => field.TargetField, StringComparer.Ordinal);
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (ModelSchemaField field in schemaFields.Where(field =>
                    !string.Equals(field.FieldName, "CID", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(field.FieldName, excludedField, StringComparison.OrdinalIgnoreCase)))
                {
                    FieldMappingRule rule;
                    persisted.TryGetValue(field.FieldName, out rule);
                    AddMappingRow(field, rule, false, isMaster);
                    fieldNames.Add(field.FieldName);
                }

                foreach (FieldMappingRule orphan in persisted.Values.Where(field => !fieldNames.Contains(field.TargetField)))
                {
                    AddMappingRow(new ModelSchemaField
                    {
                        FieldName = orphan.TargetField,
                        FieldType = orphan.TargetType,
                        IsRequired = orphan.IsRequired,
                        Description = orphan.TargetDescription
                    }, orphan, true, isMaster);
            }
        }

        private void AddMappingRow(ModelSchemaField field, FieldMappingRule rule, bool orphan, bool isMaster)
        {
            bool fileNameOnly = isMaster || GetSelectedMappingMode() == MappingRecordMode.ImageFileName;
            int index = dataGridView1.Rows.Add();
            DataGridViewRow row = dataGridView1.Rows[index];
            row.Cells[MappingTargetFieldColumn].Value = field.FieldName;
            row.Cells[MappingTargetTypeColumn].Value = field.FieldType;
            row.Cells[MappingRequiredColumn].Value = field.IsRequired;
            row.Cells[MappingDescriptionColumn].Value = field.Description ?? string.Empty;
            row.Cells[MappingRoleColumn].Value = isMaster
                ? "主表"
                : (GetSelectedMappingMode() == MappingRecordMode.MasterDetail ? "子表" : "单表");
            MappingFieldScope fieldScope = rule == null ? MappingFieldScope.Common : rule.Scope;
            row.Cells[MappingScopeColumn].Value = fieldScope == MappingFieldScope.RowColumn
                ? "明细列"
                : "公共字段";
            row.Cells[MappingKeyColumn].Value = fieldScope == MappingFieldScope.RowColumn &&
                _mappingRepeatedRows != null && rule != null && rule.Locator != null &&
                rule.Locator.ColumnOffset == _mappingRepeatedRows.KeyColumnOffset;
            if (fileNameOnly)
            {
                row.Cells[MappingScopeColumn].ReadOnly = true;
                row.Cells[MappingKeyColumn].ReadOnly = true;
                row.Cells[MappingScopeColumn].Style.BackColor = SystemColors.Control;
                row.Cells[MappingKeyColumn].Style.BackColor = SystemColors.Control;
            }
            row.Cells[MappingLocatorTypeColumn].Value = rule == null || rule.Locator == null
                ? (fileNameOnly ? "fileNameFull" : "labelOffset")
                : rule.Locator.Type;
            row.Cells[MappingLocatorValueColumn].Value = rule == null || rule.Locator == null
                ? string.Empty
                : (rule.Locator.Type == "cell"
                    ? rule.Locator.Cell
                    : (rule.Locator.Type == "fileNameSegment"
                        ? (rule.Locator.SegmentIndex + 1).ToString(CultureInfo.InvariantCulture)
                        : rule.Locator.Text));
            row.Cells[MappingRowOffsetColumn].Value = rule == null || rule.Locator == null
                ? "0"
                : rule.Locator.RowOffset.ToString(CultureInfo.InvariantCulture);
            row.Cells[MappingColumnOffsetColumn].Value = rule == null || rule.Locator == null
                ? "1"
                : rule.Locator.ColumnOffset.ToString(CultureInfo.InvariantCulture);
            row.Cells[MappingValueColumnColumn].Value = rule == null || rule.Locator == null
                ? string.Empty
                : rule.Locator.ValueColumn;
            row.Cells[MappingDataRowOffsetColumn].Value = rule == null || rule.Locator == null
                ? "1"
                : rule.Locator.DataRowOffset.ToString(CultureInfo.InvariantCulture);
            row.Cells[MappingTransformsColumn].Value = rule == null
                ? string.Empty
                : string.Join(",", (rule.Transforms ?? new List<string>())
                    .Where(transform => !field.IsRequired ||
                                        !string.Equals(transform, "default", StringComparison.Ordinal)));
            row.Cells[MappingValueMapColumn].Value = rule == null
                ? string.Empty
                : FormatMappingValueMap(rule.ExactValueMap);
            row.Cells[MappingDefaultValueColumn].Value = field.IsRequired || rule == null
                ? string.Empty
                : rule.DefaultValue;
            row.Cells[MappingDefaultValueColumn].ReadOnly = field.IsRequired;
            if (field.IsRequired)
                row.Cells[MappingDefaultValueColumn].Style.BackColor = SystemColors.Control;

            MappingConfirmationState confirmation = rule == null
                ? MappingConfirmationState.Unconfirmed
                : rule.ConfirmationState;
            var metadata = new MappingRowMetadata
            {
                ConfirmationState = confirmation,
                IsOrphan = orphan,
                AnchorCell = rule == null || rule.Locator == null ? null : rule.Locator.AnchorCell,
                AnchorText = rule == null || rule.Locator == null ? null : rule.Locator.AnchorText,
                IsMaster = isMaster
            };
            row.Tag = metadata;
            row.Cells[MappingHumanConfirmedColumn].Value = confirmation == MappingConfirmationState.HumanConfirmed;
            UpdateMappingConfirmationCell(row);
            if (orphan)
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose;
                row.Cells[MappingPreviewResultColumn].Value = "模型中已不存在";
            }
        }

        private void MappingModelCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || _mappingCurrentDefinition != null) return;
            MappingModelChoice model = GetSelectedMappingModel();
            if (GetSelectedMappingMode() == MappingRecordMode.ImageFileName)
                PopulateImagePathFieldChoices(null);
            PopulateMappingRows(model == null ? 0 : model.Id, null);
            if (model != null)
                _mappingRuleNameTextBox.Text = model.ModelName +
                    (GetSelectedMappingMode() == MappingRecordMode.ImageFileName
                        ? " 图片文件名映射"
                        : (IsCurrentCsvSource() ? " CSV 映射" : " Excel 映射"));
            MarkMappingDirty();
        }

        private void MappingDetailModelCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || _mappingCurrentDefinition != null ||
                GetSelectedMappingMode() != MappingRecordMode.MasterDetail) return;
            MappingModelChoice model = GetSelectedMappingModel();
            PopulateMappingRows(model == null ? 0 : model.Id, null);
            MarkMappingDirty();
        }

        private void MappingParentCidTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || GetSelectedMappingMode() != MappingRecordMode.MasterDetail) return;
            MarkMappingDirty();
        }

        private void MappingNewMasterModelButton_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscardMappingChanges()) return;
            tabControl1.SelectedTab = tabPageModel;
            btnAddModel.PerformClick();
        }

        private void MappingModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents) return;
            CancelMappingPointSelection(false);
            MappingRecordMode mode = GetSelectedMappingMode();
            if (mode == MappingRecordMode.SingleRecord)
                SetMappingStatus("单条记录模式：逐个配置公共字段定位。", Color.DimGray);
            else if (mode == MappingRecordMode.RepeatingRows)
                SetMappingStatus("重复行模式：在上方样本网格框选一行表头和首条数据，再点击“框选表格”。", Color.RoyalBlue);
            else if (mode == MappingRecordMode.MasterDetail)
                SetMappingStatus("主子表模式：主表字段来自文件名，子表字段来自表格重复行。", Color.RoyalBlue);
            else
                SetMappingStatus("图片文件名模式：字段来自图片文件名，图片复制到共享目录后写入路径字段。", Color.RoyalBlue);
            _mappingSnapshot = null;
            _mappingSamplePathTextBox.Clear();
            _mappingSheetCombo.Items.Clear();
            _mappingExtensionLabel.Text = "扩展名：-";
            if (mode == MappingRecordMode.ImageFileName)
                PopulateImagePathFieldChoices(null);
            UpdateSourceMappingControls();
            MappingModelChoice model = GetSelectedMappingModel();
            PopulateMappingRows(model == null ? 0 : model.Id, null);
            MarkMappingDirty();
        }

        private void MappingImageSetting_Changed(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || GetSelectedMappingMode() != MappingRecordMode.ImageFileName) return;
            if (ReferenceEquals(sender, _mappingImagePathFieldCombo))
                UpdateImagePathFieldRowState();
            MarkMappingDirty();
        }

        private void UpdateImagePathFieldRowState()
        {
            if (dataGridView1 == null) return;
            bool imageMode = GetSelectedMappingMode() == MappingRecordMode.ImageFileName;
            string pathField = imageMode
                ? Convert.ToString(_mappingImagePathFieldCombo.SelectedItem, CultureInfo.InvariantCulture)
                : null;
            string[] editableColumns =
            {
                MappingLocatorTypeColumn,
                MappingLocatorValueColumn,
                MappingRowOffsetColumn,
                MappingColumnOffsetColumn,
                MappingValueColumnColumn,
                MappingDataRowOffsetColumn,
                MappingTransformsColumn,
                MappingValueMapColumn,
                MappingHumanConfirmedColumn
            };
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                var metadata = row.Tag as MappingRowMetadata;
                bool systemManaged = imageMode && string.Equals(
                    CellText(row, MappingTargetFieldColumn),
                    pathField,
                    StringComparison.Ordinal);
                foreach (string column in editableColumns)
                {
                    row.Cells[column].ReadOnly = systemManaged;
                    row.Cells[column].Style.BackColor = systemManaged
                        ? SystemColors.Control
                        : SystemColors.Window;
                }
                row.Cells[MappingDefaultValueColumn].ReadOnly = systemManaged ||
                    Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false);
                if (systemManaged)
                {
                    row.Cells[MappingPreviewCellColumn].Value = "系统";
                    row.Cells[MappingPreviewValueColumn].Value = string.Empty;
                    row.Cells[MappingPreviewConvertedValueColumn].Value = string.Empty;
                    row.Cells[MappingPreviewResultColumn].Value = "运行时写入共享路径";
                    row.Cells[MappingPreviewResultColumn].Style.ForeColor = Color.RoyalBlue;
                }
                else if (metadata == null || !metadata.IsOrphan)
                {
                    ClearMappingPreviewRow(row);
                }
            }
        }

        private void PopulateImagePathFieldChoices(string preferredField)
        {
            bool previous = _mappingSuppressEvents;
            _mappingSuppressEvents = true;
            try
            {
                string selected = preferredField ?? Convert.ToString(
                    _mappingImagePathFieldCombo.SelectedItem,
                    CultureInfo.InvariantCulture);
                _mappingImagePathFieldCombo.Items.Clear();
                MappingModelChoice model = GetSelectedMappingModel();
                if (model != null)
                {
                    foreach (ModelSchemaField field in LoadMappingFields(model.Id).Where(field =>
                        string.Equals(field.FieldType, "string", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(field.FieldName, "CID", StringComparison.OrdinalIgnoreCase)))
                        _mappingImagePathFieldCombo.Items.Add(field.FieldName);
                }
                int index = string.IsNullOrWhiteSpace(selected)
                    ? -1
                    : _mappingImagePathFieldCombo.Items.IndexOf(selected);
                _mappingImagePathFieldCombo.SelectedIndex = index >= 0
                    ? index
                    : (_mappingImagePathFieldCombo.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                _mappingSuppressEvents = previous;
            }
        }

        private void UpdateSourceMappingControls()
        {
            bool imageVisible = GetSelectedMappingMode() == MappingRecordMode.ImageFileName;
            _mappingImageRootLabel.Visible = imageVisible;
            _mappingImageRootTextBox.Visible = imageVisible;
            _mappingImagePathFieldLabel.Visible = imageVisible;
            _mappingImagePathFieldCombo.Visible = imageVisible;

            bool csvVisible = !imageVisible && IsCurrentCsvSource();
            _mappingCsvEncodingLabel.Visible = csvVisible;
            _mappingCsvEncodingCombo.Visible = csvVisible;
            _mappingCsvDelimiterLabel.Visible = csvVisible;
            _mappingCsvDelimiterCombo.Visible = csvVisible;
            _mappingCsvHeaderRowLabel.Visible = csvVisible;
            _mappingCsvHeaderRowNumber.Visible = csvVisible;
            _mappingCsvFirstDataRowLabel.Visible = csvVisible;
            _mappingCsvFirstDataRowNumber.Visible = csvVisible;
        }

        private bool IsCurrentCsvSource()
        {
            string extension = _mappingSnapshot == null
                ? (_mappingCurrentDefinition == null ? null : _mappingCurrentDefinition.NormalizedExtension)
                : _mappingSnapshot.FileExtension;
            if (string.IsNullOrWhiteSpace(extension) &&
                _mappingSamplePathTextBox != null &&
                !string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text))
                extension = Path.GetExtension(_mappingSamplePathTextBox.Text).ToLowerInvariant();
            return string.Equals(extension, ".csv", StringComparison.Ordinal);
        }

        private CsvMappingOptions GetCsvOptionsFromEditor()
        {
            string encoding;
            switch (_mappingCsvEncodingCombo.SelectedIndex)
            {
                case 1: encoding = "gb18030"; break;
                case 2: encoding = "gbk"; break;
                default: encoding = "utf-8"; break;
            }

            string delimiter;
            switch (_mappingCsvDelimiterCombo.SelectedIndex)
            {
                case 1: delimiter = ";"; break;
                case 2: delimiter = "\t"; break;
                case 3: delimiter = "|"; break;
                default: delimiter = ","; break;
            }
            return new CsvMappingOptions
            {
                EncodingName = encoding,
                Delimiter = delimiter,
                QuoteCharacter = '"',
                HeaderRowNumber = Convert.ToInt32(_mappingCsvHeaderRowNumber.Value, CultureInfo.InvariantCulture),
                FirstDataRowNumber = Convert.ToInt32(_mappingCsvFirstDataRowNumber.Value, CultureInfo.InvariantCulture),
                SkipBlankRows = true
            };
        }

        private void SelectCsvOptions(CsvMappingOptions options)
        {
            CsvMappingOptions selected = options ?? new CsvMappingOptions();
            string encoding = (selected.EncodingName ?? string.Empty).Trim().ToLowerInvariant();
            _mappingCsvEncodingCombo.SelectedIndex = encoding == "gb18030"
                ? 1
                : (encoding == "gbk" ? 2 : 0);
            string delimiter = selected.Delimiter ?? ",";
            _mappingCsvDelimiterCombo.SelectedIndex = delimiter == ";"
                ? 1
                : (delimiter == "\t" ? 2 : (delimiter == "|" ? 3 : 0));
            _mappingCsvHeaderRowNumber.Value = Math.Max(
                _mappingCsvHeaderRowNumber.Minimum,
                Math.Min(_mappingCsvHeaderRowNumber.Maximum, selected.HeaderRowNumber));
            _mappingCsvFirstDataRowNumber.Value = Math.Max(
                _mappingCsvFirstDataRowNumber.Minimum,
                Math.Min(_mappingCsvFirstDataRowNumber.Maximum, selected.FirstDataRowNumber));
        }

        private void MappingCsvSetting_Changed(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || !IsCurrentCsvSource()) return;
            try
            {
                if (!string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text) &&
                    File.Exists(_mappingSamplePathTextBox.Text))
                {
                    _mappingSnapshot = _mappingCsvPreviewService.Inspect(
                        _mappingSamplePathTextBox.Text,
                        GetCsvOptionsFromEditor());
                    LoadMappingSheetChoices(CsvMappingPreviewService.VirtualSheetName);
                    ClearMappingPreviewColumns();
                }
                MarkMappingDirty();
                SetMappingStatus("CSV 读取选项已更改，请重新验证后保存。", Color.DarkOrange);
            }
            catch (Exception ex)
            {
                _mappingSnapshot = null;
                ClearMappingPreviewColumns();
                SetMappingStatus("CSV 样本读取失败：" + ex.Message, Color.Firebrick);
            }
        }

        private MappingRecordMode GetSelectedMappingMode()
        {
            var choice = _mappingModeCombo == null ? null : _mappingModeCombo.SelectedItem as MappingModeChoice;
            return choice == null ? MappingRecordMode.SingleRecord : choice.Mode;
        }

        private void SelectMappingMode(MappingRecordMode mode)
        {
            if (_mappingModeCombo == null) return;
            for (int index = 0; index < _mappingModeCombo.Items.Count; index++)
            {
                var choice = _mappingModeCombo.Items[index] as MappingModeChoice;
                if (choice != null && choice.Mode == mode)
                {
                    _mappingModeCombo.SelectedIndex = index;
                    return;
                }
            }
            _mappingModeCombo.SelectedIndex = 0;
        }

        private void MappingSheetCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents) return;
            CancelMappingPointSelection(false);
            PopulateMappingSampleGrid();
            MarkMappingDirty();
        }

        private void MappingRuleName_TextChanged(object sender, EventArgs e)
        {
            if (!_mappingSuppressEvents)
                MarkMappingDirty();
        }

        private void MappingGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents) return;
            if (_mappingPickTargetRowIndex >= 0 &&
                (dataGridView1.CurrentRow == null ||
                 dataGridView1.CurrentRow.Index != _mappingPickTargetRowIndex))
            {
                CancelMappingPointSelection(true);
            }
            UpdateMappingCommandState();
        }

        private void MappingPickLocatorButton_Click(object sender, EventArgs e)
        {
            if (_mappingPickTargetRowIndex >= 0)
            {
                CancelMappingPointSelection(true);
                return;
            }
            if (_mappingSnapshot == null || GetSelectedMappingSheet() == null)
            {
                MessageBox.Show("请先选择表格样本和工作表。", "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (dataGridView1.CurrentRow == null)
            {
                MessageBox.Show("请先在字段映射网格中选中目标字段。", "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (GetMappingFieldScope(dataGridView1.CurrentRow) == MappingFieldScope.RowColumn)
            {
                MessageBox.Show("明细列请在样本网格中框选表头和首条数据后，点击“框选表格”统一配置。",
                    "框选表格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string locatorType = CellText(dataGridView1.CurrentRow, MappingLocatorTypeColumn);
            if (!MappingRuleSerializer.AllowedLocatorTypes.Contains(locatorType))
            {
                MessageBox.Show("请先为目标字段选择有效的定位方式。", "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _mappingPickTargetRowIndex = dataGridView1.CurrentRow.Index;
            _mappingPickAnchorSampleRowIndex = -1;
            _mappingPickAnchorSampleColumnIndex = -1;
            _mappingPickAnchorCell = null;
            _mappingPickLocatorButton.Text = "取消点选";
            _mappingSampleGrid.Focus();
            SetMappingStatus(
                locatorType == "cell"
                    ? "点选定位 1/2：先点锚点作为区域参照，再点实际值单元格"
                    : "点选定位 1/2：请在只读样本网格中点击标签/表头锚点",
                Color.RoyalBlue);
        }

        private void MappingSampleGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            StopMappingSampleDrag(false);
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0 ||
                (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
                return;

            _mappingSampleDragAnchorRowIndex = e.RowIndex;
            _mappingSampleDragAnchorColumnIndex = e.ColumnIndex;
            _mappingSampleDragEndRowIndex = e.RowIndex;
            _mappingSampleDragEndColumnIndex = e.ColumnIndex;
            Rectangle cellBounds = _mappingSampleGrid.GetCellDisplayRectangle(
                e.ColumnIndex,
                e.RowIndex,
                false);
            _mappingSampleMouseDownPoint = new Point(cellBounds.Left + e.X, cellBounds.Top + e.Y);
        }

        private void MappingSampleGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_mappingSampleDragAnchorRowIndex < 0 || _mappingSampleDragAnchorColumnIndex < 0)
                return;
            if ((e.Button & MouseButtons.Left) == 0)
            {
                StopMappingSampleDrag();
                return;
            }

            if (!_mappingSampleDragging)
            {
                Size dragSize = SystemInformation.DragSize;
                var dragStartBounds = new Rectangle(
                    _mappingSampleMouseDownPoint.X - dragSize.Width / 2,
                    _mappingSampleMouseDownPoint.Y - dragSize.Height / 2,
                    dragSize.Width,
                    dragSize.Height);
                if (dragStartBounds.Contains(e.Location)) return;

                _mappingSampleDragging = true;
                _mappingSampleGrid.Capture = true;
                _mappingSampleAutoScrollTimer.Start();
            }

            ExtendMappingSampleSelection(e.Location, Point.Empty);
        }

        private void MappingSampleGrid_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                StopMappingSampleDrag();
        }

        private void MappingSampleGrid_MouseCaptureChanged(object sender, EventArgs e)
        {
            if (_mappingSampleDragging &&
                !_mappingSampleGrid.Capture &&
                (Control.MouseButtons & MouseButtons.Left) == 0)
                StopMappingSampleDrag();
        }

        private void MappingSampleAutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_mappingSampleDragging ||
                _mappingSampleGrid == null ||
                _mappingSampleGrid.IsDisposed ||
                (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                StopMappingSampleDrag();
                return;
            }

            Point pointer = _mappingSampleGrid.PointToClient(Cursor.Position);
            Rectangle viewport = GetMappingSampleViewport();
            Point direction = GetMappingSampleAutoScrollDirection(
                pointer,
                viewport,
                MappingSampleAutoScrollEdge);
            if (direction == Point.Empty) return;

            MoveMappingSampleViewport(direction);
            ExtendMappingSampleSelection(pointer, direction);
        }

        private Rectangle GetMappingSampleViewport()
        {
            Rectangle client = _mappingSampleGrid.ClientRectangle;
            int left = client.Left + (_mappingSampleGrid.RowHeadersVisible
                ? _mappingSampleGrid.RowHeadersWidth
                : 0);
            int top = client.Top + (_mappingSampleGrid.ColumnHeadersVisible
                ? _mappingSampleGrid.ColumnHeadersHeight
                : 0);
            int right = Math.Max(left + 1, client.Right - SystemInformation.VerticalScrollBarWidth);
            int bottom = Math.Max(top + 1, client.Bottom - SystemInformation.HorizontalScrollBarHeight);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static Point GetMappingSampleAutoScrollDirection(
            Point pointer,
            Rectangle viewport,
            int edgeSize)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0 || edgeSize <= 0)
                return Point.Empty;

            int horizontal = 0;
            if (pointer.X <= viewport.Left + edgeSize)
                horizontal = -1;
            else if (pointer.X >= viewport.Right - edgeSize)
                horizontal = 1;

            int vertical = 0;
            if (pointer.Y <= viewport.Top + edgeSize)
                vertical = -1;
            else if (pointer.Y >= viewport.Bottom - edgeSize)
                vertical = 1;

            return new Point(horizontal, vertical);
        }

        private void MoveMappingSampleViewport(Point direction)
        {
            if (direction.X != 0 && _mappingSampleGrid.Columns.Count > 0)
            {
                int firstColumn = _mappingSampleGrid.FirstDisplayedScrollingColumnIndex;
                int nextColumn = FindNextVisibleMappingSampleColumn(firstColumn, direction.X);
                if (nextColumn >= 0)
                    _mappingSampleGrid.FirstDisplayedScrollingColumnIndex = nextColumn;
            }

            if (direction.Y != 0 && _mappingSampleGrid.Rows.Count > 0)
            {
                int firstRow = _mappingSampleGrid.FirstDisplayedScrollingRowIndex;
                int nextRow = FindNextVisibleMappingSampleRow(firstRow, direction.Y);
                if (nextRow >= 0)
                    _mappingSampleGrid.FirstDisplayedScrollingRowIndex = nextRow;
            }
        }

        private int FindNextVisibleMappingSampleColumn(int currentIndex, int direction)
        {
            int index = currentIndex < 0
                ? (direction > 0 ? -1 : _mappingSampleGrid.Columns.Count)
                : currentIndex;
            for (index += direction; index >= 0 && index < _mappingSampleGrid.Columns.Count; index += direction)
            {
                DataGridViewColumn column = _mappingSampleGrid.Columns[index];
                if (column.Visible && !column.Frozen) return index;
            }
            return -1;
        }

        private int FindNextVisibleMappingSampleRow(int currentIndex, int direction)
        {
            int index = currentIndex < 0
                ? (direction > 0 ? -1 : _mappingSampleGrid.Rows.Count)
                : currentIndex;
            for (index += direction; index >= 0 && index < _mappingSampleGrid.Rows.Count; index += direction)
            {
                if (_mappingSampleGrid.Rows[index].Visible) return index;
            }
            return -1;
        }

        private void ExtendMappingSampleSelection(Point pointer, Point direction)
        {
            if (_mappingSampleDragAnchorRowIndex < 0 || _mappingSampleDragAnchorColumnIndex < 0)
                return;

            Rectangle viewport = GetMappingSampleViewport();
            int x = Math.Max(viewport.Left + 1, Math.Min(pointer.X, viewport.Right - 2));
            int y = Math.Max(viewport.Top + 1, Math.Min(pointer.Y, viewport.Bottom - 2));
            DataGridView.HitTestInfo hit = _mappingSampleGrid.HitTest(x, y);
            if (hit.RowIndex >= 0 && hit.ColumnIndex >= 0)
            {
                _mappingSampleDragEndRowIndex = hit.RowIndex;
                _mappingSampleDragEndColumnIndex = hit.ColumnIndex;
            }
            else
            {
                _mappingSampleDragEndRowIndex = Math.Max(
                    0,
                    Math.Min(
                        _mappingSampleGrid.Rows.Count - 1,
                        _mappingSampleDragEndRowIndex + direction.Y));
                _mappingSampleDragEndColumnIndex = Math.Max(
                    0,
                    Math.Min(
                        _mappingSampleGrid.Columns.Count - 1,
                        _mappingSampleDragEndColumnIndex + direction.X));
            }

            SelectMappingSampleRange(
                _mappingSampleDragAnchorRowIndex,
                _mappingSampleDragAnchorColumnIndex,
                _mappingSampleDragEndRowIndex,
                _mappingSampleDragEndColumnIndex);
        }

        private void SelectMappingSampleRange(
            int anchorRowIndex,
            int anchorColumnIndex,
            int endRowIndex,
            int endColumnIndex)
        {
            int firstRow = Math.Min(anchorRowIndex, endRowIndex);
            int lastRow = Math.Max(anchorRowIndex, endRowIndex);
            int firstColumn = Math.Min(anchorColumnIndex, endColumnIndex);
            int lastColumn = Math.Max(anchorColumnIndex, endColumnIndex);
            if (firstRow < 0 || lastRow >= _mappingSampleGrid.Rows.Count ||
                firstColumn < 0 || lastColumn >= _mappingSampleGrid.Columns.Count)
                return;

            _mappingSampleGrid.ClearSelection();
            for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
            {
                for (int columnIndex = firstColumn; columnIndex <= lastColumn; columnIndex++)
                    _mappingSampleGrid.Rows[rowIndex].Cells[columnIndex].Selected = true;
            }
        }

        private void StopMappingSampleDrag(bool releaseCapture = true)
        {
            if (_mappingSampleAutoScrollTimer != null)
                _mappingSampleAutoScrollTimer.Stop();
            _mappingSampleDragging = false;
            _mappingSampleDragAnchorRowIndex = -1;
            _mappingSampleDragAnchorColumnIndex = -1;
            _mappingSampleDragEndRowIndex = -1;
            _mappingSampleDragEndColumnIndex = -1;
            if (releaseCapture &&
                _mappingSampleGrid != null &&
                !_mappingSampleGrid.IsDisposed &&
                _mappingSampleGrid.Capture)
                _mappingSampleGrid.Capture = false;
        }

        private void MappingSampleGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_mappingPickTargetRowIndex < 0 || e.RowIndex < 0) return;
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            if (sheet == null) return;

            if (e.ColumnIndex < 0) return;
            string coordinate = ToMappingColumnLetters(e.ColumnIndex) +
                (e.RowIndex + 1).ToString(CultureInfo.InvariantCulture);
            MappingCellSnapshot selectedCell = sheet.Cells.FirstOrDefault(
                cell => string.Equals(cell.Coordinate, coordinate, StringComparison.Ordinal));
            if (selectedCell == null) return;

            if (_mappingPickAnchorCell == null)
            {
                _mappingPickAnchorCell = selectedCell;
                _mappingPickAnchorSampleRowIndex = e.RowIndex;
                _mappingPickAnchorSampleColumnIndex = e.ColumnIndex;
                _mappingSampleGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor = Color.LightGoldenrodYellow;
                SetMappingStatus(
                    "点选定位 2/2：锚点 " + selectedCell.Coordinate + "，现在点击实际值单元格",
                    Color.RoyalBlue);
                return;
            }

            try
            {
                ApplyMappingPointSelection(_mappingPickAnchorCell, selectedCell);
                string target = CellText(dataGridView1.Rows[_mappingPickTargetRowIndex], MappingTargetFieldColumn);
                CancelMappingPointSelection(false);
                SetMappingStatus(
                    "已为 " + target + " 计算定位参数并标记为人工确认；保存时会自动验证",
                    Color.DarkGreen);
            }
            catch (Exception ex)
            {
                CancelMappingPointSelection(false);
                MessageBox.Show("点选定位失败：" + ex.Message, "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyMappingPointSelection(
            MappingCellSnapshot anchor,
            MappingCellSnapshot valueCell)
        {
            if (_mappingPickTargetRowIndex < 0 || _mappingPickTargetRowIndex >= dataGridView1.Rows.Count)
                throw new MappingValidationException("目标字段已经失效，请重新选择。");

            int anchorRow;
            int anchorColumn;
            int valueRow;
            int valueColumn;
            if (!TryParseMappingCoordinate(anchor.Coordinate, out anchorRow, out anchorColumn) ||
                !TryParseMappingCoordinate(valueCell.Coordinate, out valueRow, out valueColumn))
                throw new MappingValidationException("样本单元格坐标无效。");

            DataGridViewRow row = dataGridView1.Rows[_mappingPickTargetRowIndex];
            string locatorType = CellText(row, MappingLocatorTypeColumn);
            var metadata = row.Tag as MappingRowMetadata ?? new MappingRowMetadata();
            row.Tag = metadata;
            _mappingApplyingSuggestion = true;
            try
            {
                row.Cells[MappingScopeColumn].Value = "公共字段";
                row.Cells[MappingKeyColumn].Value = false;
                row.Cells[MappingRowOffsetColumn].Value = "0";
                row.Cells[MappingColumnOffsetColumn].Value = "0";
                row.Cells[MappingValueColumnColumn].Value = string.Empty;
                row.Cells[MappingDataRowOffsetColumn].Value = "0";

                if (locatorType == "cell")
                {
                    if (string.IsNullOrWhiteSpace(anchor.DisplayText))
                        throw new MappingValidationException("固定单元格映射的结构锚点文本不能为空。");
                    row.Cells[MappingLocatorValueColumn].Value = valueCell.Coordinate;
                    metadata.AnchorCell = anchor.Coordinate;
                    metadata.AnchorText = anchor.DisplayText;
                }
                else
                {
                    metadata.AnchorCell = null;
                    metadata.AnchorText = null;
                    if (string.IsNullOrWhiteSpace(anchor.DisplayText))
                        throw new MappingValidationException("标签/表头锚点不能为空。");
                    row.Cells[MappingLocatorValueColumn].Value = anchor.DisplayText;
                    if (locatorType == "labelOffset")
                    {
                        row.Cells[MappingRowOffsetColumn].Value =
                            (valueRow - anchorRow).ToString(CultureInfo.InvariantCulture);
                        row.Cells[MappingColumnOffsetColumn].Value =
                            (valueColumn - anchorColumn).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (locatorType == "rowKey")
                    {
                        row.Cells[MappingRowOffsetColumn].Value =
                            (valueRow - anchorRow).ToString(CultureInfo.InvariantCulture);
                        row.Cells[MappingValueColumnColumn].Value = ToMappingColumnLetters(valueColumn);
                    }
                    else if (locatorType == "headerColumn")
                    {
                        row.Cells[MappingDataRowOffsetColumn].Value =
                            (valueRow - anchorRow).ToString(CultureInfo.InvariantCulture);
                        row.Cells[MappingColumnOffsetColumn].Value =
                            (valueColumn - anchorColumn).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        throw new MappingValidationException("不支持的定位方式：" + locatorType);
                    }
                }

                metadata.ConfirmationState = MappingConfirmationState.HumanConfirmed;
                row.Cells[MappingHumanConfirmedColumn].Value = true;
                UpdateMappingConfirmationCell(row);
                ClearMappingPreviewRow(row);
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
            MarkMappingDirty();
        }

        private void CancelMappingPointSelection(bool updateStatus)
        {
            if (_mappingPickAnchorSampleRowIndex >= 0 &&
                _mappingPickAnchorSampleRowIndex < _mappingSampleGrid.Rows.Count &&
                _mappingPickAnchorSampleColumnIndex >= 0 &&
                _mappingPickAnchorSampleColumnIndex < _mappingSampleGrid.Columns.Count)
            {
                _mappingSampleGrid.Rows[_mappingPickAnchorSampleRowIndex]
                    .Cells[_mappingPickAnchorSampleColumnIndex].Style.BackColor = Color.Empty;
            }
            bool wasActive = _mappingPickTargetRowIndex >= 0;
            _mappingPickTargetRowIndex = -1;
            _mappingPickAnchorSampleRowIndex = -1;
            _mappingPickAnchorSampleColumnIndex = -1;
            _mappingPickAnchorCell = null;
            if (_mappingPickLocatorButton != null)
                _mappingPickLocatorButton.Text = "点选定位";
            if (updateStatus && wasActive)
                SetMappingStatus("已取消点选定位", Color.DimGray);
        }

        private static bool TryParseMappingCoordinate(
            string coordinate,
            out int rowIndex,
            out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;
            if (string.IsNullOrWhiteSpace(coordinate)) return false;
            int split = 0;
            while (split < coordinate.Length && char.IsLetter(coordinate[split])) split++;
            int oneBasedRow;
            if (split == 0 || split == coordinate.Length ||
                !int.TryParse(
                    coordinate.Substring(split),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out oneBasedRow))
                return false;
            int oneBasedColumn = 0;
            foreach (char character in coordinate.Substring(0, split).ToUpperInvariant())
                oneBasedColumn = oneBasedColumn * 26 + character - 'A' + 1;
            rowIndex = oneBasedRow - 1;
            columnIndex = oneBasedColumn - 1;
            return rowIndex >= 0 && columnIndex >= 0;
        }

        private static string ToMappingColumnLetters(int zeroBasedColumn)
        {
            int value = zeroBasedColumn + 1;
            string letters = string.Empty;
            while (value > 0)
            {
                value--;
                letters = (char)('A' + value % 26) + letters;
                value /= 26;
            }
            return letters;
        }

        private void MappingGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dataGridView1.IsCurrentCellDirty)
                dataGridView1.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void MappingGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            SetMappingStatus("字段映射中有无法识别的单元格值", Color.Firebrick);
        }

        private void MappingGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_mappingSuppressEvents || _mappingApplyingSuggestion || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (!_mappingDirty && MappingEditorMatchesCleanState())
                return;

            DataGridViewRow row = dataGridView1.Rows[e.RowIndex];
            var metadata = row.Tag as MappingRowMetadata ?? new MappingRowMetadata();
            row.Tag = metadata;
            string columnName = dataGridView1.Columns[e.ColumnIndex].Name;
            _mappingApplyingSuggestion = true;
            try
            {
                if (columnName == MappingScopeColumn)
                {
                    if (GetMappingFieldScope(row) == MappingFieldScope.RowColumn)
                    {
                        row.Cells[MappingLocatorTypeColumn].Value = "rowColumn";
                    }
                    else if (string.Equals(CellText(row, MappingLocatorTypeColumn), "rowColumn", StringComparison.Ordinal))
                    {
                        row.Cells[MappingLocatorTypeColumn].Value = "labelOffset";
                        row.Cells[MappingLocatorValueColumn].Value = string.Empty;
                        row.Cells[MappingKeyColumn].Value = false;
                    }
                    metadata.ConfirmationState = MappingConfirmationState.HumanConfirmed;
                    row.Cells[MappingHumanConfirmedColumn].Value = true;
                }
                else if (columnName == MappingKeyColumn)
                {
                    bool selected = Convert.ToBoolean(row.Cells[MappingKeyColumn].Value ?? false);
                    if (selected)
                    {
                        if (GetMappingFieldScope(row) != MappingFieldScope.RowColumn)
                        {
                            row.Cells[MappingKeyColumn].Value = false;
                            SetMappingStatus("只有明细列可以设为关键列。", Color.Firebrick);
                        }
                        else
                        {
                            foreach (DataGridViewRow other in dataGridView1.Rows)
                            {
                                if (!ReferenceEquals(other, row)) other.Cells[MappingKeyColumn].Value = false;
                            }
                            if (_mappingRepeatedRows != null)
                                _mappingRepeatedRows.KeyColumnOffset = ParseMappingInteger(
                                    row, MappingColumnOffsetColumn, "列偏移");
                        }
                    }
                }
                else if (columnName == MappingHumanConfirmedColumn)
                {
                    bool confirmed = Convert.ToBoolean(row.Cells[MappingHumanConfirmedColumn].Value ?? false);
                    metadata.ConfirmationState = confirmed
                        ? MappingConfirmationState.HumanConfirmed
                        : MappingConfirmationState.Unconfirmed;
                }
                else if (IsMappingEditableColumn(columnName))
                {
                    metadata.ConfirmationState = MappingConfirmationState.HumanConfirmed;
                    row.Cells[MappingHumanConfirmedColumn].Value = true;
                }
                UpdateMappingConfirmationCell(row);
                ClearMappingPreviewRow(row);
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
            MarkMappingDirty();
        }

        private static bool IsMappingEditableColumn(string columnName)
        {
            return columnName == MappingLocatorTypeColumn ||
                   columnName == MappingScopeColumn ||
                   columnName == MappingKeyColumn ||
                   columnName == MappingLocatorValueColumn ||
                   columnName == MappingRowOffsetColumn ||
                   columnName == MappingColumnOffsetColumn ||
                   columnName == MappingValueColumnColumn ||
                   columnName == MappingDataRowOffsetColumn ||
                   columnName == MappingTransformsColumn ||
                   columnName == MappingValueMapColumn ||
                   columnName == MappingDefaultValueColumn;
        }

        private void MarkMappingDirty()
        {
            if (MappingEditorMatchesCleanState())
            {
                _mappingDirty = false;
                _mappingTemplateSignature = _mappingCleanTemplateSignature;
                UpdateMappingVersionStatus();
                UpdateMappingCommandState();
                return;
            }
            _mappingDirty = true;
            _mappingTemplateSignature = null;
            UpdateMappingVersionStatus();
            UpdateMappingCommandState();
        }

        private void AcceptMappingEditorStateAsClean()
        {
            _mappingDirty = false;
            _mappingCleanTemplateSignature = _mappingTemplateSignature;
            _mappingCleanEditorStateHash = CaptureMappingEditorStateHash();
        }

        private void RefreshMappingDirtyStateFromEditor()
        {
            if (!MappingEditorMatchesCleanState()) return;
            _mappingDirty = false;
            _mappingTemplateSignature = _mappingCleanTemplateSignature;
        }

        private bool MappingEditorMatchesCleanState()
        {
            return !string.IsNullOrEmpty(_mappingCleanEditorStateHash) &&
                   string.Equals(
                       _mappingCleanEditorStateHash,
                       CaptureMappingEditorStateHash(),
                       StringComparison.Ordinal);
        }

        private string CaptureMappingEditorStateHash()
        {
            var state = new StringBuilder();
            MappingModelChoice model = GetSelectedMappingModel();
            AppendMappingStatePart(state, model == null ? 0 : model.Id);
            MappingModelChoice detailModel = GetSelectedMappingDetailModel();
            AppendMappingStatePart(state, detailModel == null ? 0 : detailModel.Id);
            AppendMappingStatePart(state, _mappingParentCidTextBox == null ? null : _mappingParentCidTextBox.Text);
            AppendMappingStatePart(state, _mappingImageRootTextBox == null ? null : _mappingImageRootTextBox.Text);
            AppendMappingStatePart(state, _mappingImagePathFieldCombo == null ? null : _mappingImagePathFieldCombo.SelectedItem);
            AppendMappingStatePart(state, _mappingCsvEncodingCombo == null ? null : _mappingCsvEncodingCombo.SelectedItem);
            AppendMappingStatePart(state, _mappingCsvDelimiterCombo == null ? null : _mappingCsvDelimiterCombo.SelectedItem);
            AppendMappingStatePart(state, _mappingCsvHeaderRowNumber == null ? null : (object)_mappingCsvHeaderRowNumber.Value);
            AppendMappingStatePart(state, _mappingCsvFirstDataRowNumber == null ? null : (object)_mappingCsvFirstDataRowNumber.Value);
            AppendMappingStatePart(state, _mappingRuleNameTextBox == null ? null : _mappingRuleNameTextBox.Text);
            AppendMappingStatePart(state, _mappingSheetCombo == null ? null : _mappingSheetCombo.SelectedItem);
            AppendMappingStatePart(state, (int)GetSelectedMappingMode());

            RepeatedRowDefinition repeated = _mappingRepeatedRows;
            AppendMappingStatePart(state, repeated == null ? null : (object)(int)repeated.AnchorMode);
            AppendMappingStatePart(state, repeated == null ? null : repeated.AnchorText);
            AppendMappingStatePart(state, repeated == null ? null : repeated.AnchorCell);
            AppendMappingStatePart(state, repeated == null ? null : (object)repeated.FirstDataRowOffset);
            AppendMappingStatePart(state, repeated == null ? null : (object)repeated.KeyColumnOffset);
            AppendMappingStatePart(state, repeated == null ? null : (object)repeated.FirstColumnOffset);
            AppendMappingStatePart(state, repeated == null ? null : (object)repeated.LastColumnOffset);
            AppendMappingStatePart(state, repeated != null && repeated.StopOnBlankKey);

            string[] columns =
            {
                MappingTargetFieldColumn,
                MappingTargetTypeColumn,
                MappingRequiredColumn,
                MappingDescriptionColumn,
                MappingRoleColumn,
                MappingScopeColumn,
                MappingKeyColumn,
                MappingLocatorTypeColumn,
                MappingLocatorValueColumn,
                MappingRowOffsetColumn,
                MappingColumnOffsetColumn,
                MappingValueColumnColumn,
                MappingDataRowOffsetColumn,
                MappingTransformsColumn,
                MappingValueMapColumn,
                MappingDefaultValueColumn,
                MappingHumanConfirmedColumn
            };
            if (dataGridView1 != null)
            {
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    AppendMappingStatePart(state, row.IsNewRow);
                    foreach (string column in columns)
                        AppendMappingStatePart(state, row.Cells[column].Value);

                    var metadata = row.Tag as MappingRowMetadata;
                    AppendMappingStatePart(state, metadata == null ? null : (object)(int)metadata.ConfirmationState);
                    AppendMappingStatePart(state, metadata == null ? null : metadata.AnchorCell);
                    AppendMappingStatePart(state, metadata == null ? null : metadata.AnchorText);
                    AppendMappingStatePart(state, metadata != null && metadata.IsOrphan);
                    AppendMappingStatePart(state, metadata != null && metadata.IsMaster);
                }
            }
            return MappingRuleSerializer.Sha256(state.ToString());
        }

        private static void AppendMappingStatePart(StringBuilder state, object value)
        {
            string text = value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture);
            state.Append(text.Length.ToString(CultureInfo.InvariantCulture));
            state.Append(':');
            state.Append(text);
            state.Append('|');
        }

        private void MappingBrowseButton_Click(object sender, EventArgs e)
        {
            bool imageMode = GetSelectedMappingMode() == MappingRecordMode.ImageFileName;
            using (var dialog = new OpenFileDialog
            {
                Title = imageMode ? "选择用于文件名映射验证的图片样本" : "选择用于映射验证的表格样本",
                Filter = imageMode
                    ? "图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
                    : "表格文件 (*.xls;*.xlsx;*.csv)|*.xls;*.xlsx;*.csv|Excel 工作簿 (*.xls;*.xlsx)|*.xls;*.xlsx|CSV 文件 (*.csv)|*.csv",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CancelMappingPointSelection(false);
                    string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (_mappingCurrentDefinition != null &&
                        !string.Equals(
                            extension,
                            _mappingCurrentDefinition.NormalizedExtension,
                            StringComparison.Ordinal))
                        throw new MappingValidationException(
                            "样本扩展名与当前映射定义不一致；同一映射定义不能切换文件扩展名。");
                    if (_mappingCurrentDefinition == null &&
                        string.Equals(extension, ".csv", StringComparison.Ordinal))
                    {
                        _mappingSnapshot = null;
                        _mappingSamplePathTextBox.Text = dialog.FileName;
                        _mappingSheetCombo.Items.Clear();
                        UpdateSourceMappingControls();
                    }
                    MappingWorkbookSnapshot snapshot = imageMode
                        ? new ImageFileMappingService().Inspect(dialog.FileName)
                        : (string.Equals(extension, ".csv", StringComparison.Ordinal)
                            ? _mappingCsvPreviewService.Inspect(dialog.FileName, GetCsvOptionsFromEditor())
                            : _mappingPreviewService.Inspect(dialog.FileName));
                    string preferredSheet = imageMode || _mappingCurrentDefinition == null
                        ? null
                        : _mappingCurrentDefinition.SheetName;
                    _mappingSnapshot = snapshot;
                    _mappingSamplePathTextBox.Text = dialog.FileName;
                    UpdateSourceMappingControls();
                    if (_mappingCurrentDefinition == null)
                    {
                        MappingModelChoice selectedModel = GetSelectedMappingModel();
                        if (selectedModel != null)
                            _mappingRuleNameTextBox.Text = selectedModel.ModelName +
                                (string.Equals(snapshot.FileExtension, ".csv", StringComparison.Ordinal)
                                    ? " CSV 映射"
                                    : " Excel 映射");
                    }
                    FileNameExtractionResult fileName = FileNameExtractionParser.Inspect(dialog.FileName);
                    _mappingFileNameInfoLabel.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "文件名：{0} / 无扩展名：{1} / 片段：{2}",
                        fileName.FullName,
                        fileName.Stem,
                        fileName.Segments.Count == 0
                            ? "（无）"
                            : string.Join("、", fileName.Segments.Select((value, index) =>
                                (index + 1).ToString(CultureInfo.InvariantCulture) + "=" + value)));
                    _mappingExtensionLabel.Text = "扩展名：" + snapshot.FileExtension;
                    LoadMappingSheetChoices(preferredSheet);
                    if (_mappingCurrentDefinition != null &&
                        !string.Equals(
                            Convert.ToString(_mappingSheetCombo.SelectedItem, CultureInfo.InvariantCulture),
                            _mappingCurrentDefinition.SheetName,
                            StringComparison.Ordinal))
                    {
                        _mappingDirty = true;
                        _mappingTemplateSignature = null;
                    }
                    PopulateMappingSampleGrid();
                    ClearMappingPreviewColumns();
                    SetMappingStatus(
                        imageMode
                            ? "图片样本已只读验证，SHA-256 " + ShortHash(snapshot.FileSha256)
                            : string.Format(
                                CultureInfo.InvariantCulture,
                                "样本已只读加载：{0} 个工作表，SHA-256 {1}",
                                snapshot.Sheets.Count,
                                ShortHash(snapshot.FileSha256)),
                        Color.DarkGreen);
                    UpdateMappingCommandState();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "样本加载失败，当前草稿未修改。\r\n\r\n" + ex.Message,
                        "样本无效",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadMappingSheetChoices(string preferredSheet)
        {
            bool previouslySuppressingEvents = _mappingSuppressEvents;
            _mappingSuppressEvents = true;
            try
            {
                _mappingSheetCombo.Items.Clear();
                if (_mappingSnapshot != null)
                {
                    foreach (MappingSheetSnapshot sheet in _mappingSnapshot.Sheets)
                        _mappingSheetCombo.Items.Add(sheet.Name);
                }
                else if (!string.IsNullOrWhiteSpace(preferredSheet))
                {
                    _mappingSheetCombo.Items.Add(preferredSheet);
                }

                int selectedIndex = -1;
                if (!string.IsNullOrWhiteSpace(preferredSheet))
                    selectedIndex = _mappingSheetCombo.FindStringExact(preferredSheet);
                if (selectedIndex < 0 && _mappingSheetCombo.Items.Count > 0)
                    selectedIndex = 0;
                _mappingSheetCombo.SelectedIndex = selectedIndex;
            }
            finally
            {
                _mappingSuppressEvents = previouslySuppressingEvents;
            }
            PopulateMappingSampleGrid();
        }

        private void PopulateMappingSampleGrid()
        {
            _mappingSampleGrid.Rows.Clear();
            _mappingSampleGrid.Columns.Clear();
            _mappingRecordsGrid.Rows.Clear();
            _mappingRecordsGrid.Columns.Clear();
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            if (sheet == null || sheet.Cells.Count == 0) return;

            _mappingSampleGrid.SuspendLayout();
            try
            {
                int maxRow = 0;
                int maxColumn = 0;
                foreach (MappingCellSnapshot cell in sheet.Cells)
                {
                    int rowIndex;
                    int columnIndex;
                    if (!TryParseMappingCoordinate(cell.Coordinate, out rowIndex, out columnIndex)) continue;
                    maxRow = Math.Max(maxRow, rowIndex);
                    maxColumn = Math.Max(maxColumn, columnIndex);
                }
                for (int columnIndex = 0; columnIndex <= maxColumn; columnIndex++)
                {
                    _mappingSampleGrid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = "ExcelColumn" + columnIndex.ToString(CultureInfo.InvariantCulture),
                        HeaderText = ToMappingColumnLetters(columnIndex),
                        Width = 105,
                        SortMode = DataGridViewColumnSortMode.NotSortable
                    });
                }
                _mappingSampleGrid.Rows.Add(maxRow + 1);
                for (int rowIndex = 0; rowIndex <= maxRow; rowIndex++)
                    _mappingSampleGrid.Rows[rowIndex].HeaderCell.Value =
                        (rowIndex + 1).ToString(CultureInfo.InvariantCulture);

                foreach (MappingCellSnapshot cell in sheet.Cells)
                {
                    int rowIndex;
                    int columnIndex;
                    if (!TryParseMappingCoordinate(cell.Coordinate, out rowIndex, out columnIndex)) continue;
                    DataGridViewCell gridCell = _mappingSampleGrid.Rows[rowIndex].Cells[columnIndex];
                    gridCell.Value = cell.DisplayText;
                    gridCell.Tag = cell;
                    gridCell.ToolTipText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} / {1}{2}",
                        cell.Coordinate,
                        cell.ValueType,
                        cell.IsFormula ? " / 公式" : string.Empty);
                    if (cell.FormulaCacheMissing)
                    {
                        gridCell.Style.ForeColor = Color.Firebrick;
                        gridCell.ToolTipText += " / 公式缓存值缺失";
                    }
                }
                ApplyMappingMergedRegionDisplay(sheet);
            }
            finally
            {
                _mappingSampleGrid.ResumeLayout();
            }
        }

        private void ApplyMappingMergedRegionDisplay(MappingSheetSnapshot sheet)
        {
            foreach (string mergedRegion in sheet.MergedRegions ?? new List<string>())
            {
                string[] bounds = (mergedRegion ?? string.Empty).Split(':');
                int firstRow;
                int firstColumn;
                int lastRow;
                int lastColumn;
                if (bounds.Length != 2 ||
                    !TryParseMappingCoordinate(bounds[0], out firstRow, out firstColumn) ||
                    !TryParseMappingCoordinate(bounds[1], out lastRow, out lastColumn))
                    continue;

                firstRow = Math.Max(0, firstRow);
                firstColumn = Math.Max(0, firstColumn);
                lastRow = Math.Min(lastRow, _mappingSampleGrid.Rows.Count - 1);
                lastColumn = Math.Min(lastColumn, _mappingSampleGrid.Columns.Count - 1);
                for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
                {
                    for (int columnIndex = firstColumn; columnIndex <= lastColumn; columnIndex++)
                    {
                        DataGridViewCell cell = _mappingSampleGrid.Rows[rowIndex].Cells[columnIndex];
                        cell.Style.BackColor = Color.AliceBlue;
                        string note = "合并区域 " + mergedRegion;
                        cell.ToolTipText = string.IsNullOrWhiteSpace(cell.ToolTipText)
                            ? note
                            : cell.ToolTipText + " / " + note;
                    }
                }
            }
        }

        private void MappingFrameTableButton_Click(object sender, EventArgs e)
        {
            if (_mappingSnapshot == null || GetSelectedMappingSheet() == null)
            {
                MessageBox.Show("请先选择表格样本和工作表。", "框选表格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_mappingSampleGrid.SelectedCells.Count == 0)
            {
                MessageBox.Show("请在样本网格中框选一行表头和紧邻的一条样例数据。",
                    "框选表格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<int> rows = _mappingSampleGrid.SelectedCells.Cast<DataGridViewCell>()
                .Select(cell => cell.RowIndex).Distinct().OrderBy(value => value).ToList();
            List<int> columns = _mappingSampleGrid.SelectedCells.Cast<DataGridViewCell>()
                .Select(cell => cell.ColumnIndex).Distinct().OrderBy(value => value).ToList();
            if (rows.Count != 2 || rows[1] != rows[0] + 1 || columns.Count == 0 ||
                _mappingSampleGrid.SelectedCells.Count != rows.Count * columns.Count)
            {
                MessageBox.Show("框选区域必须是连续两行：第一行为表头，第二行为第一条数据。",
                    "框选表格", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ConfigureRepeatedRowsFromSelection(rows[0], rows[1], columns);
            }
            catch (Exception ex)
            {
                ShowMappingError("配置重复行表格失败", ex);
            }
        }

        private void ConfigureRepeatedRowsFromSelection(int headerRow, int dataRow, IList<int> columns)
        {
            List<DataGridViewRow> targetRows = dataGridView1.Rows.Cast<DataGridViewRow>()
                .Where(row =>
                {
                    var metadata = row.Tag as MappingRowMetadata;
                    return metadata == null || (!metadata.IsOrphan && !metadata.IsMaster);
                }).ToList();
            var targetNames = targetRows.Select(row => CellText(row, MappingTargetFieldColumn)).ToList();

            using (var dialog = new Form
            {
                Text = "重复行列映射",
                StartPosition = FormStartPosition.CenterParent,
                Width = 900,
                Height = 520,
                MinimizeBox = false,
                MaximizeBox = true,
                ShowInTaskbar = false
            })
            {
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    EditMode = DataGridViewEditMode.EditOnEnter
                };
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceColumn", HeaderText = "源列", ReadOnly = true, FillWeight = 45F });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Header", HeaderText = "表头", ReadOnly = true, FillWeight = 110F });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Example", HeaderText = "样例值", ReadOnly = true, FillWeight = 130F });
                var targetColumn = new DataGridViewComboBoxColumn
                {
                    Name = "TargetField",
                    HeaderText = "映射到目标字段",
                    FlatStyle = FlatStyle.Flat,
                    FillWeight = 135F
                };
                targetColumn.Items.Add("（忽略）");
                foreach (string targetName in targetNames) targetColumn.Items.Add(targetName);
                grid.Columns.Add(targetColumn);
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Transforms",
                    HeaderText = "转换（可选）",
                    FillWeight = 105F
                });
                grid.Columns.Add(new DataGridViewCheckBoxColumn
                {
                    Name = "IsKey",
                    HeaderText = "关键列",
                    FillWeight = 55F
                });

                for (int sourcePosition = 0; sourcePosition < columns.Count; sourcePosition++)
                {
                    int columnIndex = columns[sourcePosition];
                    string header = Convert.ToString(
                        _mappingSampleGrid.Rows[headerRow].Cells[columnIndex].Value,
                        CultureInfo.InvariantCulture) ?? string.Empty;
                    string example = Convert.ToString(
                        _mappingSampleGrid.Rows[dataRow].Cells[columnIndex].Value,
                        CultureInfo.InvariantCulture) ?? string.Empty;
                    int index = grid.Rows.Add(
                        ToMappingColumnLetters(columnIndex),
                        header,
                        example,
                        GetRepeatedRowDefaultTarget(targetNames, sourcePosition),
                        string.Empty,
                        false);
                    grid.Rows[index].Tag = columnIndex;
                }

                var footer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 58,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8)
                };
                var ok = new Button { Text = "应用映射", Width = 110, Height = 34 };
                var cancel = new Button { Text = "取消", Width = 90, Height = 34 };
                footer.Controls.Add(ok);
                footer.Controls.Add(cancel);
                footer.Controls.Add(new Label
                {
                    AutoSize = true,
                    Margin = new Padding(0, 9, 16, 0),
                    Text = "已按数据模型字段填写顺序预匹配；不正确可下拉修改；必须且只能选择一个关键列。"
                });
                dialog.Controls.Add(grid);
                dialog.Controls.Add(footer);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                cancel.Click += delegate { dialog.DialogResult = DialogResult.Cancel; };
                ok.Click += delegate
                {
                    try
                    {
                        List<DataGridViewRow> mapped = grid.Rows.Cast<DataGridViewRow>()
                            .Where(row => !string.Equals(
                                Convert.ToString(row.Cells["TargetField"].Value, CultureInfo.InvariantCulture),
                                "（忽略）",
                                StringComparison.Ordinal)).ToList();
                        if (mapped.Count == 0)
                            throw new MappingValidationException("请至少映射一个明细列。");
                        List<string> mappedTargets = mapped.Select(row => Convert.ToString(
                            row.Cells["TargetField"].Value, CultureInfo.InvariantCulture)).ToList();
                        if (mappedTargets.Distinct(StringComparer.Ordinal).Count() != mappedTargets.Count)
                            throw new MappingValidationException("同一目标字段不能映射到多个源列。");
                        List<DataGridViewRow> keys = mapped.Where(row => Convert.ToBoolean(
                            row.Cells["IsKey"].Value ?? false)).ToList();
                        if (keys.Count != 1)
                            throw new MappingValidationException("必须且只能选择一个已映射列作为关键列。");

                        ApplyRepeatedRowColumnMappings(headerRow, dataRow, columns, mapped, keys[0]);
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, ex.Message, "列映射无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                dialog.ShowDialog(this);
            }
        }

        private static string GetRepeatedRowDefaultTarget(IList<string> targetNames, int sourcePosition)
        {
            if (targetNames == null || sourcePosition < 0 || sourcePosition >= targetNames.Count ||
                string.IsNullOrWhiteSpace(targetNames[sourcePosition]))
                return "（忽略）";
            return targetNames[sourcePosition];
        }

        private void ApplyRepeatedRowColumnMappings(
            int headerRow,
            int dataRow,
            IList<int> selectedColumns,
            IList<DataGridViewRow> mappedRows,
            DataGridViewRow keyRow)
        {
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            int anchorColumn = selectedColumns.First();
            MappingTableAnchorMode anchorMode = MappingTableAnchorMode.FixedCell;
            string anchorText = string.Empty;
            foreach (int candidate in selectedColumns)
            {
                string header = Convert.ToString(
                    _mappingSampleGrid.Rows[headerRow].Cells[candidate].Value,
                    CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(header)) continue;
                int matches = sheet.Cells.Count(cell => string.Equals(
                    NormalizeMappingLabel(cell.DisplayText),
                    NormalizeMappingLabel(header),
                    StringComparison.OrdinalIgnoreCase));
                if (matches == 1)
                {
                    anchorColumn = candidate;
                    anchorMode = MappingTableAnchorMode.HeaderText;
                    anchorText = header;
                    break;
                }
            }

            int keyColumn = Convert.ToInt32(keyRow.Tag, CultureInfo.InvariantCulture);
            _mappingRepeatedRows = new RepeatedRowDefinition
            {
                AnchorMode = anchorMode,
                AnchorText = anchorText,
                AnchorCell = ToMappingColumnLetters(anchorColumn) +
                    (headerRow + 1).ToString(CultureInfo.InvariantCulture),
                FirstDataRowOffset = dataRow - headerRow,
                KeyColumnOffset = keyColumn - anchorColumn,
                FirstColumnOffset = selectedColumns.First() - anchorColumn,
                LastColumnOffset = selectedColumns.Last() - anchorColumn,
                StopOnBlankKey = true
            };

            _mappingApplyingSuggestion = true;
            try
            {
                if (GetSelectedMappingMode() != MappingRecordMode.MasterDetail)
                    SelectMappingMode(MappingRecordMode.RepeatingRows);
                foreach (DataGridViewRow targetRow in dataGridView1.Rows)
                {
                    if (GetMappingFieldScope(targetRow) != MappingFieldScope.RowColumn) continue;
                    targetRow.Cells[MappingScopeColumn].Value = "公共字段";
                    targetRow.Cells[MappingKeyColumn].Value = false;
                    targetRow.Cells[MappingLocatorTypeColumn].Value = "labelOffset";
                    targetRow.Cells[MappingLocatorValueColumn].Value = string.Empty;
                    targetRow.Cells[MappingHumanConfirmedColumn].Value = false;
                    var metadata = targetRow.Tag as MappingRowMetadata;
                    if (metadata != null) metadata.ConfirmationState = MappingConfirmationState.Unconfirmed;
                    UpdateMappingConfirmationCell(targetRow);
                }

                var targetByName = dataGridView1.Rows.Cast<DataGridViewRow>()
                    .Where(row =>
                    {
                        var metadata = row.Tag as MappingRowMetadata;
                        return metadata == null || !metadata.IsMaster;
                    })
                    .ToDictionary(
                    row => CellText(row, MappingTargetFieldColumn),
                    row => row,
                    StringComparer.Ordinal);
                foreach (DataGridViewRow sourceRow in mappedRows)
                {
                    string targetName = Convert.ToString(sourceRow.Cells["TargetField"].Value, CultureInfo.InvariantCulture);
                    DataGridViewRow targetRow = targetByName[targetName];
                    int sourceColumn = Convert.ToInt32(sourceRow.Tag, CultureInfo.InvariantCulture);
                    targetRow.Cells[MappingScopeColumn].Value = "明细列";
                    targetRow.Cells[MappingKeyColumn].Value = ReferenceEquals(sourceRow, keyRow);
                    targetRow.Cells[MappingLocatorTypeColumn].Value = "rowColumn";
                    targetRow.Cells[MappingLocatorValueColumn].Value = Convert.ToString(
                        sourceRow.Cells["Header"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                    targetRow.Cells[MappingRowOffsetColumn].Value = "0";
                    targetRow.Cells[MappingColumnOffsetColumn].Value =
                        (sourceColumn - anchorColumn).ToString(CultureInfo.InvariantCulture);
                    targetRow.Cells[MappingValueColumnColumn].Value = string.Empty;
                    targetRow.Cells[MappingDataRowOffsetColumn].Value = "0";
                    targetRow.Cells[MappingTransformsColumn].Value = Convert.ToString(
                        sourceRow.Cells["Transforms"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                    targetRow.Cells[MappingHumanConfirmedColumn].Value = true;
                    var metadata = targetRow.Tag as MappingRowMetadata ?? new MappingRowMetadata();
                    targetRow.Tag = metadata;
                    metadata.AnchorCell = null;
                    metadata.AnchorText = null;
                    metadata.ConfirmationState = MappingConfirmationState.HumanConfirmed;
                    UpdateMappingConfirmationCell(targetRow);
                    ClearMappingPreviewRow(targetRow);
                }
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
            MarkMappingDirty();
            SetMappingStatus(
                string.Format(CultureInfo.InvariantCulture,
                    anchorMode == MappingTableAnchorMode.HeaderText
                        ? "已配置重复行表格：{0} 个明细列，关键列 {1}，使用唯一表头锚点。"
                        : "已配置重复行表格：{0} 个明细列，关键列 {1}，未找到唯一表头，已改用固定单元格锚点（稳定性较低）。",
                    mappedRows.Count,
                    ToMappingColumnLetters(keyColumn)),
                anchorMode == MappingTableAnchorMode.HeaderText ? Color.DarkGreen : Color.DarkOrange);
        }

        private static string NormalizeMappingLabel(string value)
        {
            return string.Concat((value ?? string.Empty)
                .Normalize(NormalizationForm.FormKC)
                .Where(character => !char.IsWhiteSpace(character)));
        }

        private MappingSheetSnapshot GetSelectedMappingSheet()
        {
            if (_mappingSnapshot == null || _mappingSheetCombo.SelectedItem == null) return null;
            string name = _mappingSheetCombo.SelectedItem.ToString();
            return _mappingSnapshot.Sheets.FirstOrDefault(
                sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
        }

        private MappingWorkbookSnapshot CreateSelectedSheetSnapshot()
        {
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            if (sheet == null)
                throw new MappingValidationException("请先选择有效的样本工作表。");
            return new MappingWorkbookSnapshot
            {
                FileExtension = _mappingSnapshot.FileExtension,
                FileSha256 = _mappingSnapshot.FileSha256,
                Sheets = new List<MappingSheetSnapshot> { sheet }
            };
        }

        private void MappingLocalAssistButton_Click(object sender, EventArgs e)
        {
            try
            {
                MappingWorkbookSnapshot snapshot = CreateSelectedSheetSnapshot();
                List<DataGridViewRow> eligibleRows = GetMappingSuggestionEligibleRows();
                if (eligibleRows.Count == 0)
                {
                    MessageBox.Show("没有可自动填充的空白且未确认字段。", "本地辅助", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                List<AiTargetField> targets = eligibleRows.Select(CreateAiTargetFromRow).ToList();
                IReadOnlyList<AiMappingSuggestion> suggestions = _mappingLocalAssistant.Suggest(
                    snapshot,
                    targets,
                    IsCurrentCsvSource() ? GetCsvOptionsFromEditor() : null);
                int applied = ApplyMappingSuggestions(suggestions, MappingConfirmationState.LocalCandidate);
                SetMappingStatus(
                    string.Format(CultureInfo.InvariantCulture, "本地辅助已填充 {0} 项；请人工检查后再验证", applied),
                    applied > 0 ? Color.DarkOrange : Color.DimGray);
                if (applied == 0)
                {
                    MessageBox.Show(
                        "未找到唯一的字段名/说明精确标签。草稿未发生变化。",
                        "本地辅助",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("本地辅助失败，草稿未修改。\r\n\r\n" + ex.Message, "本地辅助", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void MappingAiAssistButton_Click(object sender, EventArgs e)
        {
            if (_mappingAiOptions == null)
            {
                MessageBox.Show(_mappingAiUnavailableReason ?? "AI 未配置。", "AI 自动映射", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<DataGridViewRow> eligibleRows = GetMappingSuggestionEligibleRows();
            if (eligibleRows.Count == 0)
            {
                MessageBox.Show("没有可自动填充的空白且未确认字段。", "AI 自动映射", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AiMappingRequest request;
            int requestModelId;
            List<string> allowedTargets = eligibleRows
                .Select(row => Convert.ToString(row.Cells[MappingTargetFieldColumn].Value, CultureInfo.InvariantCulture))
                .ToList();
            try
            {
                MappingWorkbookSnapshot snapshot = CreateSelectedSheetSnapshot();
                MappingRuleDefinition requestDefinition = CreateAssistanceDefinition(eligibleRows);
                request = new AiMappingRequestFactory().Create(snapshot, requestDefinition);
                requestModelId = requestDefinition.ModelId;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法准备 AI 请求，草稿未修改。\r\n\r\n" + ex.Message, "AI 自动映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _mappingAiBusy = true;
            _mappingAiCancellation = new CancellationTokenSource();
            SetMappingStatus("AI 正在生成声明式映射建议...", Color.RoyalBlue);
            UpdateMappingCommandState();
            try
            {
                AiMappingClientResult clientResult;
                using (var client = new OpenAiCompatibleMappingClient(_mappingAiOptions))
                {
                    clientResult = await client.SuggestMappingsAsync(
                        request,
                        _mappingAiCancellation.Token);
                }

                if (IsDisposed || Disposing) return;
                if (clientResult == null || !clientResult.IsSuccess)
                {
                    string code = clientResult == null ? "INVALID_AI_RESPONSE" : clientResult.ErrorCode;
                    SetMappingStatus("AI 请求失败：" + code + "；草稿未修改", Color.Firebrick);
                    MessageBox.Show("AI 请求失败，草稿未修改。\r\n\r\n" + code, "AI 自动映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MappingModelChoice currentModel = GetSelectedMappingModel();
                string currentModelHash = currentModel == null
                    ? null
                    : ModelSchemaService.ComputeHash(LoadMappingFields(currentModel.Id));
                if (currentModel == null ||
                    currentModel.Id != requestModelId ||
                    !string.Equals(currentModelHash, request.ModelSchemaHash, StringComparison.Ordinal))
                {
                    SetMappingStatus("AI 调用期间模型结构已变化；草稿未修改", Color.Firebrick);
                    MessageBox.Show(
                        "AI 调用期间目标模型或结构哈希发生变化，旧建议已丢弃，草稿未修改。",
                        "AI 自动映射",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var validation = new AiMappingResponseValidator().Validate(
                    clientResult.Response,
                    allowedTargets,
                    request.ModelSchemaHash);
                if (!validation.IsValid)
                {
                    string errors = string.Join(", ", validation.ErrorCodes);
                    SetMappingStatus("AI 响应被安全校验拒绝；草稿未修改", Color.Firebrick);
                    MessageBox.Show(
                        "AI 响应未通过结构与白名单校验，草稿未修改。\r\n\r\n" + errors,
                        "AI 自动映射",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                int applied = ApplyMappingSuggestions(
                    validation.AcceptedSuggestions,
                    MappingConfirmationState.AiPendingConfirmation);
                SetMappingStatus(
                    string.Format(CultureInfo.InvariantCulture, "AI 已填充 {0} 项；尚未保存，请人工确认", applied),
                    Color.DarkOrange);
                if (applied == 0)
                {
                    MessageBox.Show(
                        "AI 未返回可应用的空白字段建议，草稿未发生变化。",
                        "AI 自动映射",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing)
                    SetMappingStatus("AI 请求已取消；草稿未修改", Color.DimGray);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    SetMappingStatus("AI 请求失败；草稿未修改", Color.Firebrick);
                    MessageBox.Show("AI 请求失败，草稿未修改。\r\n\r\n" + ex.Message, "AI 自动映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (_mappingAiCancellation != null)
                {
                    _mappingAiCancellation.Dispose();
                    _mappingAiCancellation = null;
                }
                _mappingAiBusy = false;
                if (!IsDisposed && !Disposing)
                    UpdateMappingCommandState();
            }
        }

        private MappingRuleDefinition CreateAssistanceDefinition(IEnumerable<DataGridViewRow> rows)
        {
            MappingModelChoice model = GetSelectedMappingModel();
            if (model == null) throw new MappingValidationException("请选择模型。");
            var definition = new MappingRuleDefinition
            {
                DefinitionId = 0,
                RuleName = "AI 映射请求",
                ModelId = model.Id,
                TargetModelType = model.ModelName,
                ModelSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(model.Id)),
                NormalizedExtension = _mappingSnapshot.FileExtension,
                SheetName = Convert.ToString(_mappingSheetCombo.SelectedItem, CultureInfo.InvariantCulture)
            };
            foreach (DataGridViewRow row in rows)
            {
                FieldMappingRule field = CreateFieldRuleMetadata(row);
                field.Locator = new MappingLocator
                {
                    Type = "cell",
                    Cell = "A1",
                    AnchorCell = "A1",
                    AnchorText = "AI request placeholder"
                };
                definition.Fields.Add(field);
            }
            return definition;
        }

        private List<DataGridViewRow> GetMappingSuggestionEligibleRows()
        {
            return dataGridView1.Rows
                .Cast<DataGridViewRow>()
                .Where(row =>
                {
                    var metadata = row.Tag as MappingRowMetadata;
                    return metadata != null &&
                           !metadata.IsOrphan &&
                           GetMappingFieldScope(row) == MappingFieldScope.Common &&
                           metadata.ConfirmationState == MappingConfirmationState.Unconfirmed &&
                           !HasMappingLocator(row);
                })
                .ToList();
        }

        private static AiTargetField CreateAiTargetFromRow(DataGridViewRow row)
        {
            return new AiTargetField
            {
                FieldName = Convert.ToString(row.Cells[MappingTargetFieldColumn].Value, CultureInfo.InvariantCulture),
                FieldType = Convert.ToString(row.Cells[MappingTargetTypeColumn].Value, CultureInfo.InvariantCulture),
                Description = Convert.ToString(row.Cells[MappingDescriptionColumn].Value, CultureInfo.InvariantCulture),
                IsRequired = Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false)
            };
        }

        private int ApplyMappingSuggestions(
            IEnumerable<AiMappingSuggestion> suggestions,
            MappingConfirmationState state)
        {
            var rowsByTarget = dataGridView1.Rows
                .Cast<DataGridViewRow>()
                .ToDictionary(
                    row => Convert.ToString(row.Cells[MappingTargetFieldColumn].Value, CultureInfo.InvariantCulture),
                    StringComparer.Ordinal);
            int applied = 0;
            _mappingApplyingSuggestion = true;
            try
            {
                foreach (AiMappingSuggestion suggestion in suggestions ?? Enumerable.Empty<AiMappingSuggestion>())
                {
                    DataGridViewRow row;
                    if (suggestion == null || suggestion.Locator == null ||
                        !rowsByTarget.TryGetValue(suggestion.TargetField ?? string.Empty, out row))
                        continue;
                    var metadata = row.Tag as MappingRowMetadata;
                    if (metadata == null || metadata.IsOrphan ||
                        metadata.ConfirmationState == MappingConfirmationState.HumanConfirmed ||
                        HasMappingLocator(row))
                        continue;

                    MappingRuleSerializer.ValidateLocator(suggestion.Locator);
                    row.Cells[MappingScopeColumn].Value = "公共字段";
                    row.Cells[MappingKeyColumn].Value = false;
                    row.Cells[MappingLocatorTypeColumn].Value = suggestion.Locator.Type;
                    row.Cells[MappingLocatorValueColumn].Value = suggestion.Locator.Type == "cell"
                        ? suggestion.Locator.Cell
                        : suggestion.Locator.Text;
                    row.Cells[MappingRowOffsetColumn].Value = suggestion.Locator.RowOffset.ToString(CultureInfo.InvariantCulture);
                    row.Cells[MappingColumnOffsetColumn].Value = suggestion.Locator.ColumnOffset.ToString(CultureInfo.InvariantCulture);
                    row.Cells[MappingValueColumnColumn].Value = suggestion.Locator.ValueColumn ?? string.Empty;
                    row.Cells[MappingDataRowOffsetColumn].Value = suggestion.Locator.DataRowOffset.ToString(CultureInfo.InvariantCulture);
                    row.Cells[MappingTransformsColumn].Value = string.Join(",", suggestion.Transforms ?? new List<string>());
                    row.Cells[MappingHumanConfirmedColumn].Value = false;
                    metadata.AnchorCell = suggestion.Locator.Type == "cell"
                        ? suggestion.Locator.AnchorCell
                        : null;
                    metadata.AnchorText = suggestion.Locator.Type == "cell"
                        ? suggestion.Locator.AnchorText
                        : null;
                    metadata.ConfirmationState = state;
                    UpdateMappingConfirmationCell(row);
                    ClearMappingPreviewRow(row);
                    applied++;
                }
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
            if (applied > 0) MarkMappingDirty();
            return applied;
        }

        private void MappingSaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<MappingMachineChoice> selectedMachines = GetSelectedMappingMachines();
                if (selectedMachines.Count == 0)
                    throw new MappingValidationException("请至少勾选一台适用机台后再保存。");

                bool requiresSample = MappingSavePolicy.RequiresSample(
                    _mappingCurrentVersion,
                    _mappingDirty);
                if (GetSelectedMappingMode() == MappingRecordMode.MasterDetail &&
                    !EnsureMasterDetailRelationFieldAndSource())
                    return;
                MappingRuleDefinition definition;
                MappingPreviewResult preview = null;
                if (requiresSample)
                {
                    if (_mappingSnapshot == null || string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text))
                        throw new MappingValidationException(
                            _mappingCurrentVersion == null || _mappingDirty
                                ? "映射内容有修改，保存前必须选择只读样本进行自动验证。"
                                : "当前映射版本尚未验证，必须选择只读样本完成验证后才能保存。");

                    EnsureMappedRowsHumanConfirmed();
                    definition = BuildMappingDefinition(true);
                    preview = definition.RecordMode == MappingRecordMode.ImageFileName
                        ? new ImageFileMappingService().Preview(_mappingSamplePathTextBox.Text, definition)
                        : (string.Equals(definition.NormalizedExtension, ".csv", StringComparison.Ordinal)
                            ? _mappingCsvPreviewService.Preview(_mappingSamplePathTextBox.Text, definition)
                            : _mappingPreviewService.Preview(_mappingSamplePathTextBox.Text, definition));
                    DisplayMappingPreview(preview);
                    if (!preview.IsValid)
                    {
                        throw new MappingValidationException(
                            "样本自动验证未通过：" + string.Join(", ", preview.ErrorCodes));
                    }

                    definition.TemplateSignature = preview.TemplateSignature;
                    _mappingTemplateSignature = preview.TemplateSignature;
                    string derivedScriptCode = new MappingScriptGenerator().Generate(definition);
                    if (definition.RecordMode == MappingRecordMode.MasterDetail)
                    {
                        ScriptEngine.ValidateCompilation(
                            derivedScriptCode,
                            new[]
                            {
                                definition.MasterDetail.Master,
                                definition.MasterDetail.Detail
                            });
                    }
                    else
                        ScriptEngine.ValidateCompilation(derivedScriptCode, definition.ModelId);
                }
                else
                {
                    definition = _mappingCurrentDefinition;
                    if (definition == null)
                        throw new ParseRuleStateException("当前字段映射尚未完成验证，请选择样本后保存。");
                }

                DialogResult publishChoice = MessageBox.Show(
                    this,
                    "样本预览和校验已通过。\r\n\r\n是：保存、验证并发布到所选机台\r\n否：仅保存并验证，不发布\r\n取消：放弃本次操作",
                    "保存/发布确认",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (publishChoice == DialogResult.Cancel) return;
                bool publishNow = publishChoice == DialogResult.Yes;

                var expectedBindings = new Dictionary<int, ParseRuleVersion>();
                var conflicts = new List<string>();
                foreach (MappingMachineChoice machine in selectedMachines)
                {
                    ParseRuleVersion existing = publishNow ? _mappingRuleStore.GetPublished(
                        machine.Id.ToString(CultureInfo.InvariantCulture),
                        definition.NormalizedExtension) : null;
                    expectedBindings[machine.Id] = existing;
                    if (existing != null &&
                        (definition.DefinitionId <= 0 || existing.DefinitionId != definition.DefinitionId))
                    {
                        conflicts.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}：{1} v{2}",
                            machine,
                            existing.RuleName,
                            existing.VersionNumber));
                    }
                }

                if (conflicts.Count > 0)
                {
                    DialogResult confirmation = MessageBox.Show(
                        this,
                        "以下机台在相同扩展名上已有其他字段映射：\r\n\r\n" +
                        string.Join("\r\n", conflicts) +
                        "\r\n\r\n是否确认覆盖这些机台的现有映射？",
                        "保存冲突确认",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (confirmation != DialogResult.Yes) return;
                }

                if (requiresSample &&
                    (!MappingDefinitionMatchesCurrentVersion(definition) ||
                     _mappingCurrentVersion.Status == ParseRuleStatus.Superseded))
                {
                    SaveMappingDraft(definition);
                }

                if (requiresSample && _mappingCurrentVersion.Status == ParseRuleStatus.Draft)
                {
                    _mappingCurrentVersion = _mappingRuleStore.Validate(
                        _mappingCurrentVersion.Id,
                        _mappingCurrentVersion.Revision,
                        BuildMappingValidationSummary(preview));
                    _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(
                        _mappingCurrentVersion.DefinitionJson);
                }

                if (!publishNow)
                {
                    AcceptMappingEditorStateAsClean();
                    RefreshMappingDefinitionList(_mappingCurrentVersion.DefinitionId);
                    UpdateMappingVersionStatus();
                    UpdateMappingCommandState();
                    MessageBox.Show(
                        this,
                        "映射已保存并通过验证，尚未发布到任何机台。",
                        "保存完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Dictionary<string, ParseRuleVersion> previouslyPublished =
                    _mappingRuleStore.GetPublishedMachineIds(_mappingCurrentVersion.DefinitionId)
                        .ToDictionary(
                            machineId => machineId,
                            machineId => _mappingRuleStore.GetPublished(
                                machineId,
                                _mappingCurrentVersion.NormalizedExtension),
                            StringComparer.Ordinal);

                ParseRuleVersion current = _mappingRuleStore.PublishToMachines(
                    _mappingCurrentVersion.Id,
                    selectedMachines
                        .Select(machine => machine.Id.ToString(CultureInfo.InvariantCulture))
                        .ToArray(),
                    _mappingCurrentVersion.Revision,
                    expectedBindings.Values.Any(version => version != null),
                    expectedBindings.ToDictionary(
                        item => item.Key.ToString(CultureInfo.InvariantCulture),
                        item => item.Value == null ? (long?)null : item.Value.Id,
                        StringComparer.Ordinal));

                foreach (long replacedVersionId in previouslyPublished.Values
                    .Concat(expectedBindings.Values)
                    .Where(version => version != null && version.Id != current.Id)
                    .Select(version => version.Id)
                    .Distinct())
                {
                    ScriptEngine.ClearCache(replacedVersionId);
                }
                ScriptEngine.ClearCache(current.Id);

                _mappingCurrentVersion = current;
                _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(current.DefinitionJson);
                AcceptMappingEditorStateAsClean();
                RefreshMappingDefinitionList(current.DefinitionId);
                LoadMappingMachineCheckboxes(current.DefinitionId);
                SetMappingStatus(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "保存成功：已应用到 {0} 台机台，版本 v{1}",
                        selectedMachines.Count,
                        current.VersionNumber),
                    Color.DarkGreen);
                UpdateMappingCommandState();
                MessageBox.Show(
                    this,
                    string.Format(CultureInfo.InvariantCulture, "保存成功，当前适用于 {0} 台机台。", selectedMachines.Count),
                    "保存完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowMappingError("保存字段映射失败", ex);
            }
        }

        private ParseRuleVersion SaveMappingDraft(MappingRuleDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException("definition");

            int expectedRevision = 0;
            if (definition.DefinitionId > 0)
            {
                if (_mappingCurrentVersion == null ||
                    _mappingCurrentVersion.DefinitionId != definition.DefinitionId)
                    throw new ParseRuleStateException("当前界面没有与映射定义一致的版本快照，请重新加载。");
                expectedRevision = _mappingCurrentVersion.Revision;
            }

            ParseRuleVersion saved = _mappingRuleStore.SaveDraft(definition, expectedRevision);
            _mappingCurrentVersion = saved;
            _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(saved.DefinitionJson);
            _mappingTemplateSignature = _mappingCurrentDefinition.TemplateSignature;
            AcceptMappingEditorStateAsClean();
            _mappingModelCombo.Enabled = false;
            RefreshMappingDefinitionList(saved.DefinitionId);
            UpdateMappingVersionStatus();
            UpdateMappingCommandState();
            return saved;
        }

        private bool EnsureMasterDetailRelationFieldAndSource()
        {
            MappingModelChoice detailModel = GetSelectedMappingDetailModel();
            if (detailModel == null)
                throw new MappingValidationException("请选择有效的子模型。");
            string fieldName = (_mappingParentCidTextBox.Text ?? string.Empty).Trim();
            if (!MappingRuleSerializer.IsIdentifier(fieldName) ||
                string.Equals(fieldName, "CID", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("关联字段必须是非 CID 的合法标识符。");

            bool exists;
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT COUNT(1) FROM ModelFields
WHERE ModelId=@ModelId AND FieldName=@FieldName;";
                command.Parameters.Add("@ModelId", DbType.Int32).Value = detailModel.Id;
                command.Parameters.Add("@FieldName", DbType.String).Value = fieldName;
                exists = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
            if (!exists)
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    "子模型缺少关联字段 " + fieldName + "。\r\n\r\n" +
                    "系统将把它作为可空 long、非主键、非自增的系统字段加入数据模型，并重新生成模型代码；不会修改已有业务字段。是否继续？",
                    "增加主子表关联字段",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirmation != DialogResult.Yes) return false;
            }

            RelationFieldProvisionResult result = new MasterDetailConfigurationService(
                DatabaseHelper.GetConnectionString()).EnsureRelationField(detailModel.Id, fieldName);
            if (result.Created)
            {
                var model = new ModelConfig
                {
                    Id = detailModel.Id,
                    ModelName = detailModel.ModelName,
                    TableName = detailModel.TableName,
                    IsActive = true
                };
                GenerateModelClass(model, LoadFieldList(detailModel.Id), false);
                SetMappingStatus("已增加子表关联字段并重新生成模型代码：" + fieldName, Color.DarkGreen);
            }
            return true;
        }

        private MappingRuleDefinition BuildMappingDefinition(bool requireRequiredMappings)
        {
            dataGridView1.EndEdit();
            MappingModelChoice model = GetSelectedMappingModel();
            if (model == null)
                throw new MappingValidationException("请选择有效模型。");
            if (!MappingRuleSerializer.IsIdentifier(model.ModelName))
                throw new MappingValidationException("模型名称必须是合法的 C# 标识符。");
            if (string.IsNullOrWhiteSpace(_mappingRuleNameTextBox.Text))
                throw new MappingValidationException("规则名称不能为空。");

            string extension = _mappingSnapshot == null
                ? (_mappingCurrentDefinition == null ? null : _mappingCurrentDefinition.NormalizedExtension)
                : _mappingSnapshot.FileExtension;
            if (string.IsNullOrWhiteSpace(extension))
                throw new MappingValidationException("新映射必须先选择样本以确定扩展名。");
            string sheetName = Convert.ToString(_mappingSheetCombo.SelectedItem, CultureInfo.InvariantCulture);
            if (GetSelectedMappingMode() != MappingRecordMode.ImageFileName && string.IsNullOrWhiteSpace(sheetName))
                throw new MappingValidationException("请选择工作表。");

            if (GetSelectedMappingMode() == MappingRecordMode.ImageFileName)
                return BuildImageFileNameMappingDefinition(model, extension, requireRequiredMappings);

            if (GetSelectedMappingMode() == MappingRecordMode.MasterDetail)
            {
                return BuildMasterDetailMappingDefinition(
                    model,
                    extension,
                    sheetName,
                    requireRequiredMappings);
            }

            var missingRequired = new List<string>();
            var definition = new MappingRuleDefinition
            {
                DefinitionId = _mappingCurrentDefinition == null ? 0 : _mappingCurrentDefinition.DefinitionId,
                RuleName = _mappingRuleNameTextBox.Text.Trim(),
                ModelId = model.Id,
                TargetModelType = model.ModelName,
                ModelSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(model.Id)),
                NormalizedExtension = extension,
                SheetName = sheetName,
                CsvOptions = string.Equals(extension, ".csv", StringComparison.Ordinal)
                    ? GetCsvOptionsFromEditor()
                    : null,
                TemplateSignature = _mappingTemplateSignature,
                RecordMode = GetSelectedMappingMode(),
                RepeatedRows = GetSelectedMappingMode() == MappingRecordMode.RepeatingRows
                    ? _mappingRepeatedRows
                    : null
            };
            if (definition.RecordMode == MappingRecordMode.RepeatingRows && definition.RepeatedRows == null)
                throw new MappingValidationException("请先在样本网格框选表头和首条数据，并配置重复行列映射。");

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                var metadata = row.Tag as MappingRowMetadata;
                if (metadata != null && metadata.IsOrphan)
                    continue;
                bool required = Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false);
                if (!HasMappingLocator(row))
                {
                    if (requireRequiredMappings && required)
                    {
                        missingRequired.Add(Convert.ToString(
                            row.Cells[MappingTargetFieldColumn].Value,
                            CultureInfo.InvariantCulture));
                    }
                    continue;
                }
                definition.Fields.Add(CreateMappingFieldRule(row));
            }

            if (missingRequired.Count > 0)
                throw new MappingValidationException("必填字段尚未映射：" + string.Join(", ", missingRequired));
            if (definition.Fields.Count == 0)
                throw new MappingValidationException("草稿至少需要一个已配置的字段映射。");

            MappingRuleSerializer.ValidateDefinition(definition);
            return definition;
        }

        private MappingRuleDefinition BuildImageFileNameMappingDefinition(
            MappingModelChoice model,
            string extension,
            bool requireRequiredMappings)
        {
            string sharedRoot = (_mappingImageRootTextBox.Text ?? string.Empty).Trim();
            string pathField = Convert.ToString(
                _mappingImagePathFieldCombo.SelectedItem,
                CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(sharedRoot))
                throw new MappingValidationException("请配置图片共享目录。");
            if (string.IsNullOrWhiteSpace(pathField))
                throw new MappingValidationException("请选择保存共享图片地址的 string 字段。");

            int segmentCount;
            if (!string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text))
                segmentCount = FileNameExtractionParser.Inspect(_mappingSamplePathTextBox.Text).Segments.Count;
            else if (_mappingCurrentDefinition != null && _mappingCurrentDefinition.ImageArchive != null &&
                _mappingCurrentDefinition.ImageArchive.FileName != null)
                segmentCount = _mappingCurrentDefinition.ImageArchive.FileName.ExpectedSegmentCount;
            else
                throw new MappingValidationException("图片文件名映射必须选择图片样本。");

            var definition = new MappingRuleDefinition
            {
                DefinitionId = _mappingCurrentDefinition == null ? 0 : _mappingCurrentDefinition.DefinitionId,
                RuleName = _mappingRuleNameTextBox.Text.Trim(),
                ModelId = model.Id,
                TargetModelType = model.ModelName,
                ModelSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(model.Id)),
                NormalizedExtension = extension,
                SheetName = null,
                TemplateSignature = _mappingTemplateSignature,
                RecordMode = MappingRecordMode.ImageFileName,
                ImageArchive = new ImageArchiveDefinition
                {
                    SharedRootPath = sharedRoot,
                    PathTargetField = pathField,
                    PathTargetType = "string",
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = segmentCount }
                }
            };
            var missingRequired = new List<string>();
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                var metadata = row.Tag as MappingRowMetadata;
                if (metadata != null && metadata.IsOrphan) continue;
                if (string.Equals(
                    CellText(row, MappingTargetFieldColumn),
                    pathField,
                    StringComparison.Ordinal))
                    continue;
                bool required = Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false);
                if (!HasMappingLocator(row))
                {
                    if (requireRequiredMappings && required)
                        missingRequired.Add(CellText(row, MappingTargetFieldColumn));
                    continue;
                }
                definition.Fields.Add(CreateMappingFieldRule(row));
            }
            if (missingRequired.Count > 0)
                throw new MappingValidationException("必填字段尚未映射：" + string.Join(", ", missingRequired));
            if (definition.Fields.Count == 0)
                throw new MappingValidationException("图片规则至少需要一个文件名字段映射。");
            MappingRuleSerializer.ValidateDefinition(definition);
            return definition;
        }

        private MappingRuleDefinition BuildMasterDetailMappingDefinition(
            MappingModelChoice masterModel,
            string extension,
            string sheetName,
            bool requireRequiredMappings)
        {
            MappingModelChoice detailModel = GetSelectedMappingDetailModel();
            if (detailModel == null)
                throw new MappingValidationException("请选择有效的子模型。");
            if (detailModel.Id == masterModel.Id)
                throw new MappingValidationException("主模型和子模型不能相同。");
            if (!MappingRuleSerializer.IsIdentifier(detailModel.ModelName))
                throw new MappingValidationException("子模型名称必须是合法的 C# 标识符。");
            string parentCidField = (_mappingParentCidTextBox.Text ?? string.Empty).Trim();
            if (!MappingRuleSerializer.IsIdentifier(parentCidField) ||
                string.Equals(parentCidField, "CID", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("关联字段必须是非 CID 的合法标识符。");
            if (_mappingRepeatedRows == null)
                throw new MappingValidationException("请先框选表头和首条数据，配置子表重复行列映射。");

            int segmentCount;
            if (!string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text))
                segmentCount = FileNameExtractionParser.Inspect(_mappingSamplePathTextBox.Text).Segments.Count;
            else if (_mappingCurrentDefinition != null &&
                _mappingCurrentDefinition.MasterDetail != null &&
                _mappingCurrentDefinition.MasterDetail.FileName != null)
                segmentCount = _mappingCurrentDefinition.MasterDetail.FileName.ExpectedSegmentCount;
            else
                throw new MappingValidationException("主子表映射必须选择文件名样本。");

            var masterTarget = new MappingTargetDefinition
            {
                ModelId = masterModel.Id,
                TargetModelType = masterModel.ModelName,
                ModelSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(masterModel.Id))
            };
            var detailTarget = new MappingTargetDefinition
            {
                ModelId = detailModel.Id,
                TargetModelType = detailModel.ModelName,
                ModelSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(detailModel.Id)),
                RepeatedRows = _mappingRepeatedRows
            };
            var missingRequired = new List<string>();
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                var metadata = row.Tag as MappingRowMetadata;
                if (metadata != null && metadata.IsOrphan) continue;
                bool required = Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false);
                if (!HasMappingLocator(row))
                {
                    if (requireRequiredMappings && required)
                        missingRequired.Add((metadata != null && metadata.IsMaster ? "主表." : "子表.") +
                            CellText(row, MappingTargetFieldColumn));
                    continue;
                }
                FieldMappingRule field = CreateMappingFieldRule(row);
                if (metadata != null && metadata.IsMaster)
                    masterTarget.Fields.Add(field);
                else
                    detailTarget.Fields.Add(field);
            }
            if (missingRequired.Count > 0)
                throw new MappingValidationException("必填字段尚未映射：" + string.Join(", ", missingRequired));
            if (masterTarget.Fields.Count == 0)
                throw new MappingValidationException("主表至少需要一个文件名字段映射。");
            if (detailTarget.Fields.Count == 0)
                throw new MappingValidationException("子表至少需要一个重复行字段映射。");

            var definition = new MappingRuleDefinition
            {
                DefinitionId = _mappingCurrentDefinition == null ? 0 : _mappingCurrentDefinition.DefinitionId,
                RuleName = _mappingRuleNameTextBox.Text.Trim(),
                ModelId = masterModel.Id,
                TargetModelType = masterModel.ModelName,
                ModelSchemaHash = masterTarget.ModelSchemaHash,
                NormalizedExtension = extension,
                SheetName = sheetName,
                CsvOptions = string.Equals(extension, ".csv", StringComparison.Ordinal)
                    ? GetCsvOptionsFromEditor()
                    : null,
                TemplateSignature = _mappingTemplateSignature,
                RecordMode = MappingRecordMode.MasterDetail,
                MasterDetail = new MasterDetailMappingDefinition
                {
                    Master = masterTarget,
                    Detail = detailTarget,
                    ParentCidField = parentCidField,
                    FileName = new FileNameExtractionDefinition
                    {
                        ExpectedSegmentCount = segmentCount
                    }
                }
            };
            MappingRuleSerializer.ValidateDefinition(definition);
            return definition;
        }

        private FieldMappingRule CreateMappingFieldRule(DataGridViewRow row)
        {
            FieldMappingRule field = CreateFieldRuleMetadata(row);
            field.Scope = GetMappingFieldScope(row);
            string locatorType = CellText(row, MappingLocatorTypeColumn);
            string locatorValue = CellText(row, MappingLocatorValueColumn);
            bool isFileNameLocator = locatorType == "fileNameFull" ||
                locatorType == "fileNameStem" ||
                locatorType == "fileNameSegment";
            var locator = new MappingLocator
            {
                Type = locatorType,
                RowOffset = isFileNameLocator
                    ? 0
                    : ParseMappingInteger(row, MappingRowOffsetColumn, "行偏移"),
                ColumnOffset = isFileNameLocator
                    ? 0
                    : ParseMappingInteger(row, MappingColumnOffsetColumn, "列偏移"),
                ValueColumn = isFileNameLocator
                    ? null
                    : CellText(row, MappingValueColumnColumn),
                DataRowOffset = isFileNameLocator
                    ? 0
                    : ParseMappingInteger(row, MappingDataRowOffsetColumn, "数据行偏移")
            };
            if (locatorType == "fileNameSegment")
            {
                int oneBasedSegment;
                if (!int.TryParse(
                    locatorValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out oneBasedSegment) || oneBasedSegment <= 0)
                    throw new MappingValidationException(field.TargetField + " 的文件名片段序号必须是正整数。");
                locator.SegmentIndex = oneBasedSegment - 1;
            }
            else if (locatorType == "cell")
            {
                locator.Cell = locatorValue;
                var anchorMetadata = row.Tag as MappingRowMetadata;
                locator.AnchorCell = anchorMetadata == null ? null : anchorMetadata.AnchorCell;
                locator.AnchorText = anchorMetadata == null ? null : anchorMetadata.AnchorText;
            }
            else if (!isFileNameLocator)
                locator.Text = locatorValue;
            field.Locator = locator;

            field.Transforms = ParseMappingTransforms(CellText(row, MappingTransformsColumn));
            field.ExactValueMap = ParseMappingValueMap(CellText(row, MappingValueMapColumn));
            if (field.ExactValueMap.Count > 0 && !field.Transforms.Contains("valueMap"))
                field.Transforms.Add("valueMap");
            if (field.ExactValueMap.Count == 0 && field.Transforms.Contains("valueMap"))
                throw new MappingValidationException(field.TargetField + " 配置了 valueMap 转换但没有精确值映射。");
            field.DefaultValue = CellText(row, MappingDefaultValueColumn);
            if (!string.IsNullOrEmpty(field.DefaultValue) && !field.Transforms.Contains("default"))
                field.Transforms.Add("default");
            var metadata = row.Tag as MappingRowMetadata;
            field.ConfirmationState = metadata == null
                ? MappingConfirmationState.Unconfirmed
                : metadata.ConfirmationState;
            return field;
        }

        private static FieldMappingRule CreateFieldRuleMetadata(DataGridViewRow row)
        {
            return new FieldMappingRule
            {
                TargetField = CellText(row, MappingTargetFieldColumn),
                TargetType = CellText(row, MappingTargetTypeColumn),
                TargetDescription = CellText(row, MappingDescriptionColumn),
                IsRequired = Convert.ToBoolean(row.Cells[MappingRequiredColumn].Value ?? false)
            };
        }

        private static List<string> ParseMappingTransforms(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(transform => transform.Trim())
                .Where(transform => transform.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static Dictionary<string, string> ParseMappingValueMap(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string item in (value ?? string.Empty)
                .Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = item.IndexOf('=');
                if (separator < 0) separator = item.IndexOf('＝');
                if (separator <= 0)
                    throw new MappingValidationException("精确值映射必须使用 源值=目标值，并用分号分隔。");
                string source = item.Substring(0, separator).Trim();
                string target = item.Substring(separator + 1).Trim();
                if (source.Length == 0 || source.Length > 256 || target.Length > 256 ||
                    source.Any(char.IsControl) || target.Any(char.IsControl))
                    throw new MappingValidationException("精确值映射的源值或目标值无效。");
                if (result.ContainsKey(source))
                    throw new MappingValidationException("精确值映射的源值不能重复：" + source);
                result.Add(source, target);
                if (result.Count > 128)
                    throw new MappingValidationException("单个字段最多允许 128 个精确值映射。");
            }
            return result;
        }

        private static string FormatMappingValueMap(IDictionary<string, string> valueMap)
        {
            if (valueMap == null || valueMap.Count == 0) return string.Empty;
            return string.Join(";", valueMap
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key + "=" + item.Value));
        }

        private static int ParseMappingInteger(DataGridViewRow row, string columnName, string displayName)
        {
            string value = CellText(row, columnName);
            if (string.IsNullOrWhiteSpace(value)) return 0;
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                throw new MappingValidationException(
                    CellText(row, MappingTargetFieldColumn) + " 的" + displayName + "不是整数。");
            return parsed;
        }

        private static string CellText(DataGridViewRow row, string columnName)
        {
            return Convert.ToString(row.Cells[columnName].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static MappingFieldScope GetMappingFieldScope(DataGridViewRow row)
        {
            return string.Equals(CellText(row, MappingScopeColumn), "明细列", StringComparison.Ordinal)
                ? MappingFieldScope.RowColumn
                : MappingFieldScope.Common;
        }

        private static bool HasMappingLocator(DataGridViewRow row)
        {
            string locatorType = CellText(row, MappingLocatorTypeColumn);
            if (string.Equals(locatorType, "fileNameFull", StringComparison.Ordinal) ||
                string.Equals(locatorType, "fileNameStem", StringComparison.Ordinal))
                return true;
            if (string.Equals(locatorType, "fileNameSegment", StringComparison.Ordinal))
            {
                int oneBasedSegment;
                return int.TryParse(
                    CellText(row, MappingLocatorValueColumn),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out oneBasedSegment) && oneBasedSegment > 0;
            }
            if (string.Equals(locatorType, "rowColumn", StringComparison.Ordinal))
                return GetMappingFieldScope(row) == MappingFieldScope.RowColumn;
            return !string.IsNullOrWhiteSpace(locatorType) &&
                   !string.IsNullOrWhiteSpace(CellText(row, MappingLocatorValueColumn));
        }

        private bool MappingDefinitionMatchesCurrentVersion(MappingRuleDefinition definition)
        {
            if (_mappingCurrentVersion == null || definition == null ||
                definition.DefinitionId <= 0 ||
                definition.DefinitionId != _mappingCurrentVersion.DefinitionId)
                return false;
            string json = MappingRuleSerializer.Serialize(definition);
            return string.Equals(
                MappingRuleSerializer.Sha256(json),
                _mappingCurrentVersion.ContentSha256,
                StringComparison.Ordinal);
        }

        private void DisplayMappingPreview(MappingPreviewResult preview)
        {
            PopulateMappingRecordsGrid(preview);
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                ClearMappingPreviewRow(row);
                string target = CellText(row, MappingTargetFieldColumn);
                if (GetSelectedMappingMode() == MappingRecordMode.ImageFileName &&
                    string.Equals(
                        target,
                        Convert.ToString(_mappingImagePathFieldCombo.SelectedItem, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                {
                    row.Cells[MappingPreviewCellColumn].Value = "系统";
                    row.Cells[MappingPreviewResultColumn].Value = "运行时写入共享路径";
                    row.Cells[MappingPreviewResultColumn].Style.ForeColor = Color.RoyalBlue;
                    continue;
                }
                MappingPreviewFieldResult field;
                if (!preview.Fields.TryGetValue(target, out field) && preview.Records.Count > 0)
                    preview.Records[0].Fields.TryGetValue(target, out field);
                if (field != null)
                {
                    row.Cells[MappingPreviewCellColumn].Value = field.SourceCell ?? string.Empty;
                    row.Cells[MappingPreviewValueColumn].Value = Convert.ToString(
                        field.RawValue,
                        CultureInfo.InvariantCulture);
                    row.Cells[MappingPreviewConvertedValueColumn].Value = Convert.ToString(
                        field.Value,
                        CultureInfo.InvariantCulture);
                    row.Cells[MappingPreviewResultColumn].Value =
                        field.ErrorCode ?? field.WarningCode ?? "通过";
                    if (!string.IsNullOrWhiteSpace(field.ErrorCode))
                        row.Cells[MappingPreviewResultColumn].Style.ForeColor = Color.Firebrick;
                    else if (!string.IsNullOrWhiteSpace(field.WarningCode))
                        row.Cells[MappingPreviewResultColumn].Style.ForeColor = Color.DarkOrange;
                    else
                        row.Cells[MappingPreviewResultColumn].Style.ForeColor = Color.DarkGreen;
                }
                else if (HasMappingLocator(row))
                {
                    row.Cells[MappingPreviewResultColumn].Value = "未返回";
                }
                else
                {
                    row.Cells[MappingPreviewResultColumn].Value = "未映射";
                }
            }
        }

        private void PopulateMappingRecordsGrid(MappingPreviewResult preview)
        {
            _mappingRecordsGrid.Rows.Clear();
            _mappingRecordsGrid.Columns.Clear();
            if (preview == null || (preview.Records.Count == 0 && preview.Fields.Count == 0)) return;

            _mappingRecordsGrid.Columns.Add("PreviewSourceRow", "归属 / 源文件行");
            List<string> targets = preview.Fields.Keys
                .Concat(preview.Records.SelectMany(record => record.Fields.Keys))
                .Distinct(StringComparer.Ordinal).ToList();
            bool isMasterDetail = GetSelectedMappingMode() == MappingRecordMode.MasterDetail;
            string parentCidField = (_mappingParentCidTextBox.Text ?? string.Empty).Trim();
            if (isMasterDetail && !string.IsNullOrWhiteSpace(parentCidField) &&
                !targets.Contains(parentCidField, StringComparer.Ordinal))
                targets.Add(parentCidField);
            foreach (string target in targets)
                _mappingRecordsGrid.Columns.Add("Preview_" + target, target);

            if (preview.Fields.Count > 0)
            {
                object[] masterValues = new object[targets.Count + 1];
                masterValues[0] = isMasterDetail ? "主表" : "公共字段";
                for (int index = 0; index < targets.Count; index++)
                {
                    MappingPreviewFieldResult field;
                    if (preview.Fields.TryGetValue(targets[index], out field))
                        masterValues[index + 1] = field.ErrorCode ?? Convert.ToString(field.Value, CultureInfo.InvariantCulture);
                }
                _mappingRecordsGrid.Rows.Add(masterValues);
            }

            foreach (MappingPreviewRecordResult record in preview.Records)
            {
                object[] values = new object[targets.Count + 1];
                values[0] = record.ExcelRowNumber;
                for (int index = 0; index < targets.Count; index++)
                {
                    MappingPreviewFieldResult field;
                    if (record.Fields.TryGetValue(targets[index], out field))
                        values[index + 1] = field.ErrorCode ?? Convert.ToString(field.Value, CultureInfo.InvariantCulture);
                    else if (isMasterDetail && string.Equals(targets[index], parentCidField, StringComparison.Ordinal))
                        values[index + 1] = "<运行时主表CID>";
                }
                int rowIndex = _mappingRecordsGrid.Rows.Add(values);
                for (int index = 0; index < targets.Count; index++)
                {
                    MappingPreviewFieldResult field;
                    if (!record.Fields.TryGetValue(targets[index], out field)) continue;
                    DataGridViewCell cell = _mappingRecordsGrid.Rows[rowIndex].Cells[index + 1];
                    cell.Tag = field;
                    cell.ToolTipText = string.Join(" / ", new[]
                    {
                        field.SourceCell ?? string.Empty,
                        Convert.ToString(field.RawValue, CultureInfo.InvariantCulture) ?? string.Empty,
                        field.ErrorCode ?? field.WarningCode ?? "通过"
                    });
                    if (!string.IsNullOrWhiteSpace(field.ErrorCode))
                        cell.Style.BackColor = Color.MistyRose;
                    else if (!string.IsNullOrWhiteSpace(field.WarningCode))
                        cell.Style.BackColor = Color.LemonChiffon;
                }
            }
        }

        private void MappingRecordsGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex <= 0) return;
            var field = _mappingRecordsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as MappingPreviewFieldResult;
            if (field == null || string.IsNullOrWhiteSpace(field.SourceCell)) return;

            int rowIndex;
            int columnIndex;
            if (!TryParseMappingCoordinate(field.SourceCell, out rowIndex, out columnIndex) ||
                rowIndex < 0 || rowIndex >= _mappingSampleGrid.Rows.Count ||
                columnIndex < 0 || columnIndex >= _mappingSampleGrid.Columns.Count)
                return;

            var samplePage = _mappingSampleGrid.Parent as TabPage;
            var sampleTabs = samplePage == null ? null : samplePage.Parent as TabControl;
            if (sampleTabs != null) sampleTabs.SelectedTab = samplePage;
            _mappingSampleGrid.ClearSelection();
            _mappingSampleGrid.CurrentCell = _mappingSampleGrid.Rows[rowIndex].Cells[columnIndex];
            _mappingSampleGrid.CurrentCell.Selected = true;
            if (rowIndex >= 0) _mappingSampleGrid.FirstDisplayedScrollingRowIndex = rowIndex;
            if (columnIndex >= 0) _mappingSampleGrid.FirstDisplayedScrollingColumnIndex = columnIndex;
            _mappingSampleGrid.Focus();
        }

        private static string BuildMappingValidationSummary(MappingPreviewResult preview)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "sample={0};template={1};fields={2};records={3};warnings={4}",
                preview.SampleSha256,
                preview.TemplateSignature,
                preview.Fields.Count,
                preview.Records.Count,
                string.Join(",", preview.WarningCodes));
        }

        private void MappingHistoryButton_Click(object sender, EventArgs e)
        {
            if (_mappingCurrentVersion == null) return;
            try
            {
                List<ParseRuleVersion> versions = LoadMappingHistory(_mappingCurrentVersion.DefinitionId);
                List<MappingMachineChoice> machines = LoadMappingMachines();
                ShowMappingHistoryDialog(versions, machines);
            }
            catch (Exception ex)
            {
                ShowMappingError("加载历史版本失败", ex);
            }
        }

        private void MappingDeleteButton_Click(object sender, EventArgs e)
        {
            if (_mappingCurrentVersion == null || _mappingCurrentVersion.DefinitionId <= 0)
            {
                MessageBox.Show(this, "请先选择要删除的字段映射。", "删除映射", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_mappingDirty && !ConfirmDiscardMappingChanges()) return;
            if (MessageBox.Show(
                    this,
                    "确定删除字段映射“" + (_mappingCurrentVersion.RuleName ?? "当前映射") + "”吗？其机台发布绑定和所有版本数据会一并删除，且不可恢复。",
                    "确认删除映射",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                new ConfigurationDeletionService(DatabaseHelper.GetDatabasePath())
                    .DeleteMappingDefinition(_mappingCurrentVersion.DefinitionId);
                ScriptEngine.ClearCache();
                RefreshMappingDefinitionList(null);
                StartNewMapping(false);
                SetMappingStatus("字段映射已删除。", Color.DarkGreen);
            }
            catch (ConfigurationDeletionBlockedException ex)
            {
                MessageBox.Show(this, ex.Message, "无法删除映射", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowMappingError("删除字段映射失败", ex);
            }
        }

        private void ShowMappingHistoryDialog(
            List<ParseRuleVersion> versions,
            List<MappingMachineChoice> machines)
        {
            ParseRuleVersion rollbackResult = null;
            using (var dialog = new Form
            {
                Text = "映射历史版本 / 回滚",
                StartPosition = FormStartPosition.CenterParent,
                Width = 940,
                Height = 560,
                MinimizeBox = false,
                MaximizeBox = true,
                ShowInTaskbar = false
            })
            {
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    MultiSelect = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
                };
                grid.Columns.Add("HistoryVersion", "版本");
                grid.Columns.Add("HistoryStatus", "状态");
                grid.Columns.Add("HistoryRevision", "修订");
                grid.Columns.Add("HistoryCreated", "创建时间（本地）");
                grid.Columns.Add("HistoryValidation", "验证摘要");
                grid.Columns.Add("HistoryHash", "内容哈希");
                foreach (ParseRuleVersion version in versions)
                {
                    int rowIndex = grid.Rows.Add(
                        "v" + version.VersionNumber.ToString(CultureInfo.InvariantCulture),
                        MappingStatusText(version.Status),
                        version.Revision,
                        version.CreatedTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
                        version.ValidationSummary ?? string.Empty,
                        ShortHash(version.ContentSha256));
                    grid.Rows[rowIndex].Tag = version;
                }

                var footer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 52,
                    FlowDirection = FlowDirection.LeftToRight,
                    Padding = new Padding(8)
                };
                footer.Controls.Add(new Label
                {
                    AutoSize = true,
                    Margin = new Padding(0, 8, 3, 0),
                    Text = "目标机台："
                });
                var machineCombo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 250
                };
                foreach (MappingMachineChoice machine in machines)
                    machineCombo.Items.Add(machine);
                if (machineCombo.Items.Count > 0) machineCombo.SelectedIndex = 0;
                footer.Controls.Add(machineCombo);
                var rollback = new Button { Text = "回滚并发布", Width = 120, Height = 32 };
                var close = new Button { Text = "关闭", Width = 90, Height = 32 };
                footer.Controls.Add(rollback);
                footer.Controls.Add(close);

                rollback.Click += delegate
                {
                    if (grid.SelectedRows.Count == 0 || machineCombo.SelectedItem == null)
                    {
                        MessageBox.Show(dialog, "请选择历史版本和目标机台。", "回滚", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    var historical = grid.SelectedRows[0].Tag as ParseRuleVersion;
                    var machine = machineCombo.SelectedItem as MappingMachineChoice;
                    if (historical == null || machine == null) return;
                    if (historical.Status != ParseRuleStatus.Published &&
                        historical.Status != ParseRuleStatus.Superseded)
                    {
                        MessageBox.Show(dialog, "只能回滚到曾发布过的版本。", "回滚", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (MessageBox.Show(
                            dialog,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "确认将 {0} 回滚到 v{1}？系统会创建一个新的已发布版本，历史记录不会被改写。",
                                machine,
                                historical.VersionNumber),
                            "确认回滚",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    try
                    {
                        string machineId = machine.Id.ToString(CultureInfo.InvariantCulture);
                        ParseRuleVersion previouslyPublished = _mappingRuleStore.GetPublished(
                            machineId,
                            historical.NormalizedExtension);
                        if (previouslyPublished == null)
                            throw new ParseRuleStateException("目标机台当前没有可替换的已发布规则。");
                        rollbackResult = _mappingRuleStore.Rollback(
                            historical.Id,
                            machineId,
                            previouslyPublished.Id);
                        ScriptEngine.ClearCache(previouslyPublished.Id);
                        ScriptEngine.ClearCache(rollbackResult.Id);
                        dialog.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, ex.Message, "回滚失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                close.Click += delegate { dialog.Close(); };

                dialog.Controls.Add(grid);
                dialog.Controls.Add(footer);
                if (grid.Rows.Count > 1)
                    grid.Rows[1].Selected = true;
                else if (grid.Rows.Count == 1)
                    grid.Rows[0].Selected = true;
                dialog.ShowDialog(this);
            }

            if (rollbackResult != null)
            {
                _mappingCurrentVersion = rollbackResult;
                _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(rollbackResult.DefinitionJson);
                _mappingTemplateSignature = _mappingCurrentDefinition.TemplateSignature;
                AcceptMappingEditorStateAsClean();
                RefreshMappingDefinitionList(rollbackResult.DefinitionId);
                LoadMappingVersion(rollbackResult);
                SetMappingStatus(
                    string.Format(CultureInfo.InvariantCulture, "回滚完成，已创建并发布 v{0}", rollbackResult.VersionNumber),
                    Color.DarkGreen);
            }
        }

        private MappingModelChoice GetSelectedMappingModel()
        {
            return _mappingModelCombo.SelectedItem as MappingModelChoice;
        }

        private MappingModelChoice GetSelectedMappingDetailModel()
        {
            return _mappingDetailModelCombo == null
                ? null
                : _mappingDetailModelCombo.SelectedItem as MappingModelChoice;
        }

        private void EnsureMappedRowsHumanConfirmed()
        {
            List<string> pending = dataGridView1.Rows
                .Cast<DataGridViewRow>()
                .Where(row => HasMappingLocator(row))
                .Where(row => GetSelectedMappingMode() != MappingRecordMode.ImageFileName ||
                    !string.Equals(
                        CellText(row, MappingTargetFieldColumn),
                        Convert.ToString(_mappingImagePathFieldCombo.SelectedItem, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                .Where(row =>
                {
                    var metadata = row.Tag as MappingRowMetadata;
                    return metadata == null ||
                           metadata.ConfirmationState != MappingConfirmationState.HumanConfirmed;
                })
                .Select(row => CellText(row, MappingTargetFieldColumn))
                .ToList();
            if (pending.Count > 0)
            {
                throw new MappingValidationException(
                    "以下映射仍是未确认/辅助候选，验证前必须逐项勾选人工确认：" +
                    string.Join(", ", pending));
            }
        }

        private void UpdateMappingConfirmationCell(DataGridViewRow row)
        {
            var metadata = row.Tag as MappingRowMetadata;
            MappingConfirmationState state = metadata == null
                ? MappingConfirmationState.Unconfirmed
                : metadata.ConfirmationState;
            row.Cells[MappingConfirmationColumn].Value = MappingConfirmationText(state);
            row.Cells[MappingConfirmationColumn].Style.ForeColor =
                state == MappingConfirmationState.HumanConfirmed
                    ? Color.DarkGreen
                    : (state == MappingConfirmationState.Unconfirmed ? Color.DimGray : Color.DarkOrange);
        }

        private static string MappingConfirmationText(MappingConfirmationState state)
        {
            switch (state)
            {
                case MappingConfirmationState.LocalCandidate: return "本地候选";
                case MappingConfirmationState.AiPendingConfirmation: return "AI 待确认";
                case MappingConfirmationState.HumanConfirmed: return "人工确认";
                default: return "未确认";
            }
        }

        private static string MappingStatusText(ParseRuleStatus status)
        {
            switch (status)
            {
                case ParseRuleStatus.Draft: return "草稿";
                case ParseRuleStatus.Validated: return "已验证";
                case ParseRuleStatus.Published: return "已发布";
                case ParseRuleStatus.Superseded: return "已被替代";
                default: return status.ToString();
            }
        }

        private void ClearMappingPreviewColumns()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
                ClearMappingPreviewRow(row);
        }

        private static void ClearMappingPreviewRow(DataGridViewRow row)
        {
            row.Cells[MappingPreviewCellColumn].Value = string.Empty;
            row.Cells[MappingPreviewValueColumn].Value = string.Empty;
            row.Cells[MappingPreviewConvertedValueColumn].Value = string.Empty;
            row.Cells[MappingPreviewResultColumn].Value = string.Empty;
            row.Cells[MappingPreviewResultColumn].Style.ForeColor = SystemColors.ControlText;
        }

        private void UpdateMappingVersionStatus()
        {
            if (_mappingCurrentVersion == null)
            {
                SetMappingStatus(
                    _mappingDirty ? "新映射（未保存）" : "新映射",
                    _mappingDirty ? Color.DarkOrange : Color.DimGray);
                return;
            }

            string text = string.Format(
                CultureInfo.InvariantCulture,
                "v{0} / {1} / 修订 {2}{3}",
                _mappingCurrentVersion.VersionNumber,
                MappingStatusText(_mappingCurrentVersion.Status),
                _mappingCurrentVersion.Revision,
                _mappingDirty ? " / 有未保存修改" : string.Empty);

            MappingModelChoice model = GetSelectedMappingModel();
            if (model != null)
            {
                string hash = ModelSchemaService.ComputeHash(LoadMappingFields(model.Id));
                if (!string.Equals(hash, _mappingCurrentVersion.ModelSchemaHash, StringComparison.Ordinal))
                {
                    text += " / 模型结构已变化";
                    SetMappingStatus(text, Color.Firebrick);
                    return;
                }
            }
            if (_mappingCurrentDefinition != null &&
                _mappingCurrentDefinition.RecordMode == MappingRecordMode.MasterDetail &&
                _mappingCurrentDefinition.MasterDetail != null)
            {
                MappingTargetDefinition detail = _mappingCurrentDefinition.MasterDetail.Detail;
                string detailHash = ModelSchemaService.ComputeHash(LoadMappingFields(detail.ModelId));
                if (!string.Equals(detailHash, detail.ModelSchemaHash, StringComparison.Ordinal))
                {
                    text += " / 子模型结构已变化";
                    SetMappingStatus(text, Color.Firebrick);
                    return;
                }
            }
            SetMappingStatus(text, _mappingDirty ? Color.DarkOrange : Color.DarkGreen);
        }

        private void SetMappingStatus(string text, Color color)
        {
            if (_mappingStatusLabel == null) return;
            _mappingStatusLabel.Text = text ?? string.Empty;
            _mappingStatusLabel.ForeColor = color;
            _mappingToolTip.SetToolTip(_mappingStatusLabel, text ?? string.Empty);
        }

        private void UpdateMappingCommandState()
        {
            if (_mappingSaveButton == null) return;
            bool ready = _mappingDataLoaded && _mappingRuleStore != null;
            bool hasModel = GetSelectedMappingModel() != null;
            bool isMasterDetail = GetSelectedMappingMode() == MappingRecordMode.MasterDetail;
            bool isImageFileName = GetSelectedMappingMode() == MappingRecordMode.ImageFileName;
            bool hasDetailModel = !isMasterDetail || GetSelectedMappingDetailModel() != null;
            bool hasSample = _mappingSnapshot != null &&
                (isImageFileName || GetSelectedMappingSheet() != null);
            bool hasImageSettings = !isImageFileName ||
                (!string.IsNullOrWhiteSpace(_mappingImageRootTextBox.Text) &&
                 _mappingImagePathFieldCombo.SelectedItem != null);
            bool hasDefinition = _mappingCurrentVersion != null;
            bool canReuseValidatedVersion = !MappingSavePolicy.RequiresSample(
                _mappingCurrentVersion,
                _mappingDirty);

            _mappingBrowseButton.Enabled = ready && !_mappingAiBusy;
            _mappingModelCombo.Enabled = ready && !_mappingAiBusy && _mappingCurrentDefinition == null;
            _mappingDetailModelCombo.Enabled = isMasterDetail && ready && !_mappingAiBusy && _mappingCurrentDefinition == null;
            _mappingParentCidTextBox.Enabled = isMasterDetail && ready && !_mappingAiBusy && _mappingCurrentDefinition == null;
            _mappingNewMasterModelButton.Enabled = isMasterDetail && ready && !_mappingAiBusy;
            _mappingImageRootTextBox.Enabled = isImageFileName && ready && !_mappingAiBusy;
            _mappingImagePathFieldCombo.Enabled = isImageFileName && ready && !_mappingAiBusy;
            _mappingFileNameInfoLabel.ForeColor = isMasterDetail || isImageFileName ? Color.DimGray : SystemColors.GrayText;
            _mappingRuleNameTextBox.Enabled = ready && !_mappingAiBusy;
            _mappingSheetCombo.Enabled = !isImageFileName && ready && !_mappingAiBusy && hasSample;
            _mappingModeCombo.Enabled = ready && !_mappingAiBusy;
            _mappingFrameTableButton.Enabled = ready && hasSample && !_mappingAiBusy &&
                (GetSelectedMappingMode() == MappingRecordMode.RepeatingRows || isMasterDetail);
            dataGridView1.Enabled = ready && !_mappingAiBusy;
            btnNewMappingScript.Enabled = ready && !_mappingAiBusy;
            listBoxMappingScripts.Enabled = ready && !_mappingAiBusy;
            _mappingLocalAssistButton.Enabled = !isImageFileName && ready && hasSample && !_mappingAiBusy &&
                GetMappingSuggestionEligibleRows().Count > 0;
            _mappingPickLocatorButton.Enabled = !isImageFileName && ready && hasSample && !_mappingAiBusy &&
                dataGridView1.CurrentRow != null &&
                GetMappingFieldScope(dataGridView1.CurrentRow) == MappingFieldScope.Common;
            _mappingAiAssistButton.Enabled = !isImageFileName && ready && hasSample && !_mappingAiBusy &&
                _mappingAiOptions != null && GetMappingSuggestionEligibleRows().Count > 0;
            _mappingAiAssistButton.Text = _mappingAiOptions == null ? "AI 未配置" : "AI 自动填映射";
            _mappingToolTip.SetToolTip(
                _mappingAiAssistButton,
                _mappingAiOptions == null
                    ? (_mappingAiUnavailableReason ?? "AI 未配置")
                    : "仅填充空白且未确认项；返回失败时不修改草稿");
            _mappingSaveButton.Enabled = ready && hasModel && hasDetailModel && hasImageSettings && !_mappingAiBusy &&
                (hasSample || canReuseValidatedVersion);
            _mappingDeleteButton.Enabled = ready && hasDefinition && !_mappingAiBusy;
        }

        private void SetMappingEditorEnabled(bool enabled)
        {
            btnNewMappingScript.Enabled = enabled;
            if (_mappingDeleteButton != null) _mappingDeleteButton.Enabled = enabled;
            listBoxMappingScripts.Enabled = enabled;
            panel2.Enabled = enabled;
            panel3.Enabled = enabled;
            dataGridView1.Enabled = enabled;
            _mappingSampleGrid.Enabled = enabled;
            _mappingRecordsGrid.Enabled = enabled;
        }

        private void ShowMappingError(string title, Exception exception)
        {
            SetMappingStatus(title + "：" + exception.Message, Color.Firebrick);
            MessageBox.Show(
                exception.Message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            UpdateMappingCommandState();
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            return value.Length <= 12 ? value : value.Substring(0, 12);
        }

        private sealed class MappingModeChoice
        {
            public MappingModeChoice(MappingRecordMode mode, string text)
            {
                Mode = mode;
                Text = text;
            }

            public MappingRecordMode Mode { get; private set; }
            public string Text { get; private set; }
            public override string ToString() { return Text; }
        }

        private sealed class MappingModelChoice
        {
            public int Id { get; set; }
            public string ModelName { get; set; }
            public string TableName { get; set; }
            public override string ToString() { return ModelName; }
        }

        private sealed class MappingMachineChoice
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Code { get; set; }
            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(Code) ? Name : Name + " (" + Code + ")";
            }
        }

        private sealed class MappingDefinitionListItem
        {
            public MappingDefinitionListItem(ParseRuleVersion version)
            {
                Version = version;
            }

            public ParseRuleVersion Version { get; private set; }

            public override string ToString()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}  v{1}  [{2}]",
                    Version.RuleName,
                    Version.VersionNumber,
                    MappingStatusText(Version.Status));
            }
        }

        private sealed class MappingRowMetadata
        {
            public MappingConfirmationState ConfirmationState { get; set; }
            public bool IsOrphan { get; set; }
            public bool IsMaster { get; set; }
            public string AnchorCell { get; set; }
            public string AnchorText { get; set; }
        }
    }
}

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

        private bool _mappingUiInitialized;
        private bool _mappingDataLoaded;
        private bool _mappingSuppressEvents;
        private bool _mappingApplyingSuggestion;
        private bool _mappingDirty;
        private bool _mappingAiBusy;
        private int _mappingSelectedListIndex = -1;

        private ComboBox _mappingModelCombo;
        private ComboBox _mappingSheetCombo;
        private TextBox _mappingRuleNameTextBox;
        private TextBox _mappingSamplePathTextBox;
        private Label _mappingStatusLabel;
        private Label _mappingExtensionLabel;
        private Button _mappingBrowseButton;
        private Button _mappingPickLocatorButton;
        private Button _mappingLocalAssistButton;
        private Button _mappingAiAssistButton;
        private Button _mappingSaveDraftButton;
        private Button _mappingValidateButton;
        private Button _mappingPublishButton;
        private Button _mappingHistoryButton;
        private DataGridView _mappingSampleGrid;
        private SplitContainer _mappingCenterSplit;
        private ToolTip _mappingToolTip;

        private ParseRuleStore _mappingRuleStore;
        private ExcelMappingPreviewService _mappingPreviewService;
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
        private MappingCellSnapshot _mappingPickAnchorCell;

        private void InitializeMappingUi()
        {
            if (_mappingUiInitialized) return;
            _mappingUiInitialized = true;

            panel2.SuspendLayout();
            panel3.SuspendLayout();
            splitContainer3.Panel2.SuspendLayout();
            try
            {
                panel2.Height = 112;
                panel3.Height = 62;
                btnNewMappingScript.Text = "+ 新建映射";

                BuildMappingHeader();
                BuildMappingCenter();
                BuildMappingFooter();
                ConfigureMappingGrid();
                WireMappingEvents();
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
                RowCount = 3,
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

            _mappingModelCombo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
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
            assistants.Controls.Add(_mappingPickLocatorButton);
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
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            _mappingSampleGrid.Columns.Add("SampleCoordinate", "单元格");
            _mappingSampleGrid.Columns.Add("SampleValue", "样本显示值");
            _mappingSampleGrid.Columns.Add("SampleType", "类型");
            _mappingSampleGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "SampleFormula",
                HeaderText = "公式",
                ReadOnly = true,
                FillWeight = 35F
            });
            _mappingSampleGrid.Columns[0].FillWeight = 40F;
            _mappingSampleGrid.Columns[1].FillWeight = 160F;
            _mappingSampleGrid.Columns[2].FillWeight = 55F;

            _mappingCenterSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                SplitterDistance = 170,
                Panel1MinSize = 90,
                Panel2MinSize = 140
            };
            var sampleGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "样本内容（只读）"
            };
            sampleGroup.Controls.Add(_mappingSampleGrid);
            var mappingGroup = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "字段映射"
            };
            dataGridView1.Dock = DockStyle.Fill;
            mappingGroup.Controls.Add(dataGridView1);
            _mappingCenterSplit.Panel1.Controls.Add(sampleGroup);
            _mappingCenterSplit.Panel2.Controls.Add(mappingGroup);

            middleParent.Controls.Add(_mappingCenterSplit);
            middleParent.Controls.SetChildIndex(_mappingCenterSplit, 0);
            panel2.BringToFront();
            panel3.BringToFront();
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
            _mappingPublishButton = CreateMappingActionButton("发布到机台", 120);
            _mappingValidateButton = CreateMappingActionButton("验证预览", 110);
            _mappingSaveDraftButton = CreateMappingActionButton("保存草稿", 100);
            _mappingHistoryButton = CreateMappingActionButton("历史版本 / 回滚", 145);
            actions.Controls.Add(_mappingPublishButton);
            actions.Controls.Add(_mappingValidateButton);
            actions.Controls.Add(_mappingSaveDraftButton);
            actions.Controls.Add(_mappingHistoryButton);
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

            var locatorType = new DataGridViewComboBoxColumn
            {
                Name = MappingLocatorTypeColumn,
                HeaderText = "定位方式",
                Width = 115,
                FlatStyle = FlatStyle.Flat
            };
            locatorType.Items.AddRange("cell", "labelOffset", "rowKey", "headerColumn");
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
            _mappingSheetCombo.SelectedIndexChanged += MappingSheetCombo_SelectedIndexChanged;
            _mappingRuleNameTextBox.TextChanged += MappingRuleName_TextChanged;
            _mappingBrowseButton.Click += MappingBrowseButton_Click;
            _mappingPickLocatorButton.Click += MappingPickLocatorButton_Click;
            _mappingLocalAssistButton.Click += MappingLocalAssistButton_Click;
            _mappingAiAssistButton.Click += MappingAiAssistButton_Click;
            _mappingSaveDraftButton.Click += MappingSaveDraftButton_Click;
            _mappingValidateButton.Click += MappingValidateButton_Click;
            _mappingPublishButton.Click += MappingPublishButton_Click;
            _mappingHistoryButton.Click += MappingHistoryButton_Click;
            dataGridView1.CellValueChanged += MappingGrid_CellValueChanged;
            dataGridView1.SelectionChanged += MappingGrid_SelectionChanged;
            dataGridView1.CurrentCellDirtyStateChanged += MappingGrid_CurrentCellDirtyStateChanged;
            dataGridView1.DataError += MappingGrid_DataError;
            _mappingSampleGrid.CellClick += MappingSampleGrid_CellClick;
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
        }

        private void MappingTab_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabPageMapping)
                ActivateMappingUi();
        }

        private void ActivateMappingUi()
        {
            if (_mappingDataLoaded) return;
            try
            {
                var connection = new SQLiteConnectionStringBuilder(DatabaseHelper.GetConnectionString());
                _mappingRuleStore = new ParseRuleStore(connection.DataSource);
                _mappingRuleStore.Initialize();
                _mappingPreviewService = new ExcelMappingPreviewService(LoadMappingForbiddenRoots());
                _mappingLocalAssistant = new LocalMappingAssistant();
                RefreshMappingAiConfiguration();
                LoadMappingModels();
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

        private void LoadMappingModels()
        {
            var models = new List<MappingModelChoice>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id, ModelName, TableName
FROM DataModels
WHERE IsActive = @IsActive
ORDER BY ModelName COLLATE BINARY, Id;";
                command.Parameters.Add("@IsActive", DbType.Int32).Value = 1;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        models.Add(new MappingModelChoice
                        {
                            Id = reader.GetInt32(0),
                            ModelName = reader.GetString(1),
                            TableName = reader.GetString(2)
                        });
                    }
                }
            }

            _mappingSuppressEvents = true;
            _mappingModelCombo.Items.Clear();
            foreach (MappingModelChoice model in models)
                _mappingModelCombo.Items.Add(model);
            if (_mappingModelCombo.Items.Count > 0)
                _mappingModelCombo.SelectedIndex = 0;
            _mappingSuppressEvents = false;
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
WHERE IsEnabled = @IsEnabled
ORDER BY SortOrder, Id;";
                command.Parameters.Add("@IsEnabled", DbType.Int32).Value = 1;
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

        private void RefreshMappingDefinitionList(long? selectedDefinitionId)
        {
            var items = new List<MappingDefinitionListItem>();
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = MappingVersionSelectSql + @"
WHERE v.RuleType = @RuleType
  AND v.VersionNumber = (
      SELECT MAX(v2.VersionNumber)
      FROM ParseRuleVersions v2
      WHERE v2.DefinitionId = v.DefinitionId)
ORDER BY d.UpdatedTime DESC, d.Id DESC;";
                command.Parameters.Add("@RuleType", DbType.Int32).Value = (int)ParseRuleType.Mapping;
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ParseRuleVersion version = ReadMappingVersion(reader);
                        items.Add(new MappingDefinitionListItem(version));
                    }
                }
            }

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
            _mappingSnapshot = null;
            _mappingSamplePathTextBox.Clear();
            _mappingSheetCombo.Items.Clear();
            _mappingExtensionLabel.Text = "扩展名：-";
            _mappingModelCombo.Enabled = true;
            if (_mappingModelCombo.Items.Count > 0 && _mappingModelCombo.SelectedIndex < 0)
                _mappingModelCombo.SelectedIndex = 0;
            MappingModelChoice model = GetSelectedMappingModel();
            _mappingRuleNameTextBox.Text = model == null ? string.Empty : model.ModelName + " Excel 映射";
            _mappingSuppressEvents = false;

            PopulateMappingRows(model == null ? 0 : model.Id, null);
            _mappingDirty = false;
            ClearMappingPreviewColumns();
            SetMappingStatus("新映射：请选择只读 Excel 样本并配置字段定位", Color.DimGray);
            UpdateMappingCommandState();
        }

        private void MappingDefinitionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_mappingSuppressEvents || listBoxMappingScripts.SelectedIndex < 0) return;

            int requestedIndex = listBoxMappingScripts.SelectedIndex;
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

                SelectMappingModel(definition.ModelId);
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
                _mappingSuppressEvents = false;

                _mappingDirty = false;
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

        private void PopulateMappingRows(int modelId, MappingRuleDefinition definition)
        {
            _mappingApplyingSuggestion = true;
            dataGridView1.Rows.Clear();
            try
            {
                List<ModelSchemaField> schemaFields = modelId > 0
                    ? LoadMappingFields(modelId)
                    : new List<ModelSchemaField>();
                var persisted = (definition == null ? Enumerable.Empty<FieldMappingRule>() : definition.Fields)
                    .Where(field => field != null && !string.IsNullOrWhiteSpace(field.TargetField))
                    .ToDictionary(field => field.TargetField, StringComparer.Ordinal);
                var fieldNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (ModelSchemaField field in schemaFields)
                {
                    FieldMappingRule rule;
                    persisted.TryGetValue(field.FieldName, out rule);
                    AddMappingRow(field, rule, false);
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
                    }, orphan, true);
                }
            }
            finally
            {
                _mappingApplyingSuggestion = false;
            }
        }

        private void AddMappingRow(ModelSchemaField field, FieldMappingRule rule, bool orphan)
        {
            int index = dataGridView1.Rows.Add();
            DataGridViewRow row = dataGridView1.Rows[index];
            row.Cells[MappingTargetFieldColumn].Value = field.FieldName;
            row.Cells[MappingTargetTypeColumn].Value = field.FieldType;
            row.Cells[MappingRequiredColumn].Value = field.IsRequired;
            row.Cells[MappingDescriptionColumn].Value = field.Description ?? string.Empty;
            row.Cells[MappingLocatorTypeColumn].Value = rule == null || rule.Locator == null
                ? "labelOffset"
                : rule.Locator.Type;
            row.Cells[MappingLocatorValueColumn].Value = rule == null || rule.Locator == null
                ? string.Empty
                : (rule.Locator.Type == "cell" ? rule.Locator.Cell : rule.Locator.Text);
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
                AnchorText = rule == null || rule.Locator == null ? null : rule.Locator.AnchorText
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
            PopulateMappingRows(model == null ? 0 : model.Id, null);
            if (model != null)
                _mappingRuleNameTextBox.Text = model.ModelName + " Excel 映射";
            MarkMappingDirty();
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
                MessageBox.Show("请先选择 Excel 样本和工作表。", "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (dataGridView1.CurrentRow == null)
            {
                MessageBox.Show("请先在字段映射网格中选中目标字段。", "点选定位", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            _mappingPickAnchorCell = null;
            _mappingPickLocatorButton.Text = "取消点选";
            _mappingSampleGrid.Focus();
            SetMappingStatus(
                locatorType == "cell"
                    ? "点选定位 1/2：先点锚点作为区域参照，再点实际值单元格"
                    : "点选定位 1/2：请在只读样本网格中点击标签/表头锚点",
                Color.RoyalBlue);
        }

        private void MappingSampleGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_mappingPickTargetRowIndex < 0 || e.RowIndex < 0) return;
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            if (sheet == null) return;

            string coordinate = Convert.ToString(
                _mappingSampleGrid.Rows[e.RowIndex].Cells["SampleCoordinate"].Value,
                CultureInfo.InvariantCulture);
            MappingCellSnapshot selectedCell = sheet.Cells.FirstOrDefault(
                cell => string.Equals(cell.Coordinate, coordinate, StringComparison.Ordinal));
            if (selectedCell == null) return;

            if (_mappingPickAnchorCell == null)
            {
                _mappingPickAnchorCell = selectedCell;
                _mappingPickAnchorSampleRowIndex = e.RowIndex;
                _mappingSampleGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
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
                    "已为 " + target + " 计算定位参数并标记为人工确认；请用验证预览核对",
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
                _mappingPickAnchorSampleRowIndex < _mappingSampleGrid.Rows.Count)
            {
                _mappingSampleGrid.Rows[_mappingPickAnchorSampleRowIndex]
                    .DefaultCellStyle.BackColor = Color.Empty;
            }
            bool wasActive = _mappingPickTargetRowIndex >= 0;
            _mappingPickTargetRowIndex = -1;
            _mappingPickAnchorSampleRowIndex = -1;
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

            DataGridViewRow row = dataGridView1.Rows[e.RowIndex];
            var metadata = row.Tag as MappingRowMetadata ?? new MappingRowMetadata();
            row.Tag = metadata;
            string columnName = dataGridView1.Columns[e.ColumnIndex].Name;
            _mappingApplyingSuggestion = true;
            try
            {
                if (columnName == MappingHumanConfirmedColumn)
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
            _mappingDirty = true;
            _mappingTemplateSignature = null;
            UpdateMappingVersionStatus();
            UpdateMappingCommandState();
        }

        private void MappingBrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择用于映射验证的 Excel 样本",
                Filter = "Excel 工作簿 (*.xls;*.xlsx)|*.xls;*.xlsx",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CancelMappingPointSelection(false);
                    MappingWorkbookSnapshot snapshot = _mappingPreviewService.Inspect(dialog.FileName);
                    if (_mappingCurrentDefinition != null &&
                        !string.Equals(
                            snapshot.FileExtension,
                            _mappingCurrentDefinition.NormalizedExtension,
                            StringComparison.Ordinal))
                    {
                        throw new MappingValidationException(
                            "样本扩展名与当前映射定义不一致；同一映射定义不能切换文件扩展名。");
                    }

                    string preferredSheet = _mappingCurrentDefinition == null
                        ? null
                        : _mappingCurrentDefinition.SheetName;
                    _mappingSnapshot = snapshot;
                    _mappingSamplePathTextBox.Text = dialog.FileName;
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
                        string.Format(
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
            _mappingSuppressEvents = true;
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
            _mappingSuppressEvents = false;
            PopulateMappingSampleGrid();
        }

        private void PopulateMappingSampleGrid()
        {
            _mappingSampleGrid.Rows.Clear();
            MappingSheetSnapshot sheet = GetSelectedMappingSheet();
            if (sheet == null) return;

            _mappingSampleGrid.SuspendLayout();
            try
            {
                foreach (MappingCellSnapshot cell in sheet.Cells)
                {
                    int rowIndex = _mappingSampleGrid.Rows.Add(
                        cell.Coordinate,
                        cell.DisplayText,
                        cell.ValueType,
                        cell.IsFormula);
                    if (cell.FormulaCacheMissing)
                    {
                        _mappingSampleGrid.Rows[rowIndex].Cells["SampleValue"].Style.ForeColor = Color.Firebrick;
                        _mappingSampleGrid.Rows[rowIndex].Cells["SampleValue"].ToolTipText = "公式缓存值缺失";
                    }
                }
            }
            finally
            {
                _mappingSampleGrid.ResumeLayout();
            }
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
                IReadOnlyList<AiMappingSuggestion> suggestions = _mappingLocalAssistant.Suggest(snapshot, targets);
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

        private void MappingSaveDraftButton_Click(object sender, EventArgs e)
        {
            try
            {
                MappingRuleDefinition definition = BuildMappingDefinition(false);
                if (MappingDefinitionMatchesCurrentVersion(definition) &&
                    _mappingCurrentVersion.Status == ParseRuleStatus.Draft)
                {
                    SetMappingStatus("当前草稿内容已经保存", Color.DarkGreen);
                    return;
                }

                SaveMappingDraft(definition);
                SetMappingStatus(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "草稿已保存：v{0}，修订 {1}",
                        _mappingCurrentVersion.VersionNumber,
                        _mappingCurrentVersion.Revision),
                    Color.DarkGreen);
            }
            catch (Exception ex)
            {
                ShowMappingError("保存草稿失败", ex);
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
            _mappingDirty = false;
            _mappingModelCombo.Enabled = false;
            RefreshMappingDefinitionList(saved.DefinitionId);
            UpdateMappingVersionStatus();
            UpdateMappingCommandState();
            return saved;
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
                throw new MappingValidationException("新映射必须先选择 Excel 样本以确定扩展名。");
            string sheetName = Convert.ToString(_mappingSheetCombo.SelectedItem, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new MappingValidationException("请选择工作表。");

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
                TemplateSignature = _mappingTemplateSignature
            };

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

        private FieldMappingRule CreateMappingFieldRule(DataGridViewRow row)
        {
            FieldMappingRule field = CreateFieldRuleMetadata(row);
            string locatorType = CellText(row, MappingLocatorTypeColumn);
            string locatorValue = CellText(row, MappingLocatorValueColumn);
            var locator = new MappingLocator
            {
                Type = locatorType,
                RowOffset = ParseMappingInteger(row, MappingRowOffsetColumn, "行偏移"),
                ColumnOffset = ParseMappingInteger(row, MappingColumnOffsetColumn, "列偏移"),
                ValueColumn = CellText(row, MappingValueColumnColumn),
                DataRowOffset = ParseMappingInteger(row, MappingDataRowOffsetColumn, "数据行偏移")
            };
            if (locatorType == "cell")
            {
                locator.Cell = locatorValue;
                var anchorMetadata = row.Tag as MappingRowMetadata;
                locator.AnchorCell = anchorMetadata == null ? null : anchorMetadata.AnchorCell;
                locator.AnchorText = anchorMetadata == null ? null : anchorMetadata.AnchorText;
            }
            else
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

        private static bool HasMappingLocator(DataGridViewRow row)
        {
            return !string.IsNullOrWhiteSpace(CellText(row, MappingLocatorTypeColumn)) &&
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

        private void MappingValidateButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mappingSnapshot == null || string.IsNullOrWhiteSpace(_mappingSamplePathTextBox.Text))
                    throw new MappingValidationException("验证预览必须选择只读 Excel 样本。");

                EnsureMappedRowsHumanConfirmed();
                MappingRuleDefinition definition = BuildMappingDefinition(true);
                MappingPreviewResult preview = _mappingPreviewService.Preview(
                    _mappingSamplePathTextBox.Text,
                    definition);
                DisplayMappingPreview(preview);
                if (!preview.IsValid)
                {
                    string errors = string.Join(", ", preview.ErrorCodes);
                    SetMappingStatus("验证未通过：" + errors, Color.Firebrick);
                    MessageBox.Show(
                        "验证预览未通过，草稿状态未推进。\r\n\r\n" + errors,
                        "验证预览",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                definition.TemplateSignature = preview.TemplateSignature;
                _mappingTemplateSignature = preview.TemplateSignature;
                if (!MappingDefinitionMatchesCurrentVersion(definition) ||
                    _mappingCurrentVersion.Status == ParseRuleStatus.Superseded)
                {
                    SaveMappingDraft(definition);
                }

                ScriptEngine.ValidateCompilation(
                    _mappingCurrentVersion.DerivedScriptCode,
                    _mappingCurrentVersion.ModelId);

                if (_mappingCurrentVersion.Status == ParseRuleStatus.Draft)
                {
                    string summary = BuildMappingValidationSummary(preview);
                    _mappingCurrentVersion = _mappingRuleStore.Validate(
                        _mappingCurrentVersion.Id,
                        _mappingCurrentVersion.Revision,
                        summary);
                    _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(
                        _mappingCurrentVersion.DefinitionJson);
                }

                _mappingDirty = false;
                RefreshMappingDefinitionList(_mappingCurrentVersion.DefinitionId);
                SetMappingStatus(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "验证通过：{0} 个字段，样本 {1}，模板 {2}",
                        preview.Fields.Count,
                        ShortHash(preview.SampleSha256),
                        ShortHash(preview.TemplateSignature)),
                    Color.DarkGreen);
                UpdateMappingCommandState();
                MessageBox.Show(
                    "验证预览通过。原值、转换结果和验证状态已分别显示在映射网格中。",
                    "验证预览",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowMappingError("验证预览失败", ex);
            }
        }

        private void DisplayMappingPreview(MappingPreviewResult preview)
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                ClearMappingPreviewRow(row);
                string target = CellText(row, MappingTargetFieldColumn);
                MappingPreviewFieldResult field;
                if (preview.Fields.TryGetValue(target, out field))
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

        private static string BuildMappingValidationSummary(MappingPreviewResult preview)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "sample={0};template={1};fields={2};warnings={3}",
                preview.SampleSha256,
                preview.TemplateSignature,
                preview.Fields.Count,
                string.Join(",", preview.WarningCodes));
        }

        private void MappingPublishButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mappingCurrentVersion == null ||
                    (_mappingCurrentVersion.Status != ParseRuleStatus.Validated &&
                     _mappingCurrentVersion.Status != ParseRuleStatus.Published))
                    throw new ParseRuleStateException("请先保存草稿并完成验证预览。");
                if (_mappingDirty)
                    throw new ParseRuleStateException("当前界面有未保存修改；请重新保存并验证后再发布。");
                if (_mappingCurrentDefinition == null ||
                    _mappingCurrentDefinition.Fields.Any(
                        field => field.ConfirmationState != MappingConfirmationState.HumanConfirmed))
                    throw new ParseRuleStateException("发布前必须逐项人工确认所有已映射字段。");

                MappingModelChoice model = GetSelectedMappingModel();
                if (model == null)
                    throw new MappingValidationException("当前模型不存在。");
                string currentSchemaHash = ModelSchemaService.ComputeHash(LoadMappingFields(model.Id));
                if (!string.Equals(currentSchemaHash, _mappingCurrentVersion.ModelSchemaHash, StringComparison.Ordinal))
                    throw new ParseRuleStateException("模型结构已变化；必须基于新结构重新保存并验证草稿。");

                List<MappingMachineChoice> selectedMachines = ShowMappingMachineSelection();
                if (selectedMachines == null || selectedMachines.Count == 0) return;

                var publishedByMachine = new Dictionary<int, ParseRuleVersion>();
                var conflicts = new List<string>();
                foreach (MappingMachineChoice machine in selectedMachines)
                {
                    ParseRuleVersion existing = _mappingRuleStore.GetPublished(
                        machine.Id.ToString(CultureInfo.InvariantCulture),
                        _mappingCurrentVersion.NormalizedExtension);
                    publishedByMachine[machine.Id] = existing;
                    if (existing != null && existing.Id != _mappingCurrentVersion.Id)
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
                        "以下机台在相同扩展名上已有发布规则：\r\n\r\n" +
                        string.Join("\r\n", conflicts) +
                        "\r\n\r\n是否确认覆盖这些绑定？",
                        "发布冲突确认",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (confirmation != DialogResult.Yes) return;
                }

                ParseRuleVersion current = _mappingRuleStore.Publish(
                    _mappingCurrentVersion.Id,
                    selectedMachines
                        .Select(machine => machine.Id.ToString(CultureInfo.InvariantCulture))
                        .ToArray(),
                    _mappingCurrentVersion.Revision,
                    conflicts.Count > 0,
                    publishedByMachine.ToDictionary(
                        item => item.Key.ToString(CultureInfo.InvariantCulture),
                        item => item.Value == null ? (long?)null : item.Value.Id,
                        StringComparer.Ordinal));

                foreach (long replacedVersionId in publishedByMachine.Values
                    .Where(version => version != null && version.Id != current.Id)
                    .Select(version => version.Id)
                    .Distinct())
                {
                    ScriptEngine.ClearCache(replacedVersionId);
                }
                ScriptEngine.ClearCache(current.Id);

                _mappingCurrentVersion = current;
                _mappingCurrentDefinition = MappingRuleSerializer.Deserialize(current.DefinitionJson);
                RefreshMappingDefinitionList(current.DefinitionId);
                SetMappingStatus(
                    string.Format(CultureInfo.InvariantCulture, "已发布到 {0} 台机台；版本 v{1}", selectedMachines.Count, current.VersionNumber),
                    Color.DarkGreen);
                UpdateMappingCommandState();
                MessageBox.Show(
                    string.Format(CultureInfo.InvariantCulture, "发布完成，共更新 {0} 台机台。", selectedMachines.Count),
                    "发布完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowMappingError("发布失败", ex);
            }
        }

        private List<MappingMachineChoice> ShowMappingMachineSelection()
        {
            List<MappingMachineChoice> machines = LoadMappingMachines();
            if (machines.Count == 0)
            {
                MessageBox.Show("没有启用的机台可供发布。", "发布到机台", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            using (var dialog = new Form
            {
                Text = "选择发布机台",
                StartPosition = FormStartPosition.CenterParent,
                Width = 470,
                Height = 480,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false
            })
            {
                var list = new CheckedListBox
                {
                    Dock = DockStyle.Fill,
                    CheckOnClick = true,
                    IntegralHeight = false
                };
                foreach (MappingMachineChoice machine in machines)
                    list.Items.Add(machine, false);

                var message = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 48,
                    Padding = new Padding(8),
                    Text = "选择一个或多个机台。相同扩展名的现有规则会在下一步单独提示冲突。"
                };
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 48,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(6)
                };
                var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 90 };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                dialog.Controls.Add(list);
                dialog.Controls.Add(message);
                dialog.Controls.Add(buttons);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                return list.CheckedItems.Cast<MappingMachineChoice>().ToList();
            }
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
                _mappingDirty = false;
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

        private void EnsureMappedRowsHumanConfirmed()
        {
            List<string> pending = dataGridView1.Rows
                .Cast<DataGridViewRow>()
                .Where(row => HasMappingLocator(row))
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
            if (_mappingSaveDraftButton == null) return;
            bool ready = _mappingDataLoaded && _mappingRuleStore != null;
            bool hasModel = GetSelectedMappingModel() != null;
            bool hasSample = _mappingSnapshot != null && GetSelectedMappingSheet() != null;
            bool hasDefinition = _mappingCurrentVersion != null;
            bool publishable = hasDefinition && !_mappingDirty &&
                (_mappingCurrentVersion.Status == ParseRuleStatus.Validated ||
                 _mappingCurrentVersion.Status == ParseRuleStatus.Published);

            _mappingBrowseButton.Enabled = ready && !_mappingAiBusy;
            _mappingModelCombo.Enabled = ready && !_mappingAiBusy && _mappingCurrentDefinition == null;
            _mappingRuleNameTextBox.Enabled = ready && !_mappingAiBusy;
            _mappingSheetCombo.Enabled = ready && !_mappingAiBusy && hasSample;
            dataGridView1.Enabled = ready && !_mappingAiBusy;
            btnNewMappingScript.Enabled = ready && !_mappingAiBusy;
            listBoxMappingScripts.Enabled = ready && !_mappingAiBusy;
            _mappingLocalAssistButton.Enabled = ready && hasSample && !_mappingAiBusy &&
                GetMappingSuggestionEligibleRows().Count > 0;
            _mappingPickLocatorButton.Enabled = ready && hasSample && !_mappingAiBusy &&
                dataGridView1.CurrentRow != null;
            _mappingAiAssistButton.Enabled = ready && hasSample && !_mappingAiBusy &&
                _mappingAiOptions != null && GetMappingSuggestionEligibleRows().Count > 0;
            _mappingAiAssistButton.Text = _mappingAiOptions == null ? "AI 未配置" : "AI 自动填映射";
            _mappingToolTip.SetToolTip(
                _mappingAiAssistButton,
                _mappingAiOptions == null
                    ? (_mappingAiUnavailableReason ?? "AI 未配置")
                    : "仅填充空白且未确认项；返回失败时不修改草稿");
            _mappingSaveDraftButton.Enabled = ready && hasModel && !_mappingAiBusy;
            _mappingValidateButton.Enabled = ready && hasModel && hasSample && !_mappingAiBusy;
            _mappingPublishButton.Enabled = ready && publishable && !_mappingAiBusy;
            _mappingHistoryButton.Enabled = ready && hasDefinition && !_mappingAiBusy;
        }

        private void SetMappingEditorEnabled(bool enabled)
        {
            btnNewMappingScript.Enabled = enabled;
            listBoxMappingScripts.Enabled = enabled;
            panel2.Enabled = enabled;
            panel3.Enabled = enabled;
            dataGridView1.Enabled = enabled;
            _mappingSampleGrid.Enabled = enabled;
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
            public string AnchorCell { get; set; }
            public string AnchorText { get; set; }
        }
    }
}

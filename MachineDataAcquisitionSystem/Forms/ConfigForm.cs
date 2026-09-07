using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class ConfigForm : Form
    {
        // ========== 路径配置 ==========
        private List<MachineConfig> _machineConfigs;
        private AppSettings _appSettings;
        private DataGridView _dgvPath;

        // ========== 数据库配置 ==========
        private List<DatabaseConfig> _databases;
        private DatabaseConfig _currentDatabase;
        private PropertyGrid _aiMappingPropertyGrid;
        private Button _aiMappingSaveButton;
        private Button _aiMappingTestButton;
        private Label _aiMappingStatusLabel;
        private CheckBox _autoStartCheckBox;
        private CheckedListBox _machineAutoStartList;
        private Button _basicSettingsSaveButton;
        private Label _basicSettingsStatusLabel;
        private readonly StartupRegistrationService _startupRegistrationService = new StartupRegistrationService();
        private bool _configurationLoaded;
        private bool _configurationHasUnsavedEdits;
        private bool _basicConfigurationHasUnsavedEdits;
        private CancellationTokenSource _databaseConnectionTestCancellation;
        private bool _reloadingConfiguration;

        public ConfigForm()
        {
            InitializeComponent();
            InitializeBasicSettingsEditor();
            InitializeAiMappingEditor();

            this.Load += ConfigForm_Load;
            this.Activated += ConfigForm_Activated;
            btnCancel.Click += (s, e) => this.Close();
            btnSave.Click += BtnSave_Click;

            // 数据库配置事件绑定
            //if (listViewDb != null)
            //{
            //    listViewDb.SelectedIndexChanged += ListViewDb_SelectedIndexChanged;
            //}
            if (btnAddDb != null)
            {
                btnAddDb.Click += BtnAddDb_Click;
            }
            if (btnSaveDb != null)
            {
                btnSaveDb.Click += BtnSaveDb_Click;
            }
            if (propertyGridDb != null)
            {
                propertyGridDb.PropertyValueChanged += PropertyGridDb_PropertyValueChanged;
            }

            // 绑定右键菜单事件
            menuDelete.Click += menuDelete_Click;
            menuSetPrimary.Click += MenuSetPrimary_Click;
            menuTestConn.Click += btnTestConn_Click;
            FormClosed += (sender, args) => _databaseConnectionTestCancellation?.Cancel();
        }

        private void InitializeBasicSettingsEditor()
        {
            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("微软雅黑", 14F, FontStyle.Bold),
                Text = "程序启动"
            };
            _autoStartCheckBox = new CheckBox
            {
                AutoSize = true,
                Margin = new Padding(3, 18, 3, 3),
                Text = "Windows 登录后自动启动本程序"
            };
            var descriptionLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(28, 8, 3, 3),
                Text = "仅对当前 Windows 用户生效，不需要管理员权限。"
            };
            var machineStartLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(3, 20, 3, 8),
                Text = "程序启动后默认采集的机台（分别勾选）"
            };
            _machineAutoStartList = new CheckedListBox
            {
                Name = "machineAutoStartList",
                Width = 520,
                Height = 150,
                IntegralHeight = false,
                CheckOnClick = true,
                FormattingEnabled = true,
                HorizontalScrollbar = true
            };
            _machineAutoStartList.Format += (sender, args) =>
            {
                if (args.ListItem is MachineConfig machine)
                    args.Value = $"{machine.Name}（ID: {machine.Id}）";
            };
            _machineAutoStartList.ItemCheck += AutoStartCheckBox_CheckedChanged;
            var machineStartDescription = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(650, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 8, 3, 3),
                Text = "保存后下次启动程序时生效，手动打开程序也适用；不改变当前采集状态。\r\n" +
                       "如需 Windows 登录后自动采集，还需勾选上方的程序自启选项。"
            };
            _basicSettingsSaveButton = new Button
            {
                AutoSize = true,
                Height = 38,
                Margin = new Padding(3, 24, 3, 3),
                Text = "保存基础配置"
            };
            _basicSettingsStatusLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 12, 3, 3),
                Text = "关闭主界面后程序将继续在系统托盘运行。"
            };

            var layout = new FlowLayoutPanel
            {
                AutoScroll = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(20),
                WrapContents = false
            };
            layout.Controls.Add(titleLabel);
            layout.Controls.Add(_autoStartCheckBox);
            layout.Controls.Add(descriptionLabel);
            layout.Controls.Add(machineStartLabel);
            layout.Controls.Add(_machineAutoStartList);
            layout.Controls.Add(machineStartDescription);
            layout.Controls.Add(_basicSettingsSaveButton);
            layout.Controls.Add(_basicSettingsStatusLabel);
            tabPageBasicSettings.Controls.Add(layout);
            tabControl1.SelectedTab = tabPageBasicSettings;

            _autoStartCheckBox.CheckedChanged += AutoStartCheckBox_CheckedChanged;
            _basicSettingsSaveButton.Click += BasicSettingsSaveButton_Click;
        }

        private void InitializeAiMappingEditor()
        {
            _aiMappingPropertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                HelpVisible = true,
                ToolbarVisible = false,
                PropertySort = PropertySort.Categorized
            };
            _aiMappingPropertyGrid.PropertyValueChanged += AiMappingPropertyGrid_PropertyValueChanged;

            _aiMappingSaveButton = new Button
            {
                AutoSize = true,
                Height = 34,
                Text = "保存 AI 配置"
            };
            _aiMappingTestButton = new Button
            {
                AutoSize = true,
                Height = 34,
                Text = "测试 AI 连接"
            };
            _aiMappingStatusLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(12, 9, 0, 0),
                ForeColor = Color.DimGray,
                Text = "修改配置后请先保存或测试连接"
            };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4)
            };
            actions.Controls.Add(_aiMappingSaveButton);
            actions.Controls.Add(_aiMappingTestButton);
            actions.Controls.Add(_aiMappingStatusLabel);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.Controls.Add(_aiMappingPropertyGrid, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            tabPage5.Controls.Add(layout);

            _aiMappingSaveButton.Click += AiMappingSaveButton_Click;
            _aiMappingTestButton.Click += AiMappingTestButton_Click;
        }

        // ========== 路径配置方法 ==========

        private DataGridView GetDataGridViewFromTabPage(TabPage page)
        {
            foreach (Control ctrl in page.Controls)
            {
                if (ctrl is DataGridView)
                    return ctrl as DataGridView;
            }
            return null;
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            // 获取 DataGridView
            _dgvPath = GetDataGridViewFromTabPage(tabPage1);

            // 设置列自动填充
            if (_dgvPath != null)
            {
                _dgvPath.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            }

            ReloadConfigurationFromStorage();
            if (!string.IsNullOrWhiteSpace(SettingsHelper.LastLoadError))
            {
                MessageBox.Show(
                    SettingsHelper.LastLoadError,
                    "配置文件读取失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            _configurationLoaded = true;
            _configurationHasUnsavedEdits = false;
            if (_dgvPath != null)
                _dgvPath.CellValueChanged += PathGrid_CellValueChanged;
        }

        private void ConfigForm_Activated(object sender, EventArgs e)
        {
            if (!_configurationLoaded || _configurationHasUnsavedEdits || _basicConfigurationHasUnsavedEdits) return;

            ReloadConfigurationFromStorage();
        }

        private void ReloadConfigurationFromStorage()
        {
            string selectedDatabaseName = _currentDatabase == null ? null : _currentDatabase.Name;
            _reloadingConfiguration = true;
            try
            {
                _machineConfigs = MachineConfig.Load();
                if (_machineConfigs == null || _machineConfigs.Count == 0)
                    _machineConfigs = SettingsHelper.GenerateDefaultMachineConfigs();

                _appSettings = SettingsHelper.LoadSettings();
                LoadDataToGrid();
                LoadDatabases();
                if (!string.IsNullOrWhiteSpace(selectedDatabaseName))
                {
                    DatabaseConfig selectedDatabase = _databases.FirstOrDefault(
                        database => string.Equals(database.Name, selectedDatabaseName, StringComparison.Ordinal));
                    if (selectedDatabase != null)
                        SelectDatabase(selectedDatabase);
                }
                LoadAiMappingSettings();
                LoadBasicSettings();
            }
            finally
            {
                _reloadingConfiguration = false;
            }
        }

        private void LoadBasicSettings()
        {
            bool wasReloading = _reloadingConfiguration;
            _reloadingConfiguration = true;
            _machineAutoStartList.BeginUpdate();
            try
            {
                _autoStartCheckBox.Checked = _appSettings.AutoStart;
                var selectedIds = new HashSet<int>(_appSettings.AutoStartMachineIds ?? new List<int>());
                _machineAutoStartList.Items.Clear();
                foreach (MachineConfig machine in (_machineConfigs ?? new List<MachineConfig>())
                    .Where(machine => machine != null && machine.Id >= 1 && machine.Id <= 6)
                    .GroupBy(machine => machine.Id).Select(group => group.First()).OrderBy(machine => machine.Id))
                    _machineAutoStartList.Items.Add(machine, selectedIds.Contains(machine.Id));
            }
            finally
            {
                _machineAutoStartList.EndUpdate();
                _reloadingConfiguration = wasReloading;
            }
            _basicConfigurationHasUnsavedEdits = false;
            UpdateBasicSettingsStatus("关闭主界面后程序将继续在系统托盘运行。", Color.DimGray);
        }

        private void AutoStartCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_reloadingConfiguration) return;

            _basicConfigurationHasUnsavedEdits = true;
            UpdateBasicSettingsStatus("基础配置有未保存修改。", Color.DarkOrange);
        }

        private void BasicSettingsSaveButton_Click(object sender, EventArgs e)
        {
            bool previousAutoStart = _appSettings.AutoStart;
            bool requestedAutoStart = _autoStartCheckBox.Checked;
            List<int> requestedMachineIds = _machineAutoStartList.CheckedItems.Cast<MachineConfig>()
                .Select(machine => machine.Id).ToList();
            _basicSettingsSaveButton.Enabled = false;
            try
            {
                _startupRegistrationService.SetEnabled(requestedAutoStart, Application.ExecutablePath);
                try
                {
                    SettingsHelper.SaveBasicConfiguration(requestedAutoStart, requestedMachineIds);
                }
                catch (Exception saveException)
                {
                    try
                    {
                        _startupRegistrationService.SetEnabled(previousAutoStart, Application.ExecutablePath);
                    }
                    catch (Exception rollbackException)
                    {
                        throw new InvalidOperationException(
                            "配置文件保存失败，且开机启动状态未能恢复：" +
                            saveException.Message + "；回滚失败：" + rollbackException.Message,
                            saveException);
                    }

                    throw new InvalidOperationException(
                        "配置文件保存失败，开机启动状态已恢复：" + saveException.Message,
                        saveException);
                }

                _appSettings.AutoStart = requestedAutoStart;
                _appSettings.AutoStartMachineIds = requestedMachineIds;
                _basicConfigurationHasUnsavedEdits = false;
                UpdateBasicSettingsStatus("基础配置保存成功。", Color.DarkGreen);
                MessageBox.Show(this, "基础配置已保存。机台默认启动设置将在下次程序启动时生效，当前采集状态不变。",
                    "基础配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LoadBasicSettings();
                UpdateBasicSettingsStatus("基础配置保存失败。", Color.Firebrick);
                MessageBox.Show(this, ex.Message, "基础配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _basicSettingsSaveButton.Enabled = true;
            }
        }

        private void UpdateBasicSettingsStatus(string message, Color color)
        {
            _basicSettingsStatusLabel.Text = message;
            _basicSettingsStatusLabel.ForeColor = color;
        }

        private void PathGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (!_reloadingConfiguration && e.RowIndex >= 0)
                _configurationHasUnsavedEdits = true;
        }

        private void LoadAiMappingSettings()
        {
            if (_appSettings.AiMapping == null)
                _appSettings.AiMapping = new AiMappingConfig();
            _aiMappingPropertyGrid.SelectedObject = _appSettings.AiMapping;
        }

        private void AiMappingPropertyGrid_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            if (!_reloadingConfiguration)
            {
                _configurationHasUnsavedEdits = true;
                UpdateAiMappingStatus("AI 配置有未保存修改", Color.DarkOrange);
            }
        }

        private void AiMappingSaveButton_Click(object sender, EventArgs e)
        {
            try
            {
                AiMappingConfig config = GetCurrentAiMappingConfig();
                OpenAiCompatibleMappingClient.ValidateOptions(config.ToClientOptions());
                SettingsHelper.SaveAiMappingConfiguration(config);
                UpdateAiMappingStatus("AI 配置保存成功", Color.DarkGreen);
                MessageBox.Show(this, "AI 自动映射配置已保存。", "AI 配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateAiMappingStatus("AI 配置保存失败", Color.Firebrick);
                MessageBox.Show(this, ex.Message, "AI 配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void AiMappingTestButton_Click(object sender, EventArgs e)
        {
            SetAiMappingActionsEnabled(false);
            UpdateAiMappingStatus("正在测试 AI 连接...", Color.RoyalBlue);
            try
            {
                AiMappingConfig config = GetCurrentAiMappingConfig();
                AiConnectionTestResult result;
                using (var tester = new AiMappingConnectionTester())
                    result = await tester.TestAsync(config.ToClientOptions(), CancellationToken.None);

                if (result.IsSuccess)
                {
                    UpdateAiMappingStatus("AI 连接成功", Color.DarkGreen);
                    MessageBox.Show(this, "AI Endpoint 和模型连接成功。", "AI 连接测试", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    UpdateAiMappingStatus("AI 连接失败：" + result.ErrorCode, Color.Firebrick);
                    MessageBox.Show(this, "AI 连接失败：" + result.ErrorCode, "AI 连接测试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                UpdateAiMappingStatus("AI 连接测试失败", Color.Firebrick);
                MessageBox.Show(this, ex.Message, "AI 连接测试", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                    SetAiMappingActionsEnabled(true);
            }
        }

        private AiMappingConfig GetCurrentAiMappingConfig()
        {
            if (_appSettings == null || _appSettings.AiMapping == null)
                throw new InvalidOperationException("AI 配置尚未加载。");
            return _appSettings.AiMapping;
        }

        private void SetAiMappingActionsEnabled(bool enabled)
        {
            _aiMappingSaveButton.Enabled = enabled;
            _aiMappingTestButton.Enabled = enabled;
            _aiMappingPropertyGrid.Enabled = enabled;
        }

        private void UpdateAiMappingStatus(string message, Color color)
        {
            _aiMappingStatusLabel.Text = message;
            _aiMappingStatusLabel.ForeColor = color;
        }


        private void menuDelete_Click(object sender, EventArgs e)
        {
            if (listViewDb.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要删除的数据库", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DatabaseConfig db = listViewDb.SelectedItems[0].Tag as DatabaseConfig;
            if (db == null) return;

            DialogResult result = MessageBox.Show(
                $"确定要删除数据库 \"{db.Name}\" 吗？",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _databases.Remove(db);
                _configurationHasUnsavedEdits = true;
                RefreshDatabaseList();

                if (_databases.Count > 0)
                {
                    SelectDatabase(_databases[0]);
                }
                else
                {
                    ClearDatabaseForm();
                }

                UpdateDbStatus($"已删除数据库 \"{db.Name}\"", Color.Blue);
            }
        }

        private void MenuSetPrimary_Click(object sender, EventArgs e)
        {
            if (listViewDb.SelectedItems.Count == 0)
            {
                UpdateDbStatus("请先选择数据库", Color.Firebrick);
                return;
            }

            DatabaseConfig selectedDatabase = listViewDb.SelectedItems[0].Tag as DatabaseConfig;
            if (selectedDatabase == null) return;

            var previousValues = _databases.ToDictionary(database => database, database => database.IsPrimary);
            try
            {
                DatabasePrimarySelectionService.SetPrimary(_databases, selectedDatabase);
                SettingsHelper.SaveDatabaseConfigurations(_databases);
                _appSettings.Databases = _databases;
                _currentDatabase = selectedDatabase;
                _configurationHasUnsavedEdits = false;
                RefreshDatabaseList();
                SelectDatabase(selectedDatabase);
                propertyGridDb.Refresh();
                UpdateDbStatus($"已将 {selectedDatabase.Name} 设为主数据库", Color.Green);
            }
            catch (Exception ex)
            {
                foreach (KeyValuePair<DatabaseConfig, bool> previousValue in previousValues)
                    previousValue.Key.IsPrimary = previousValue.Value;
                RefreshDatabaseList();
                UpdateDbStatus("设置主数据库失败", Color.Firebrick);
                MessageBox.Show("设置主数据库失败：" + ex.Message, "数据库配置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDataToGrid()
        {
            if (_dgvPath == null) return;

            _dgvPath.Rows.Clear();

            foreach (var config in _machineConfigs)
            {
                int rowIndex = _dgvPath.Rows.Add();
                _dgvPath.Rows[rowIndex].Cells["Id"].Value = config.Id;
                _dgvPath.Rows[rowIndex].Cells["Names"].Value = config.Name;
                _dgvPath.Rows[rowIndex].Cells["MonitorPath"].Value = config.MonitorPath;
                _dgvPath.Rows[rowIndex].Cells["SuccessPath"].Value = config.SuccessPath;
                _dgvPath.Rows[rowIndex].Cells["ErrorPath"].Value = config.ErrorPath;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_dgvPath != null && !_dgvPath.EndEdit())
            {
                MessageBox.Show("请先完成当前路径单元格的编辑。", "路径配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 保存路径配置
            if (_dgvPath != null)
            {
                for (int i = 0; i < _machineConfigs.Count && i < _dgvPath.Rows.Count; i++)
                {
                    _machineConfigs[i].MonitorPath = _dgvPath.Rows[i].Cells["MonitorPath"].Value?.ToString();
                    _machineConfigs[i].SuccessPath = _dgvPath.Rows[i].Cells["SuccessPath"].Value?.ToString();
                    _machineConfigs[i].ErrorPath = _dgvPath.Rows[i].Cells["ErrorPath"].Value?.ToString();
                }
            }

            // 保存数据库配置到 _appSettings
            try
            {
                _appSettings.Databases = _databases;
                MachineConfig.Save(_machineConfigs);
                SettingsHelper.SaveSettingsOrThrow(_appSettings);
                _configurationHasUnsavedEdits = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("配置保存失败：" + ex.Message, "配置保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }


            // 询问是否重启
            DialogResult result = MessageBox.Show(
                "配置保存成功！是否立即重启程序？",
                "提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(Application.ExecutablePath);
                System.Environment.Exit(0);
            }
            else
            {
                this.Close();
            }
        }

        // ========== 数据库配置方法 ==========

        private void LoadDatabases()
        {
            // 从 _appSettings 获取数据库列表
            _databases = _appSettings.Databases;

            if (_databases == null)
            {
                _databases = new List<DatabaseConfig>();
                _appSettings.Databases = _databases;
            }

            RefreshDatabaseList();

            if (_databases.Count > 0)
            {
                SelectDatabase(_databases[0]);
            }
        }

        private void RefreshDatabaseList()
        {
            listViewDb.Items.Clear();

            foreach (var db in _databases)
            {
                string connIcon = db.LastTestResult ? "🟢" : "⚪";
                string status = db.IsPrimary ? "主库 " + connIcon : connIcon;
                string info = $"{db.DbType} | {db.Server}";

                ListViewItem item = new ListViewItem(db.Name);
                item.SubItems.Add(status);
                item.SubItems.Add(info);
                item.Tag = db;

                listViewDb.Items.Add(item);
            }

            lblDbCount.Text = $"共 {_databases.Count} 个数据库";
        }

        private void SelectDatabase(DatabaseConfig db)
        {
            _currentDatabase = db;
            if (propertyGridDb != null)
            {
                propertyGridDb.SelectedObject = db;
            }
        }

        private void ClearDatabaseForm()
        {
            _currentDatabase = null;
            if (propertyGridDb != null)
            {
                propertyGridDb.SelectedObject = null;
            }
        }

        private void ListViewDb_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewDb.SelectedItems.Count > 0)
            {
                DatabaseConfig db = listViewDb.SelectedItems[0].Tag as DatabaseConfig;
                if (db != null)
                {
                    SelectDatabase(db);
                }
            }
        }

        private void BtnAddDb_Click(object sender, EventArgs e)
        {
            DatabaseConfig newDb = new DatabaseConfig
            {
                Name = $"数据库{_databases.Count + 1}"
            };
            _databases.Add(newDb);
            _configurationHasUnsavedEdits = true;
            RefreshDatabaseList();
            SelectDatabase(newDb);
        }

        private void BtnSaveDb_Click(object sender, EventArgs e)
        {
            if (_currentDatabase != null)
            {
                try
                {
                    // 自动生成连接字符串
                    if (string.IsNullOrEmpty(_currentDatabase.ConnectionString))
                    {
                        _currentDatabase.ConnectionString = GenerateConnectionString(_currentDatabase);
                    }

                    if (!DatabaseConnectionTester.TryValidateConnectionString(
                        _currentDatabase.DbType,
                        _currentDatabase.ConnectionString,
                        out string validationError))
                    {
                        UpdateDbStatus("配置不完整", Color.Firebrick);
                        MessageBox.Show(validationError, "数据库配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    SettingsHelper.SaveDatabaseConfigurations(_databases);
                    _appSettings.Databases = _databases;
                    _configurationHasUnsavedEdits = false;

                    // 刷新左边列表
                    RefreshDatabaseList();
                    UpdateDbStatus("保存成功！", Color.Green);
                }
                catch (Exception ex)
                {
                    UpdateDbStatus("保存失败", Color.Firebrick);
                    MessageBox.Show("数据库配置保存失败：" + ex.Message, "数据库配置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                UpdateDbStatus("请先选择数据库", Color.Firebrick);
            }
        }



        private async void btnTestConn_Click(object sender, EventArgs e)
        {
            if (_currentDatabase == null)
            {
                UpdateDbStatus("请先选择一个数据库", Color.Red);
                return;
            }

            UpdateDbStatus("正在测试连接...", Color.Blue);
            menuTestConn.Enabled = false;
            _databaseConnectionTestCancellation?.Cancel();
            _databaseConnectionTestCancellation?.Dispose();
            var testCancellation = new CancellationTokenSource();
            _databaseConnectionTestCancellation = testCancellation;
            try
            {
                string connStr = _currentDatabase.GetConnectionString();
                DatabaseConnectionTestResult result = await DatabaseConnectionTester.TryOpenAsync(
                    _currentDatabase.DbType,
                    connStr,
                    testCancellation.Token);

                _currentDatabase.LastTestTime = DateTime.Now;
                _currentDatabase.LastTestResult = result.IsSuccess;
                RefreshDatabaseList();

                if (result.IsSuccess)
                {
                    UpdateDbStatus("连接成功！", Color.Green);
                }
                else
                {
                    UpdateDbStatus(result.TimedOut ? "连接超时" : "连接失败", Color.Red);
                    MessageBox.Show(
                        result.Error ?? "连接失败，请检查配置。",
                        "数据库连接测试",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing)
                    UpdateDbStatus("连接测试已取消", Color.DarkOrange);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                    menuTestConn.Enabled = true;
                if (ReferenceEquals(_databaseConnectionTestCancellation, testCancellation))
                {
                    _databaseConnectionTestCancellation = null;
                    testCancellation.Dispose();
                }
            }
        }

        private string GenerateConnectionString(DatabaseConfig db)
        {
            return db == null ? "" : db.GetConnectionString();
        }

        private void PropertyGridDb_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            string propertyName = e.ChangedItem.PropertyDescriptor.Name;
            // 只有真实连接参数发生变化时，才丢弃旧的自动/自定义连接字符串。
            if (_currentDatabase != null &&
                DatabaseConfigurationRules.ConnectionStringShouldBeRegenerated(propertyName))
            {
                _currentDatabase.ConnectionString = "";
                DatabaseConfigurationRules.InvalidateConnectionTest(_currentDatabase);
                RefreshDatabaseList();
            }
            if (_currentDatabase != null &&
                propertyName == "IsPrimary" &&
                _currentDatabase.IsPrimary)
            {
                DatabasePrimarySelectionService.SetPrimary(_databases, _currentDatabase);
                RefreshDatabaseList();
                propertyGridDb.Refresh();
            }
            if (!_reloadingConfiguration)
                _configurationHasUnsavedEdits = true;
        }

        private void UpdateDbStatus(string message, Color color)
        {
            if (lblDbStatus != null)
            {
                lblDbStatus.Text = message;
                lblDbStatus.ForeColor = color;
            }
        }
    }
}

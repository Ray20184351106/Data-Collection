using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
using MachineDataAcquisitionSystem.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private bool _configurationLoaded;
        private bool _configurationHasUnsavedEdits;
        private bool _reloadingConfiguration;

        public ConfigForm()
        {
            InitializeComponent();

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
            _dgvPath = GetDataGridViewFromTabPage(tabControl1.TabPages[0]);

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
            if (!_configurationLoaded || _configurationHasUnsavedEdits) return;

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
            }
            finally
            {
                _reloadingConfiguration = false;
            }
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
            if (_aiMappingPropertyGrid == null)
            {
                _aiMappingPropertyGrid = new PropertyGrid
                {
                    Dock = DockStyle.Fill,
                    HelpVisible = true,
                    ToolbarVisible = false,
                    PropertySort = PropertySort.Categorized
                };
                tabPage5.Controls.Add(_aiMappingPropertyGrid);
            }
            _aiMappingPropertyGrid.SelectedObject = _appSettings.AiMapping;
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



        private void btnTestConn_Click(object sender, EventArgs e)
        {
            if (_currentDatabase == null)
            {
                UpdateDbStatus("请先选择一个数据库", Color.Red);
                return;
            }

            UpdateDbStatus("正在测试连接...", Color.Blue);

            string connStr = _currentDatabase.GetConnectionString();
            bool success = DatabaseConnectionTester.TryOpen(
                _currentDatabase.DbType,
                connStr,
                out string error);

            _currentDatabase.LastTestTime = DateTime.Now;
            _currentDatabase.LastTestResult = success;
            RefreshDatabaseList();

            if (success)
            {
                UpdateDbStatus("连接成功！", Color.Green);
            }
            else
            {
                UpdateDbStatus("连接失败", Color.Red);
                MessageBox.Show(error ?? "连接失败，请检查配置。", "数据库连接测试", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
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

        public ConfigForm()
        {
            InitializeComponent();

            this.Load += ConfigForm_Load;
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
            if (btnTestConn != null)
            {
                btnTestConn.Click += btnTestConn_Click;
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

            // 加载配置
            _machineConfigs = MachineConfig.Load();
            if (_machineConfigs == null || _machineConfigs.Count == 0)
            {
                _machineConfigs = SettingsHelper.GenerateDefaultMachineConfigs();
            }

            _appSettings = SettingsHelper.LoadSettings();

            // 绑定数据
            LoadDataToGrid();

            // ========== 加载数据库配置 ==========
            LoadDatabases();
            LoadAiMappingSettings();
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
            _appSettings.Databases = _databases;

            // 保存到文件
            MachineConfig.Save(_machineConfigs);
            SettingsHelper.SaveSettings(_appSettings);


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
                string statusIcon = db.IsPrimary ? "●" : "○";
                string connIcon = db.LastTestResult ? "🟢" : "⚪";
                string info = $"{db.DbType} | {db.Server}";

                ListViewItem item = new ListViewItem(db.Name);
                item.SubItems.Add(connIcon);
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
            RefreshDatabaseList();
            SelectDatabase(newDb);
        }

        private void BtnSaveDb_Click(object sender, EventArgs e)
        {
            if (_currentDatabase != null)
            {
                // 自动生成连接字符串
                if (string.IsNullOrEmpty(_currentDatabase.ConnectionString))
                {
                    _currentDatabase.ConnectionString = GenerateConnectionString(_currentDatabase);
                }

                // 刷新左边列表
                RefreshDatabaseList();

                // 更新状态
                UpdateDbStatus("保存成功！", Color.Green);
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

            string connStr = string.IsNullOrEmpty(_currentDatabase.ConnectionString)
                ? GenerateConnectionString(_currentDatabase)
                : _currentDatabase.ConnectionString;

            bool success = TestConnection(_currentDatabase.DbType, connStr);

            _currentDatabase.LastTestTime = DateTime.Now;
            _currentDatabase.LastTestResult = success;

            if (success)
            {
                UpdateDbStatus("连接成功！", Color.Green);
                RefreshDatabaseList();
            }
            else
            {
                UpdateDbStatus("连接失败，请检查配置", Color.Red);
            }
        }

        private string GenerateConnectionString(DatabaseConfig db)
        {
            switch (db.DbType)
            {
                case "SQLite":
                    return $"Data Source={db.Server};Version=3;";
                case "SQL Server":
                    int port = db.Port > 0 ? db.Port : 1433;
                    return $"Server={db.Server},{port};Database={db.DatabaseName};User Id={db.UserId};Password={db.Password};";
                case "MySQL":
                    int myport = db.Port > 0 ? db.Port : 3306;
                    return $"Server={db.Server};Port={myport};Database={db.DatabaseName};Uid={db.UserId};Pwd={db.Password};";
                case "PostgreSQL":
                    int pgport = db.Port > 0 ? db.Port : 5432;
                    return $"Host={db.Server};Port={pgport};Database={db.DatabaseName};Username={db.UserId};Password={db.Password};";
                default:
                    return "";
            }
        }

        private bool TestConnection(string dbType, string connStr)
        {
            try
            {
                switch (dbType)
                {
                    case "SQLite":
                        using (var conn = new System.Data.SQLite.SQLiteConnection(connStr))
                        {
                            conn.Open();
                            conn.Close();
                        }
                        break;
                    case "SQL Server":
                        using (var conn = new System.Data.SqlClient.SqlConnection(connStr))
                        {
                            conn.Open();
                            conn.Close();
                        }
                        break;
                    case "MySQL":
                        using (var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr))
                        {
                            conn.Open();
                            conn.Close();
                        }
                        break;
                    //case "PostgreSQL":
                    //    using (var conn = new Npgsql.NpgsqlConnection(connStr))
                    //    {
                    //        conn.Open();
                    //        conn.Close();
                    //    }
                    //    break;
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"连接失败: {ex.Message}");
                return false;
            }
        }

        private void PropertyGridDb_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            // 当属性改变时，清空自动生成的连接字符串
            if (_currentDatabase != null && e.ChangedItem.PropertyDescriptor.Name != "ConnectionString")
            {
                _currentDatabase.ConnectionString = "";
            }
        }

        private void UpdateDbStatus(string message, Color color)
        {
            if (btnSaveDb != null)
            {
                btnSaveDb.Text = message;
                btnSaveDb.ForeColor = color;
            }
        }
    }
}

using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Helpers;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Drawing;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class ModelConfigForm : Form
    {
        private List<ModelConfig> _models = new List<ModelConfig>();
        private ModelConfig _currentModel;
        private int _currentModelId = -1;

        // 解析脚本相关
        private List<ParseScript> _scripts = new List<ParseScript>();
        private ParseScript _currentScript;
        private int _currentScriptId = -1;

        public ModelConfigForm()
        {
            InitializeComponent();

            // 绑定事件
            this.Load += ModelConfigForm_Load;
            listBoxModels.SelectedIndexChanged += ListBoxModels_SelectedIndexChanged;
            btnAddModel.Click += BtnAddModel_Click;
            btnAddField.Click += BtnAddField_Click;
            btnSaveModel.Click += BtnSaveModel_Click;

            btnAddScript.Click += BtnAddScript_Click;
            btnSaveScript.Click += BtnSaveScript_Click;
            listBoxScripts.SelectedIndexChanged += ListBoxScripts_SelectedIndexChanged;
            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;
        }

        private void ModelConfigForm_Load(object sender, EventArgs e)
        {
            LoadModels();
            InitMachineCheckboxes();
            LoadParentModels(); 
        }

        /// <summary>
        /// 选择脚本时加载数据
        /// </summary>
        private void ListBoxScripts_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxScripts.SelectedIndex < 0) return;

            _currentScript = _scripts[listBoxScripts.SelectedIndex];
            _currentScriptId = _currentScript.Id;

            LoadScriptToUI();
        }

        /// <summary>
        /// 加载脚本数据到界面
        /// </summary>
        private void LoadScriptToUI()
        {
            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT Name, ModelId, FileExtension, ScriptCode, IsEnabled FROM ParseScripts WHERE Id = @Id";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", _currentScriptId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            txtScriptName.Text = reader.GetString(0);

                            // 选中模型
                            int modelId = reader.GetInt32(1);
                            for (int i = 0; i < cmbScriptModel.Items.Count; i++)
                            {
                                var item = cmbScriptModel.Items[i] as ModelItem;
                                if (item != null && item.Id == modelId)
                                {
                                    cmbScriptModel.SelectedIndex = i;
                                    break;
                                }
                            }

                            cmbScriptFileType.SelectedItem = reader.GetString(2);
                            rtxtScriptCode.Text = reader.GetString(3);
                            chkScriptEnabled.Checked = reader.GetInt32(4) == 1;
                            //txtScriptDescription.Text = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        }
                    }
                }
            }

            // 加载适用的机台
            LoadScriptMachines();
        }

        /// <summary>
        /// 加载脚本适用的机台
        /// </summary>
        private void LoadScriptMachines()
        {
            // 先清空所有机台复选框
            foreach (Control ctrl in flowLayoutMachines.Controls)
            {
                if (ctrl is CheckBox chk && chk.Tag is int)
                {
                    chk.Checked = false;
                }
            }

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT MachineId FROM ScriptMachines WHERE ScriptId = @ScriptId";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ScriptId", _currentScriptId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int machineId = reader.GetInt32(0);
                            foreach (Control ctrl in flowLayoutMachines.Controls)
                            {
                                if (ctrl is CheckBox chk && chk.Tag is int && (int)chk.Tag == machineId)
                                {
                                    chk.Checked = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// 新建脚本
        /// </summary>
        private void BtnAddScript_Click(object sender, EventArgs e)
        {
            _currentScriptId = -1;
            _currentScript = null;

            txtScriptName.Text = "新脚本";
            //txtScriptDescription.Text = "";
            rtxtScriptCode.Text = GetDefaultScriptCode();
            chkScriptEnabled.Checked = true;
            cmbScriptFileType.SelectedIndex = 0;

            // 清空机台选择
            foreach (Control ctrl in flowLayoutMachines.Controls)
            {
                if (ctrl is CheckBox chk && chk.Tag is int)
                {
                    chk.Checked = false;
                }
            }

            listBoxScripts.SelectedIndex = -1;
        }

        private string GetDefaultScriptCode()
        {
            return @"// 请编写解析代码
                    // 可用变量: filePath (文件路径)
                    // 返回: Dictionary<string, object> result

                    // Excel 解析示例:
                    // using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    // {
                    //     var workbook = new XSSFWorkbook(fs);
                    //     var sheet = workbook.GetSheetAt(0);
                    //     result[""字段名""] = sheet.GetRow(0)?.GetCell(0)?.ToString();
                    // }";
        }


        private void BtnSaveScript_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtScriptName.Text))
            {
                MessageBox.Show("请输入脚本名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cmbScriptModel.SelectedItem == null)
            {
                MessageBox.Show("请选择关联模型", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(rtxtScriptCode.Text))
            {
                MessageBox.Show("请编写脚本代码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int modelId = ((ModelItem)cmbScriptModel.SelectedItem).Id;
            string fileExtension = cmbScriptFileType.SelectedItem?.ToString() ?? ".xlsx";

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();

                    if (_currentScriptId == -1)
                    {
                        // 新增脚本
                        string sql = @"INSERT INTO ParseScripts (Name, ModelId, FileExtension, ScriptCode, IsEnabled)
                               VALUES (@Name, @ModelId, @FileExtension, @ScriptCode, @IsEnabled)";
                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@Name", txtScriptName.Text);
                            cmd.Parameters.AddWithValue("@ModelId", modelId);
                            cmd.Parameters.AddWithValue("@FileExtension", fileExtension);
                            cmd.Parameters.AddWithValue("@ScriptCode", rtxtScriptCode.Text);
                            cmd.Parameters.AddWithValue("@IsEnabled", chkScriptEnabled.Checked ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }

                        // 获取新ID
                        using (var cmd = new SQLiteCommand("SELECT last_insert_rowid()", conn))
                        {
                            _currentScriptId = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                    else
                    {
                        // 更新脚本
                        string sql = @"UPDATE ParseScripts SET Name = @Name, ModelId = @ModelId, FileExtension = @FileExtension, 
                               ScriptCode = @ScriptCode, IsEnabled = @IsEnabled, UpdateTime = CURRENT_TIMESTAMP
                               WHERE Id = @Id";
                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", _currentScriptId);
                            cmd.Parameters.AddWithValue("@Name", txtScriptName.Text);
                            cmd.Parameters.AddWithValue("@ModelId", modelId);
                            cmd.Parameters.AddWithValue("@FileExtension", fileExtension);
                            cmd.Parameters.AddWithValue("@ScriptCode", rtxtScriptCode.Text);
                            cmd.Parameters.AddWithValue("@IsEnabled", chkScriptEnabled.Checked ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }

                        // 删除旧的机台关联
                        string delSql = "DELETE FROM ScriptMachines WHERE ScriptId = @ScriptId";
                        using (var cmd = new SQLiteCommand(delSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ScriptId", _currentScriptId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 保存机台关联
                    foreach (Control ctrl in flowLayoutMachines.Controls)
                    {
                        if (ctrl is CheckBox chk && chk.Tag is int && chk.Checked)
                        {
                            string insertSql = "INSERT INTO ScriptMachines (ScriptId, MachineId) VALUES (@ScriptId, @MachineId)";
                            using (var cmd = new SQLiteCommand(insertSql, conn))
                            {
                                cmd.Parameters.AddWithValue("@ScriptId", _currentScriptId);
                                cmd.Parameters.AddWithValue("@MachineId", (int)chk.Tag);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                // 保存成功后，清除脚本缓存
                ScriptEngine.ClearCache();
                MessageBox.Show("保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                LoadScripts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        /// <summary>
        /// 获取选中的机台ID列表
        /// </summary>
        private List<int> GetSelectedMachineIds()
        {
            List<int> ids = new List<int>();
            foreach (Control ctrl in flowLayoutMachines.Controls)
            {
                if (ctrl is CheckBox chk && chk.Tag is int && chk.Checked)
                {
                    ids.Add((int)chk.Tag);
                }
            }
            return ids;
        }

        /// <summary>
        /// 加载机台复选框
        /// </summary>
        private void LoadMachineCheckboxes()
        {
            // 先清空
            flowLayoutMachines.Controls.Clear();

            // 全选按钮
            CheckBox chkSelectAll = new CheckBox
            {
                Text = "全选",
                AutoSize = true,
                Tag = "all"
            };
            chkSelectAll.CheckedChanged += ChkSelectAll_Changed;
            flowLayoutMachines.Controls.Add(chkSelectAll);

            // 取消全选按钮
            CheckBox chkUnselectAll = new CheckBox
            {
                Text = "取消全选",
                AutoSize = true,
                Tag = "unall"
            };
            chkUnselectAll.CheckedChanged += ChkUnselectAll_Changed;
            flowLayoutMachines.Controls.Add(chkUnselectAll);

            // 从数据库加载机台
            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT Id, MachineName FROM Machines ORDER BY SortOrder";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        string name = reader.GetString(1);
                        CheckBox chk = new CheckBox
                        {
                            Text = name,
                            AutoSize = true,
                            Tag = id
                        };
                        flowLayoutMachines.Controls.Add(chk);
                    }
                }
            }

            // 如果没有机台数据，添加默认6台
            if (flowLayoutMachines.Controls.Count == 2)
            {
                for (int i = 1; i <= 6; i++)
                {
                    CheckBox chk = new CheckBox
                    {
                        Text = $"机台{i}",
                        AutoSize = true,
                        Tag = i
                    };
                    flowLayoutMachines.Controls.Add(chk);
                }
            }
        }

        private void ChkSelectAll_Changed(object sender, EventArgs e)
        {
            CheckBox chkAll = sender as CheckBox;
            foreach (Control ctrl in flowLayoutMachines.Controls)
            {
                if (ctrl is CheckBox && ctrl.Tag is int)
                {
                    ((CheckBox)ctrl).Checked = chkAll.Checked;
                }
            }
        }


        /// <summary>
        /// 加载模型下拉框
        /// </summary>
        private void LoadModelCombo()
        {
            cmbScriptModel.Items.Clear();

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT Id, ModelName FROM DataModels WHERE IsActive = 1 ORDER BY Id";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cmbScriptModel.Items.Add(new ModelItem { Id = reader.GetInt32(0), Name = reader.GetString(1) });
                    }
                }
            }

            if (cmbScriptModel.Items.Count > 0) cmbScriptModel.SelectedIndex = 0;
        }


        /// <summary>
        /// 加载脚本列表
        /// </summary>
        private void LoadScripts()
        {
            _scripts.Clear();
            listBoxScripts.Items.Clear();

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT Id, Name FROM ParseScripts ORDER BY Id";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var script = new ParseScript
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        };
                        _scripts.Add(script);
                        listBoxScripts.Items.Add(script.Name);
                    }
                }
            }
        }

        private void InitMachineCheckboxes()
        {
            flowLayoutMachines.Controls.Clear();
            flowLayoutMachines.AutoScroll = true;
            flowLayoutMachines.WrapContents = true;

            // 全选按钮
            CheckBox chkSelectAll = new CheckBox
            {
                Text = "全选",
                AutoSize = true,
                Tag = "all"
            };
            chkSelectAll.CheckedChanged += ChkSelectAll_Changed;
            flowLayoutMachines.Controls.Add(chkSelectAll);

            // 取消全选按钮
            CheckBox chkUnselectAll = new CheckBox
            {
                Text = "取消全选",
                AutoSize = true,
                Tag = "unall"
            };
            chkUnselectAll.CheckedChanged += ChkUnselectAll_Changed;
            flowLayoutMachines.Controls.Add(chkUnselectAll);

            // 机台复选框
            for (int i = 1; i <= 6; i++)
            {
                CheckBox chk = new CheckBox
                {
                    Text = $"机台{i}",
                    AutoSize = true,
                    Tag = i
                };
                flowLayoutMachines.Controls.Add(chk);
            }
        }

        private void ChkUnselectAll_Changed(object sender, EventArgs e)
        {
            CheckBox chkUnall = sender as CheckBox;
            if (chkUnall.Checked)
            {
                foreach (Control ctrl in flowLayoutMachines.Controls)
                {
                    if (ctrl is CheckBox && ctrl.Tag is int)
                    {
                        ((CheckBox)ctrl).Checked = false;
                    }
                }
                chkUnall.Checked = false; // 自动复位
            }
        }


        // 加载模型列表
        private void LoadModels()
        {
            _models.Clear();
            listBoxModels.Items.Clear();

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    // 添加 ParentModelId 字段
                    string sql = "SELECT Id, ModelName, TableName, ParentModelId, Description, IsActive FROM DataModels ORDER BY Id";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var model = new ModelConfig
                            {
                                Id = reader.GetInt32(0),
                                ModelName = reader.GetString(1),
                                TableName = reader.GetString(2),
                                ParentModelId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                IsActive = reader.GetInt32(5) == 1
                            };
                            _models.Add(model);
                            listBoxModels.Items.Add(model.ModelName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载模型失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 选择模型时加载数据
        private void ListBoxModels_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxModels.SelectedIndex < 0) return;

            _currentModel = _models[listBoxModels.SelectedIndex];
            _currentModelId = _currentModel.Id;

            // 显示到控件
            txtModelName.Text = _currentModel.ModelName;
            txtTableName.Text = _currentModel.TableName;
            txtDescription.Text = _currentModel.Description;
            chkIsActive.Checked = _currentModel.IsActive;

            // 重新加载父模型下拉框（排除当前模型）
            LoadParentModels(_currentModel.Id);

            // 选中父模型
            SelectParentModel(_currentModel.ParentModelId);

            // 加载字段列表
            LoadFields(_currentModelId);
        }

        private void SelectParentModel(int parentId)
        {
            for (int i = 0; i < cmbParentModel.Items.Count; i++)
            {
                var item = cmbParentModel.Items[i] as ParentModelItem;
                if (item != null && item.Id == parentId)
                {
                    cmbParentModel.SelectedIndex = i;
                    return;
                }
            }
            cmbParentModel.SelectedIndex = 0;
        }

        private void LoadParentModels(int excludeId = -1)
        {
            cmbParentModel.Items.Clear();

            // 添加"无"选项
            cmbParentModel.Items.Add(new ParentModelItem { Id = -1, Name = "(无)" });

            // 添加基类选项
            cmbParentModel.Items.Add(new ParentModelItem { Id = -2, Name = "BaseEntity（基类）" });

            // 添加其他模型作为可选父模型
            foreach (var model in _models)
            {
                if (model.Id != excludeId)
                {
                    cmbParentModel.Items.Add(new ParentModelItem { Id = model.Id, Name = model.ModelName });
                }
            }

            cmbParentModel.DisplayMember = "Name";
            cmbParentModel.ValueMember = "Id";

            if (cmbParentModel.Items.Count > 0)
            {
                cmbParentModel.SelectedIndex = 0;
            }
        }

        // 辅助类
        public class ParentModelItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        // 加载字段列表
        private void LoadFields(int modelId)
        {
            dgvFields.Rows.Clear();

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    string sql = "SELECT FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description FROM ModelFields WHERE ModelId = @ModelId ORDER BY Id";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@ModelId", modelId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int rowIndex = dgvFields.Rows.Add();
                                dgvFields.Rows[rowIndex].Cells["colFieldName"].Value = reader.GetString(0);
                                dgvFields.Rows[rowIndex].Cells["colFieldType"].Value = reader.GetString(1);
                                dgvFields.Rows[rowIndex].Cells["colFieldLength"].Value = reader.GetInt32(2);
                                dgvFields.Rows[rowIndex].Cells["colIsRequired"].Value = reader.GetInt32(3) == 1;
                                dgvFields.Rows[rowIndex].Cells["colIsPrimaryKey"].Value = reader.GetInt32(4) == 1;
                                dgvFields.Rows[rowIndex].Cells["colIsIdentity"].Value = reader.GetInt32(5) == 1;
                                dgvFields.Rows[rowIndex].Cells["colDescription"].Value = reader.IsDBNull(6) ? "" : reader.GetString(6);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载字段失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 新建模型
        private void BtnAddModel_Click(object sender, EventArgs e)
        {
            _currentModel = new ModelConfig
            {
                Id = -1,
                ModelName = "新模型",
                TableName = "NewTable",
                Description = "",
                IsActive = true
            };
            _currentModelId = -1;

            // 清空控件
            txtModelName.Text = "新模型";
            txtTableName.Text = "NewTable";
            txtDescription.Text = "";
            chkIsActive.Checked = true;
            dgvFields.Rows.Clear();

            listBoxModels.SelectedIndex = -1;
        }

        // 添加字段
        private void BtnAddField_Click(object sender, EventArgs e)
        {
            dgvFields.Rows.Add("新字段", "string", 0, false, false, false, "");
        }

        // 保存模型
        private void BtnSaveModel_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtModelName.Text))
            {
                MessageBox.Show("请输入模型名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(txtTableName.Text))
            {
                MessageBox.Show("请输入表名", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 更新当前模型数据
            if (_currentModel == null)
            {
                _currentModel = new ModelConfig();
            }

            _currentModel.ModelName = txtModelName.Text;
            _currentModel.TableName = txtTableName.Text;
            _currentModel.Description = txtDescription.Text;
            _currentModel.IsActive = chkIsActive.Checked;

            // 获取选中的父模型ID
            int parentModelId = 0;
            if (cmbParentModel.SelectedItem != null)
            {
                if (cmbParentModel.SelectedItem is ParentModelItem selectedItem)
                {
                    int id = selectedItem.Id;
                    if (id == -1)  // 无
                    {
                        parentModelId = 0;
                    }
                    else if (id == -2)  // 基类
                    {
                        parentModelId = -2;
                    }
                    else
                    {
                        parentModelId = id;
                    }
                }
                else if (cmbParentModel.SelectedItem is KeyValuePair<int, string> kvp)
                {
                    parentModelId = kvp.Key;
                }
                else
                {
                    // 如果是字符串，尝试解析
                    string selected = cmbParentModel.SelectedItem.ToString();
                    if (selected == "BaseEntity（基类）")
                    {
                        parentModelId = -2;
                    }
                    else if (selected != "(无)" && selected != "无")
                    {
                        // 查找模型ID
                        var model = _models.FirstOrDefault(m => m.ModelName == selected);
                        if (model != null)
                            parentModelId = model.Id;
                    }
                }
            }
            _currentModel.ParentModelId = parentModelId;

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();

                    // 保存模型
                    if (_currentModelId == -1)
                    {
                        // 新增（包含 ParentModelId）
                        string sql = "INSERT INTO DataModels (ModelName, TableName, ParentModelId, Description, IsActive) VALUES (@ModelName, @TableName, @ParentModelId, @Description, @IsActive)";
                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ModelName", _currentModel.ModelName);
                            cmd.Parameters.AddWithValue("@TableName", _currentModel.TableName);
                            cmd.Parameters.AddWithValue("@ParentModelId", _currentModel.ParentModelId);
                            cmd.Parameters.AddWithValue("@Description", _currentModel.Description ?? "");
                            cmd.Parameters.AddWithValue("@IsActive", _currentModel.IsActive ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }

                        // 获取新ID
                        using (var cmd = new SQLiteCommand("SELECT last_insert_rowid()", conn))
                        {
                            _currentModelId = Convert.ToInt32(cmd.ExecuteScalar());
                            _currentModel.Id = _currentModelId;
                        }
                    }
                    else
                    {
                        // 更新（包含 ParentModelId）
                        string sql = "UPDATE DataModels SET ModelName = @ModelName, TableName = @TableName, ParentModelId = @ParentModelId, Description = @Description, IsActive = @IsActive WHERE Id = @Id";
                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@Id", _currentModelId);
                            cmd.Parameters.AddWithValue("@ModelName", _currentModel.ModelName);
                            cmd.Parameters.AddWithValue("@TableName", _currentModel.TableName);
                            cmd.Parameters.AddWithValue("@ParentModelId", _currentModel.ParentModelId);
                            cmd.Parameters.AddWithValue("@Description", _currentModel.Description ?? "");
                            cmd.Parameters.AddWithValue("@IsActive", _currentModel.IsActive ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }

                        // 删除旧字段
                        string delSql = "DELETE FROM ModelFields WHERE ModelId = @ModelId";
                        using (var cmd = new SQLiteCommand(delSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ModelId", _currentModelId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 保存字段
                    foreach (DataGridViewRow row in dgvFields.Rows)
                    {
                        if (row.IsNewRow) continue;

                        string fieldName = row.Cells["colFieldName"].Value?.ToString();
                        if (string.IsNullOrEmpty(fieldName)) continue;

                        string sql = @"INSERT INTO ModelFields (ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description)
                       VALUES (@ModelId, @FieldName, @FieldType, @FieldLength, @IsRequired, @IsPrimaryKey, @IsIdentity, @Description)";

                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ModelId", _currentModelId);
                            cmd.Parameters.AddWithValue("@FieldName", fieldName);
                            cmd.Parameters.AddWithValue("@FieldType", row.Cells["colFieldType"].Value?.ToString() ?? "string");
                            cmd.Parameters.AddWithValue("@FieldLength", Convert.ToInt32(row.Cells["colFieldLength"].Value ?? 0));
                            cmd.Parameters.AddWithValue("@IsRequired", Convert.ToBoolean(row.Cells["colIsRequired"].Value ?? false) ? 1 : 0);
                            cmd.Parameters.AddWithValue("@IsPrimaryKey", Convert.ToBoolean(row.Cells["colIsPrimaryKey"].Value ?? false) ? 1 : 0);
                            cmd.Parameters.AddWithValue("@IsIdentity", Convert.ToBoolean(row.Cells["colIsIdentity"].Value ?? false) ? 1 : 0);
                            cmd.Parameters.AddWithValue("@Description", row.Cells["colDescription"].Value?.ToString() ?? "");
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                // 保存成功后，生成 Model 类
                try
                {
                    List<ModelField> fields = LoadFieldList(_currentModelId);

                    // 生成 Model 类文件
                    GenerateModelClass(_currentModel, fields);

                    MessageBox.Show("保存成功！Model 类已生成。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存成功，但生成 Model 失败: {ex.Message}", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // 刷新列表
                LoadModels();

                // 重新选中当前模型
                for (int i = 0; i < listBoxModels.Items.Count; i++)
                {
                    if (listBoxModels.Items[i].ToString() == _currentModel.ModelName)
                    {
                        listBoxModels.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 使用 ADO.NET 创建数据库表
        /// </summary>
        private void CreateTableWithAdo(string tableName, List<ModelField> fields)
        {
            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();

                // 构建建表SQL
                string sql = $"CREATE TABLE IF NOT EXISTS [{tableName}] (";
                sql += "[Id] INTEGER PRIMARY KEY AUTOINCREMENT, ";

                foreach (var field in fields)
                {
                    string dbType = GetDbType(field.FieldType);
                    sql += $"[{field.FieldName}] {dbType}, ";
                }

                sql = sql.TrimEnd(',', ' ');
                sql += ")";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 类型转 SQLite 数据类型
        /// </summary>
        private string GetDbType(string typeName)
        {
            switch (typeName.ToLower())
            {
                case "string": return "TEXT";
                case "int": return "INTEGER";
                case "long": return "INTEGER";
                case "decimal": return "REAL";
                case "float": return "REAL";
                case "double": return "REAL";
                case "datetime": return "TEXT";
                case "bool": return "INTEGER";
                default: return "TEXT";
            }
        }

        /// <summary>
        /// 根据模型配置动态创建类型
        /// </summary>
        private Type CreateDynamicType(ModelConfig model, List<ModelField> fields)
        {
            // 1. 定义程序集和模块
            AssemblyName assemblyName = new AssemblyName("DynamicModelAssembly");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicModelModule");

            // 2. 确定父类型
            Type parentType = typeof(object);

            if (model.ParentModelId == -2)
            {
                // 继承动态基类 BaseEntity
                try
                {
                    parentType = DynamicBaseEntityBuilder.GetBaseEntityType();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"获取基类失败: {ex.Message}");
                    parentType = typeof(object);
                }
            }
            else if (model.ParentModelId > 0)
            {
                // 查找父模型
                var parentModel = _models.FirstOrDefault(m => m.Id == model.ParentModelId);
                if (parentModel != null)
                {
                    // 递归创建父类型
                    List<ModelField> parentFields = LoadFieldList(parentModel.Id);
                    parentType = CreateDynamicType(parentModel, parentFields);
                }
            }
            // else parentModelId == 0，继承 object

            // 3. 定义类型
            TypeBuilder typeBuilder = moduleBuilder.DefineType(
                $"MachineDataAcquisitionSystem.Models.{model.TableName}",
                TypeAttributes.Public | TypeAttributes.Class,
                parentType
            );

            // 4. 获取父类已有字段
            HashSet<string> parentFieldNames = new HashSet<string>();
            foreach (var prop in parentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                parentFieldNames.Add(prop.Name);
            }

            // 5. 添加字段属性
            foreach (var field in fields)
            {
                // 跳过父类已有的字段
                if (parentFieldNames.Contains(field.FieldName))
                    continue;

                Type propertyType = GetPropertyType(field.FieldType);

                // 创建私有字段
                FieldBuilder fieldBuilder = typeBuilder.DefineField(
                    "_" + field.FieldName,
                    propertyType,
                    FieldAttributes.Private
                );

                // 创建属性
                PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
                    field.FieldName,
                    PropertyAttributes.None,
                    propertyType,
                    null
                );

                // 创建 get 方法
                MethodBuilder getMethod = typeBuilder.DefineMethod(
                    "get_" + field.FieldName,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    propertyType,
                    Type.EmptyTypes
                );
                ILGenerator getIl = getMethod.GetILGenerator();
                getIl.Emit(OpCodes.Ldarg_0);
                getIl.Emit(OpCodes.Ldfld, fieldBuilder);
                getIl.Emit(OpCodes.Ret);

                // 创建 set 方法
                MethodBuilder setMethod = typeBuilder.DefineMethod(
                    "set_" + field.FieldName,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    null,
                    new Type[] { propertyType }
                );
                ILGenerator setIl = setMethod.GetILGenerator();
                setIl.Emit(OpCodes.Ldarg_0);
                setIl.Emit(OpCodes.Ldarg_1);
                setIl.Emit(OpCodes.Stfld, fieldBuilder);
                setIl.Emit(OpCodes.Ret);

                // 绑定 get/set 到属性
                propertyBuilder.SetGetMethod(getMethod);
                propertyBuilder.SetSetMethod(setMethod);
            }

            // 6. 创建类型
            return typeBuilder.CreateType();
        }
        /// <summary>
        /// 获取基类字段配置
        /// </summary>
        private List<ModelField> GetBaseFields()
        {
            var baseFields = new List<ModelField>();

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    // 只查询确定的列，不查询可能不存在的列
                    string sql = "SELECT FieldName, FieldType, Description FROM BaseFields ORDER BY SortOrder";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            baseFields.Add(new ModelField
                            {
                                FieldName = reader.GetString(0),
                                FieldType = reader.GetString(1),
                                FieldLength = 0,
                                IsRequired = true,
                                IsPrimaryKey = false,
                                IsIdentity = false,
                                Description = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取基类字段失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return baseFields;
        }

        /// <summary>
        /// 生成 Model 类文件
        /// </summary>
        /// <summary>
        /// 生成 Model 类文件
        /// </summary>
        private void GenerateModelClass(ModelConfig model, List<ModelField> fields)
        {
            // 从数据库读取基类字段配置
            List<BaseFieldConfig> baseFields = GetBaseFieldsFromDb();

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"namespace MachineDataAcquisitionSystem.Models");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {model.ModelName}");
            sb.AppendLine("    {");

            // 添加基类字段（不生成默认值）
            foreach (var field in baseFields)
            {
                if (field.FieldName == "CID") continue;  // 跳过 CID

                string propertyType = GetCSharpType(field.FieldType);
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {field.Description}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        public {propertyType} {field.FieldName} {{ get; set; }}");
            }

            // 添加自定义字段
            foreach (var field in fields)
            {
                string propertyType = GetCSharpType(field.FieldType);
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {field.Description}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine($"        public {propertyType} {field.FieldName} {{ get; set; }}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            // 保存到文件
            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels");
            if (!Directory.Exists(modelDir))
                Directory.CreateDirectory(modelDir);

            string filePath = Path.Combine(modelDir, $"{model.ModelName}.cs");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            MessageBox.Show($"Model 类已生成：{filePath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 从数据库读取基类字段配置
        /// </summary>
        private List<BaseFieldConfig> GetBaseFieldsFromDb()
        {
            var fields = new List<BaseFieldConfig>();

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT FieldName, FieldType, DefaultValue, Description FROM BaseFields ORDER BY SortOrder";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        fields.Add(new BaseFieldConfig
                        {
                            FieldName = reader.GetString(0),
                            FieldType = reader.GetString(1),
                            DefaultValue = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Description = reader.IsDBNull(3) ? "" : reader.GetString(3)
                        });
                    }
                }
            }

            return fields;
        }

        /// <summary>
        /// 根据默认值表达式生成代码
        /// </summary>
        private static string GetDefaultValueCode(string defaultValue, string fieldType)
        {
            if (string.IsNullOrEmpty(defaultValue)) return "";

            // YitIdHelper.NextId()
            if (defaultValue.Contains("YitIdHelper.NextId()"))
            {
                return "YitIdHelper.NextId()";
            }

            // DateTime.Now
            if (defaultValue == "DateTime.Now")
            {
                return "DateTime.Now";
            }

            // 字符串类型
            if (fieldType == "string")
            {
                return $"\"{defaultValue}\"";
            }

            // 数字类型（long, int, decimal）
            if (fieldType == "long" || fieldType == "int" || fieldType == "decimal")
            {
                // 去掉引号，直接返回数字
                return defaultValue.Replace("\"", "");
            }

            return $"\"{defaultValue}\"";
        }

        private static string GetDefaultValueExpression(ModelField field)
        {
            if (field.FieldName == "CID" && field.FieldType == "long")
            {
                return "YitIdHelper.NextId()";
            }
            if (field.FieldName == "CDATETIME_CREATED" && field.FieldType == "datetime")
            {
                return "DateTime.Now";
            }
            if (field.FieldName == "CDATETIME_MODIFIED" && field.FieldType == "datetime")
            {
                return "DateTime.Now";
            }
            if (field.FieldName == "CUSER_CREATED" && field.FieldType == "string")
            {
                return "\"SYS\"";
            }
            if (field.FieldName == "CUSER_MODIFIED" && field.FieldType == "string")
            {
                return "\"SYS\"";
            }
            if (field.FieldName == "CSTATE" && field.FieldType == "string")
            {
                return "\"A\"";
            }

            return "";
        }

        /// <summary>
        /// 类型转 C# 类型
        /// </summary>
        private string GetCSharpType(string typeName)
        {
            switch (typeName.ToLower())
            {
                case "string": return "string";
                case "int": return "int";
                case "long": return "long";
                case "decimal": return "decimal";
                case "float": return "float";
                case "double": return "double";
                case "datetime": return "DateTime";
                case "bool": return "bool";
                default: return "string";
            }
        }

        /// <summary>
        /// 加载模型的字段列表
        /// </summary>
        private List<ModelField> LoadFieldList(int modelId)
        {
            var fields = new List<ModelField>();

            using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                conn.Open();
                string sql = "SELECT FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description FROM ModelFields WHERE ModelId = @ModelId ORDER BY Id";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ModelId", modelId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            fields.Add(new ModelField
                            {
                                FieldName = reader.GetString(0),
                                FieldType = reader.GetString(1),
                                FieldLength = reader.GetInt32(2),
                                IsRequired = reader.GetInt32(3) == 1,
                                IsPrimaryKey = reader.GetInt32(4) == 1,
                                IsIdentity = reader.GetInt32(5) == 1,
                                Description = reader.IsDBNull(6) ? "" : reader.GetString(6)
                            });
                        }
                    }
                }
            }

            return fields;
        }

        // 在 tabControl1 的 SelectedIndexChanged 事件中添加
        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedTab == tabPageScript)
            {
                LoadModelCombo();
                LoadMachineCheckboxes();
                LoadScripts();
            }
        }

        /// <summary>
        /// 字符串类型转 .NET 类型
        /// </summary>
        private Type GetPropertyType(string typeName)
        {
            switch (typeName.ToLower())
            {
                case "string": return typeof(string);
                case "int": return typeof(int);
                case "long": return typeof(long);
                case "decimal": return typeof(decimal);
                case "float": return typeof(float);
                case "double": return typeof(double);
                case "datetime": return typeof(DateTime);
                case "bool": return typeof(bool);
                default: return typeof(string);
            }
        }

        private void btnExcelTemplate_Click(object sender, EventArgs e)
        {
            string template = @"
                // Excel 解析模板（使用 NPOI）
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    // 根据扩展名选择不同的 Workbook
                    NPOI.SS.UserModel.IWorkbook workbook;
                    if (filePath.EndsWith("".xlsx""))
                    {
                        workbook = new XSSFWorkbook(fs);  // .xlsx
                    }
                    else
                    {
                        workbook = new HSSFWorkbook(fs);  // .xls
                    }
    
                    var sheet = workbook.GetSheetAt(0);
    
                    // 创建 Model 对象（替换成你的模型名称）
                    var model = new TestData();
    
                    // 读取指定位置的值（带空值处理）
                    model.ProductSn = sheet.GetRow(0)?.GetCell(0)?.ToString() ?? """";
    
                    // 安全转换 decimal
                    string testValueStr = sheet.GetRow(1)?.GetCell(0)?.ToString();
                    if (decimal.TryParse(testValueStr, out decimal testValue))
                    {
                        model.TestValue = testValue;
                    }
                    else
                    {
                        model.TestValue = 0;
                    }
    
                    model.TestTime = DateTime.Now;
                    model.MachineId = machineId;
                    model.FileName = Path.GetFileName(filePath);
    
                    return model;
                }";

            rtxtScriptCode.Text = template;
        }

        private void btnCsvTemplate_Click(object sender, EventArgs e)
        {
            string template = @"
                // CSV 解析模板
                using (var reader = new StreamReader(filePath))
                {
                    string line = reader.ReadLine();
                    if (line == null) return null;
    
                    var values = line.Split(',');
    
                    // 创建 Model 对象（替换成你的模型名称）
                    var model = new TestData();
    
                    model.ProductSn = values.Length > 0 ? values[0] : """";
    
                    // 安全转换 decimal
                    if (values.Length > 1 && decimal.TryParse(values[1], out decimal testValue))
                    {
                        model.TestValue = testValue;
                    }
                    else
                    {
                        model.TestValue = 0;
                    }
    
                    model.TestTime = DateTime.Now;
                    model.MachineId = machineId;
                    model.FileName = Path.GetFileName(filePath);
    
                    return model;
                }";

            rtxtScriptCode.Text = template;
        }

        private void btnJsonTemplate_Click(object sender, EventArgs e)
        {
            string template = @"
                // JSON 解析模板
                string json = File.ReadAllText(filePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                // 创建 Model 对象（替换成你的模型名称）
                var model = new TestData();

                model.ProductSn = data.ContainsKey(""ProductSn"") ? data[""ProductSn""]?.ToString() : """";
                model.TestValue = data.ContainsKey(""TestValue"") && decimal.TryParse(data[""TestValue""]?.ToString(), out decimal val) ? val : 0;
                model.TestTime = DateTime.Now;
                model.MachineId = machineId;
                model.FileName = Path.GetFileName(filePath);

                return model;";

            rtxtScriptCode.Text = template;
        }

        private void btnTxtTemplate_Click(object sender, EventArgs e)
        {
            string template = @"
                // TXT 解析模板
                var lines = File.ReadAllLines(filePath);

                // 创建 Model 对象（替换成你的模型名称）
                var model = new TestData();

                model.ProductSn = lines.Length > 0 ? lines[0] : """";
                model.TestValue = lines.Length > 1 && decimal.TryParse(lines[1], out decimal val) ? val : 0;
                model.TestTime = DateTime.Now;
                model.MachineId = machineId;
                model.FileName = Path.GetFileName(filePath);

                return model;";

            rtxtScriptCode.Text = template;
        }
    }

    /// <summary>
    /// 基类字段配置
    /// </summary>
    public class BaseFieldConfig
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public string DefaultValue { get; set; }
        public string Description { get; set; }
    }

    // 数据模型类
    public class ModelConfig
    {
        public int Id { get; set; }
        public string ModelName { get; set; }
        public string TableName { get; set; }
        public string Namespace { get; set; } = "MachineDataAcquisitionSystem.Models";
        public int ParentModelId { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }

    // 解析脚本类
    public class ParseScript
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ModelId { get; set; }
        public string FileExtension { get; set; }
        public string ScriptCode { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
    }

    // 字段类
    public class ModelField
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public int FieldLength { get; set; }
        public bool IsRequired { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsIdentity { get; set; }
        public string Description { get; set; }
    }

    // 模型下拉框项
    public class ModelItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    // 机台项
    public class MachineItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }
}
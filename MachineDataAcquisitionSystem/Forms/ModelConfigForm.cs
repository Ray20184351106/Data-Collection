using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
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
        private Button _btnDeleteModel;
        private Button _btnDeleteScript;
        private Button _btnImportModels;
        private Button _btnCreateModelImportTemplate;
        private ContextMenuStrip _modelContextMenu;
        private ToolStripMenuItem _copyModelMenuItem;
        private ToolStripMenuItem _deleteModelMenuItem;

        public ModelConfigForm()
        {
            InitializeComponent();
            InitializeMappingUi();
            InitializeDeletionButtons();
            InitializeModelImportButtons();
            InitializeModelContextMenu();

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

        private void InitializeModelContextMenu()
        {
            _modelContextMenu = new ContextMenuStrip(components);
            _copyModelMenuItem = new ToolStripMenuItem("复制模型");
            _deleteModelMenuItem = new ToolStripMenuItem("删除模型");
            _copyModelMenuItem.Click += BtnCopyModel_Click;
            _deleteModelMenuItem.Click += BtnDeleteModel_Click;
            _modelContextMenu.Items.AddRange(new ToolStripItem[]
            {
                _copyModelMenuItem,
                new ToolStripSeparator(),
                _deleteModelMenuItem
            });
            _modelContextMenu.Opening += ModelContextMenu_Opening;
            listBoxModels.MouseDown += ListBoxModels_MouseDown;
            listBoxModels.ContextMenuStrip = _modelContextMenu;
        }

        private void ListBoxModels_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            int index = listBoxModels.IndexFromPoint(e.Location);
            if (index >= 0) listBoxModels.SelectedIndex = index;
        }

        private void ModelContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool hasSelection = _currentModelId > 0 && _currentModel != null;
            _copyModelMenuItem.Enabled = hasSelection;
            _deleteModelMenuItem.Enabled = hasSelection;
        }

        private void InitializeDeletionButtons()
        {
            _btnDeleteModel = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                Text = "删除模型",
                BackColor = Color.MistyRose
            };
            _btnDeleteModel.Click += BtnDeleteModel_Click;
            splitContainer1.Panel1.Controls.Add(_btnDeleteModel);

            _btnDeleteScript = new Button
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                Text = "删除脚本",
                BackColor = Color.MistyRose
            };
            _btnDeleteScript.Click += BtnDeleteScript_Click;
            splitContainer2.Panel1.Controls.Add(_btnDeleteScript);
        }

        private void InitializeModelImportButtons()
        {
            var buttonPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(3)
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            _btnImportModels = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 2, 0),
                Text = "Excel 导入"
            };
            _btnCreateModelImportTemplate = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 0, 0),
                Text = "下载模板"
            };
            _btnImportModels.Click += BtnImportModels_Click;
            _btnCreateModelImportTemplate.Click += BtnCreateModelImportTemplate_Click;
            buttonPanel.Controls.Add(_btnImportModels, 0, 0);
            buttonPanel.Controls.Add(_btnCreateModelImportTemplate, 1, 0);
            splitContainer1.Panel1.Controls.Add(buttonPanel);
            buttonPanel.BringToFront();
        }

        private void BtnImportModels_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择数据模型 Excel",
                Filter = "Excel 文件 (*.xlsx;*.xls)|*.xlsx;*.xls",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var service = new ModelExcelImportService(DatabaseHelper.GetConnectionString());
                    ModelExcelImportPreview preview = service.Preview(dialog.FileName);
                    if (!preview.CanImport)
                    {
                        MessageBox.Show(
                            BuildModelImportPreviewMessage(preview),
                            "Excel 导入校验失败",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    if (MessageBox.Show(
                            BuildModelImportPreviewMessage(preview) + Environment.NewLine + Environment.NewLine +
                            "确认新增这些模型吗？导入不会覆盖现有模型。",
                            "确认 Excel 导入",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    ModelExcelImportResult result = service.Import(preview);
                    List<string> sourceGenerationErrors = GenerateImportedModelSources(result);

                    LoadModels();
                    LoadParentModels();
                    SelectModel(result.FirstModelId);

                    string message = "导入成功：" + result.ModelCount + " 个模型，" +
                                     result.FieldCount + " 个字段。";
                    MessageBoxIcon icon = MessageBoxIcon.Information;
                    if (sourceGenerationErrors.Count > 0)
                    {
                        icon = MessageBoxIcon.Warning;
                        message += Environment.NewLine + Environment.NewLine +
                                   "以下 Model 类生成失败，可选择模型后点击“保存模型”重试：" +
                                   Environment.NewLine + string.Join(Environment.NewLine, sourceGenerationErrors);
                    }
                    MessageBox.Show(message, "Excel 导入完成", MessageBoxButtons.OK, icon);
                }
                catch (ModelExcelImportValidationException ex)
                {
                    MessageBox.Show(
                        BuildModelImportErrorMessage(ex.Errors),
                        "Excel 导入校验失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Excel 导入失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnCreateModelImportTemplate_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "保存数据模型导入模板",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                FileName = "数据模型导入模板.xlsx"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    new ModelExcelImportService(DatabaseHelper.GetConnectionString())
                        .CreateTemplate(dialog.FileName);
                    MessageBox.Show("模板已保存：" + dialog.FileName, "下载模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存模板失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private List<string> GenerateImportedModelSources(ModelExcelImportResult result)
        {
            var errors = new List<string>();
            foreach (ModelExcelImportedModel imported in result.Models)
            {
                try
                {
                    var model = new ModelConfig
                    {
                        Id = imported.Id,
                        ModelName = imported.Model.ModelName,
                        TableName = imported.Model.TableName,
                        Description = imported.Model.Description,
                        IsActive = imported.Model.IsActive
                    };
                    var fields = imported.Model.Fields.Select(field => new ModelField
                    {
                        FieldName = field.FieldName,
                        FieldType = field.FieldType,
                        FieldLength = field.FieldLength,
                        IsRequired = field.IsRequired,
                        IsPrimaryKey = field.IsPrimaryKey,
                        IsIdentity = field.IsIdentity,
                        Description = field.Description
                    }).ToList();
                    GenerateModelClass(model, fields, false);
                }
                catch (Exception ex)
                {
                    errors.Add(imported.Model.ModelName + "：" + ex.Message);
                }
            }
            return errors;
        }

        private static string BuildModelImportPreviewMessage(ModelExcelImportPreview preview)
        {
            var builder = new StringBuilder();
            builder.AppendLine("文件：" + Path.GetFileName(preview.FilePath));
            builder.AppendLine("模型：" + preview.ModelCount);
            builder.AppendLine("字段：" + preview.FieldCount);
            builder.AppendLine("启用模型：" + preview.EnabledModelCount);
            if (preview.Errors.Count > 0)
            {
                builder.AppendLine();
                builder.Append(BuildModelImportErrorMessage(preview.Errors));
            }
            return builder.ToString().TrimEnd();
        }

        private static string BuildModelImportErrorMessage(IEnumerable<ModelExcelImportError> errors)
        {
            List<ModelExcelImportError> list = errors.ToList();
            var lines = list.Take(20).Select(error => error.ToString()).ToList();
            if (list.Count > lines.Count)
                lines.Add("另有 " + (list.Count - lines.Count) + " 个错误未显示，请修正后重新导入。");
            return string.Join(Environment.NewLine, lines);
        }

        private void BtnDeleteModel_Click(object sender, EventArgs e)
        {
            if (_currentModelId <= 0 || _currentModel == null)
            {
                MessageBox.Show("请先选择要删除的数据模型。", "删除模型", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(
                    "确定删除数据模型“" + _currentModel.ModelName + "”及其字段吗？此操作不可撤销。",
                    "确认删除模型",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                new ConfigurationDeletionService(DatabaseHelper.GetDatabasePath()).DeleteModel(_currentModelId);
                DeleteGeneratedModelSource(_currentModel.ModelName);
                LoadModels();
                LoadParentModels();
                BtnAddModel_Click(null, EventArgs.Empty);
                MessageBox.Show("数据模型已删除。", "删除模型", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ConfigurationDeletionBlockedException ex)
            {
                MessageBox.Show(ex.Message, "无法删除模型", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除模型失败：" + ex.Message, "删除模型", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCopyModel_Click(object sender, EventArgs e)
        {
            if (_currentModelId <= 0 || _currentModel == null)
            {
                MessageBox.Show("请先选择要复制的数据模型。", "复制模型", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                ModelCopyResult copy = new ModelCopyService(DatabaseHelper.GetDatabasePath())
                    .CopyModel(_currentModelId);
                LoadModels();
                LoadParentModels();
                SelectModel(copy.ModelId);
                MessageBox.Show(
                    "已复制为“" + copy.ModelName + "”。副本默认停用，未复制解析规则和机台绑定，请调整并验证后再启用。",
                    "复制模型",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制模型失败：" + ex.Message, "复制模型", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectModel(int modelId)
        {
            int index = _models.FindIndex(model => model.Id == modelId);
            if (index >= 0) listBoxModels.SelectedIndex = index;
        }

        private void BtnDeleteScript_Click(object sender, EventArgs e)
        {
            if (_currentScriptId <= 0 || _currentScript == null)
            {
                MessageBox.Show("请先选择要删除的解析脚本。", "删除脚本", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(
                    "确定删除解析脚本“" + _currentScript.Name + "”吗？未发布脚本的机台关联和旧字段映射也会一并删除。",
                    "确认删除脚本",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                new ConfigurationDeletionService(DatabaseHelper.GetDatabasePath()).DeleteLegacyScript(_currentScriptId);
                ScriptEngine.ClearCache();
                LoadScripts();
                BtnAddScript_Click(null, EventArgs.Empty);
                MessageBox.Show("解析脚本已删除。", "删除脚本", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ConfigurationDeletionBlockedException ex)
            {
                MessageBox.Show(ex.Message, "无法删除脚本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除脚本失败：" + ex.Message, "删除脚本", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void DeleteGeneratedModelSource(string modelName)
        {
            if (!MappingRuleSerializer.IsIdentifier(modelName)) return;
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels"));
            string source = Path.GetFullPath(Path.Combine(directory, modelName + ".cs"));
            string prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(source))
                File.Delete(source);
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
            string targetModelType = ((ModelItem)cmbScriptModel.SelectedItem).Name;
            string fileExtension = cmbScriptFileType.SelectedItem?.ToString() ?? ".xlsx";

            try
            {
                var service = new LegacyScriptVersionService(DatabaseHelper.GetDatabasePath());
                LegacyScriptSaveResult saved = service.Save(new LegacyScriptSaveRequest
                {
                    LegacyScriptId = _currentScriptId > 0 ? _currentScriptId : 0,
                    Name = txtScriptName.Text,
                    ModelId = modelId,
                    TargetModelType = targetModelType,
                    FileExtension = fileExtension,
                    ScriptCode = rtxtScriptCode.Text,
                    IsEnabled = chkScriptEnabled.Checked,
                    MachineIds = GetSelectedMachineIds()
                });
                _currentScriptId = checked((int)saved.LegacyScriptId);
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
        private void LoadModelCombo(int? preferredModelId = null)
        {
            if (!preferredModelId.HasValue && cmbScriptModel.SelectedItem is ModelItem selectedModel)
                preferredModelId = selectedModel.Id;

            cmbScriptModel.Items.Clear();
            IReadOnlyList<ModelCatalogItem> models = new ModelCatalogService(DatabaseHelper.GetConnectionString())
                .LoadActiveModels();
            int selectedIndex = -1;
            for (int index = 0; index < models.Count; index++)
            {
                ModelCatalogItem model = models[index];
                cmbScriptModel.Items.Add(new ModelItem { Id = model.Id, Name = model.ModelName });
                if (preferredModelId == model.Id)
                {
                    selectedIndex = index;
                }
            }

            if (selectedIndex >= 0)
                cmbScriptModel.SelectedIndex = selectedIndex;
            else if (cmbScriptModel.Items.Count > 0)
                cmbScriptModel.SelectedIndex = 0;
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
            List<ModelSchemaField> proposedSchema = GetProposedModelSchema();
            if (!MappingRuleSerializer.IsIdentifier(_currentModel.ModelName) ||
                proposedSchema.Any(field => !MappingRuleSerializer.IsIdentifier(field.FieldName)) ||
                proposedSchema.GroupBy(field => field.FieldName, StringComparer.Ordinal).Any(group => group.Count() > 1))
            {
                MessageBox.Show("模型名和字段名必须是唯一、合法的 C# 标识符。", "验证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var schemaService = new ModelSchemaService(DatabaseHelper.GetConnectionString());

                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction(System.Data.IsolationLevel.Serializable))
                    {
                    // Published-binding protection must share the same immediate write
                    // transaction as the model update; otherwise another process could
                    // publish a rule between the check and the schema write.
                    bool hasPublishedRule = _currentModelId > 0 &&
                        schemaService.HasPublishedBinding(conn, transaction, _currentModelId);
                    bool publishedModelNameChanged = hasPublishedRule && !string.Equals(
                        schemaService.LoadModelName(conn, transaction, _currentModelId),
                        _currentModel.ModelName,
                        StringComparison.Ordinal);
                    bool publishedSchemaChanged = hasPublishedRule && ModelSchemaService.HasDestructiveChange(
                        schemaService.LoadFields(conn, transaction, _currentModelId),
                        proposedSchema);
                    if (publishedModelNameChanged || publishedSchemaChanged)
                    {
                        throw new PublishedModelSchemaChangeException(
                            "该模型存在已发布解析规则，不能直接修改模型名或任何会改变结构哈希的字段。请先新建模型并完成替代规则验证与切换。");
                    }

                    // 保存模型
                    if (_currentModelId == -1)
                    {
                        // 新增（包含 ParentModelId）
                        string sql = "INSERT INTO DataModels (ModelName, TableName, ParentModelId, Description, IsActive) VALUES (@ModelName, @TableName, @ParentModelId, @Description, @IsActive)";
                        using (var cmd = new SQLiteCommand(sql, conn))
                        {
                            cmd.Transaction = transaction;
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
                            cmd.Transaction = transaction;
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
                            cmd.Transaction = transaction;
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
                            cmd.Transaction = transaction;
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
                            cmd.Transaction = transaction;
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
                    schemaService.InvalidateUnpublishedValidation(
                        conn,
                        transaction,
                        _currentModelId,
                        ModelSchemaService.ComputeHash(proposedSchema));
                    transaction.Commit();
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
                LoadModelCombo(_currentModelId);
                if (_mappingDataLoaded)
                    LoadMappingModels(_currentModelId);

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
            catch (PublishedModelSchemaChangeException ex)
            {
                // The transaction and connection have already been disposed here, so
                // displaying a modal warning cannot hold SQLite's write lock.
                MessageBox.Show(ex.Message, "已阻止破坏性修改", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<ModelSchemaField> GetProposedModelSchema()
        {
            var fields = new List<ModelSchemaField>();
            foreach (DataGridViewRow row in dgvFields.Rows)
            {
                if (row.IsNewRow) continue;
                string fieldName = row.Cells["colFieldName"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(fieldName)) continue;
                fields.Add(new ModelSchemaField
                {
                    FieldName = fieldName.Trim(),
                    FieldType = row.Cells["colFieldType"].Value?.ToString() ?? "string",
                    FieldLength = Convert.ToInt32(row.Cells["colFieldLength"].Value ?? 0),
                    IsRequired = Convert.ToBoolean(row.Cells["colIsRequired"].Value ?? false),
                    IsPrimaryKey = Convert.ToBoolean(row.Cells["colIsPrimaryKey"].Value ?? false),
                    IsIdentity = Convert.ToBoolean(row.Cells["colIsIdentity"].Value ?? false),
                    Description = row.Cells["colDescription"].Value?.ToString() ?? string.Empty
                });
            }
            return fields;
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
        private void GenerateModelClass(ModelConfig model, List<ModelField> fields, bool showSuccessMessage = true)
        {
            TargetTableDefinition targetSchema = new TargetTableSchemaLoader(
                DatabaseHelper.GetConnectionString()).Load(model.Id);

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"namespace MachineDataAcquisitionSystem.Models");
            sb.AppendLine("{");
            sb.AppendLine($"    {ModelTableMapping.GetSqlSugarTableAttribute(targetSchema.TableName)}");
            sb.AppendLine($"    public class {model.ModelName}");
            sb.AppendLine("    {");

            foreach (TargetTableColumnDefinition field in targetSchema.Columns)
            {
                string propertyType = GetCSharpType(field.FieldType);
                if (!field.IsRequired && !field.IsPrimaryKey &&
                    !string.Equals(propertyType, "string", StringComparison.Ordinal))
                {
                    propertyType += "?";
                }
                sb.AppendLine();
                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {field.Description}");
                sb.AppendLine("        /// </summary>");
                sb.AppendLine("        " + ModelTableMapping.GetSqlSugarColumnAttribute(
                    field.FieldLength,
                    field.IsRequired,
                    field.IsPrimaryKey,
                    field.IsIdentity,
                    field.Description));
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

            if (showSuccessMessage)
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

        private sealed class PublishedModelSchemaChangeException : InvalidOperationException
        {
            public PublishedModelSchemaChangeException(string message) : base(message)
            {
            }
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

using System;
using System.Data.SQLite;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Helpers;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class BaseFieldConfigForm : Form
    {
        public BaseFieldConfigForm()
        {
            InitializeComponent();

            // 绑定事件
            this.Load += BaseFieldConfigForm_Load;
            btnAddField.Click += BtnAddField_Click;
            btnSave.Click += BtnSave_Click;
        }

        private void BaseFieldConfigForm_Load(object sender, EventArgs e)
        {
            // 设置列宽自动填充
            dgvBaseFields.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // 加载数据
            LoadBaseFields();
        }

        private void LoadBaseFields()
        {
            dgvBaseFields.Rows.Clear();

            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    string sql = "SELECT Id, FieldName, FieldType, DefaultValue, IsRequired, IsReadOnly, SortOrder, Description FROM BaseFields ORDER BY SortOrder";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int rowIndex = dgvBaseFields.Rows.Add();
                            dgvBaseFields.Rows[rowIndex].Tag = reader.GetInt32(0); // 保存Id
                            dgvBaseFields.Rows[rowIndex].Cells["colFieldName"].Value = reader.GetString(1);
                            dgvBaseFields.Rows[rowIndex].Cells["colFieldType"].Value = reader.GetString(2);
                            dgvBaseFields.Rows[rowIndex].Cells["colDefaultValue"].Value = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            dgvBaseFields.Rows[rowIndex].Cells["colIsRequired"].Value = reader.GetInt32(4) == 1;
                            dgvBaseFields.Rows[rowIndex].Cells["colIsReadOnly"].Value = reader.GetInt32(5) == 1;
                            dgvBaseFields.Rows[rowIndex].Cells["colSortOrder"].Value = reader.GetInt32(6);
                            dgvBaseFields.Rows[rowIndex].Cells["colDescription"].Value = reader.IsDBNull(7) ? "" : reader.GetString(7);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载基类字段失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnAddField_Click(object sender, EventArgs e)
        {
            dgvBaseFields.Rows.Add("新字段", "string", "", false, false, 0, "");
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();

                    // 先删除所有现有数据（简单处理，也可以逐条更新）
                    string deleteSql = "DELETE FROM BaseFields";
                    using (var cmd = new SQLiteCommand(deleteSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    // 重新插入所有数据
                    string insertSql = @"
                        INSERT INTO BaseFields (FieldName, FieldType, DefaultValue, IsRequired, IsReadOnly, SortOrder, Description)
                        VALUES (@FieldName, @FieldType, @DefaultValue, @IsRequired, @IsReadOnly, @SortOrder, @Description)";

                    foreach (DataGridViewRow row in dgvBaseFields.Rows)
                    {
                        if (row.IsNewRow) continue;

                        string fieldName = row.Cells["colFieldName"].Value?.ToString();
                        if (string.IsNullOrEmpty(fieldName)) continue;

                        using (var cmd = new SQLiteCommand(insertSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@FieldName", fieldName);
                            cmd.Parameters.AddWithValue("@FieldType", row.Cells["colFieldType"].Value?.ToString() ?? "string");
                            cmd.Parameters.AddWithValue("@DefaultValue", row.Cells["colDefaultValue"].Value?.ToString() ?? "");
                            cmd.Parameters.AddWithValue("@IsRequired", Convert.ToBoolean(row.Cells["colIsRequired"].Value ?? false) ? 1 : 0);
                            cmd.Parameters.AddWithValue("@IsReadOnly", Convert.ToBoolean(row.Cells["colIsReadOnly"].Value ?? false) ? 1 : 0);
                            cmd.Parameters.AddWithValue("@SortOrder", Convert.ToInt32(row.Cells["colSortOrder"].Value ?? 0));
                            cmd.Parameters.AddWithValue("@Description", row.Cells["colDescription"].Value?.ToString() ?? "");
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                MessageBox.Show("基类配置保存成功！\n请重启程序使配置生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
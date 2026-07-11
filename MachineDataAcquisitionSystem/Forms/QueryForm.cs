using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Helpers;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class QueryForm : Form
    {
        public QueryForm()
        {
            InitializeComponent();

            // 设置默认日期（最近7天）
            dtpEnd.Value = DateTime.Now;
            dtpStart.Value = DateTime.Now.AddDays(-7);

            // 绑定事件
            btnQuery.Click += BtnQuery_Click;
            btnReset.Click += BtnReset_Click;
            btnExport.Click += BtnExport_Click;

            // 绑定表格格式化事件
            dgvRecords.CellFormatting += DgvRecords_CellFormatting;

            // 加载数据
            LoadData();
        }

        /// <summary>
        /// 查询按钮
        /// </summary>
        private void BtnQuery_Click(object sender, EventArgs e)
        {
            LoadData();
        }

        /// <summary>
        /// 重置按钮
        /// </summary>
        private void BtnReset_Click(object sender, EventArgs e)
        {
            cmbMachine.SelectedIndex = 0;  // 全部
            cmbStatus.SelectedIndex = 0;   // 全部
            dtpStart.Value = DateTime.Now.AddDays(-7);
            dtpEnd.Value = DateTime.Now;

            LoadData();
        }

        /// <summary>
        /// 加载数据
        /// </summary>
        private void LoadData()
        {
            try
            {
                // 构建查询SQL
                string sql = @"
            SELECT Id, MachineId, FileName, Status, RecordCount, ProcessTime, Duration
            FROM FileProcessRecord 
            WHERE 1=1 ";

                // 机台筛选（修改这里）
                if (cmbMachine.SelectedItem != null && cmbMachine.SelectedItem.ToString() != "全部")
                {
                    string selected = cmbMachine.SelectedItem.ToString();
                    int machineId = int.Parse(selected.Replace("机台", ""));
                    sql += $" AND MachineId = {machineId} ";
                }

                // 状态筛选
                if (cmbStatus.SelectedIndex == 1)  // 成功
                {
                    sql += " AND Status = '成功' ";
                }
                else if (cmbStatus.SelectedIndex == 2)  // 失败
                {
                    sql += " AND Status = '失败' ";
                }

                // 日期筛选
                sql += $" AND ProcessTime >= '{dtpStart.Value:yyyy-MM-dd}' ";
                sql += $" AND ProcessTime <= '{dtpEnd.Value:yyyy-MM-dd 23:59:59}' ";

                // 排序
                sql += " ORDER BY ProcessTime DESC ";

                // 执行查询
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        using (var adapter = new SQLiteDataAdapter(cmd))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);

                            // 绑定数据
                            dgvRecords.Rows.Clear();

                            int total = 0, success = 0, fail = 0;
                            long totalDuration = 0;

                            for (int i = 0; i < dt.Rows.Count; i++)
                            {
                                DataRow row = dt.Rows[i];

                                int rowIndex = dgvRecords.Rows.Add();
                                dgvRecords.Rows[rowIndex].Cells["colId"].Value = i + 1;
                                dgvRecords.Rows[rowIndex].Cells["colMachine"].Value = $"机台{row["MachineId"]}";
                                dgvRecords.Rows[rowIndex].Cells["colFileName"].Value = row["FileName"];
                                dgvRecords.Rows[rowIndex].Cells["colStatus"].Value = row["Status"];
                                dgvRecords.Rows[rowIndex].Cells["colRecordCount"].Value = row["RecordCount"];
                                dgvRecords.Rows[rowIndex].Cells["colProcessTime"].Value = row["ProcessTime"];
                                dgvRecords.Rows[rowIndex].Cells["colDuration"].Value = row["Duration"];

                                // 统计
                                total++;
                                if (row["Status"].ToString() == "成功") success++;
                                else fail++;
                                totalDuration += Convert.ToInt64(row["Duration"]);
                            }

                            // 更新统计
                            lblTotal.Text = $"总记录: {total}";
                            lblSuccess.Text = $"成功: {success}";
                            lblFail.Text = $"失败: {fail}";

                            double rate = total > 0 ? (double)success / total * 100 : 0;
                            lblRate.Text = $"成功率: {rate:F1}%";

                            long avgTime = total > 0 ? totalDuration / total : 0;
                            lblAvgTime.Text = $"平均耗时: {avgTime} ms";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 导出Excel按钮
        /// </summary>
        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (dgvRecords.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = "导出Excel";
                saveDialog.Filter = "Excel文件 (*.xlsx)|*.xlsx|CSV文件 (*.csv)|*.csv";
                saveDialog.FileName = $"采集记录_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // 简单导出为CSV（兼容Excel）
                        string content = "";

                        // 添加表头
                        content += "序号,机台,文件名,状态,记录数,处理时间,耗时(ms)\n";

                        // 添加数据
                        foreach (DataGridViewRow row in dgvRecords.Rows)
                        {
                            content += $"{row.Cells["colId"].Value},";
                            content += $"{row.Cells["colMachine"].Value},";
                            content += $"{row.Cells["colFileName"].Value},";
                            content += $"{row.Cells["colStatus"].Value},";
                            content += $"{row.Cells["colRecordCount"].Value},";
                            content += $"{row.Cells["colProcessTime"].Value},";
                            content += $"{row.Cells["colDuration"].Value}\n";
                        }

                        System.IO.File.WriteAllText(saveDialog.FileName, content);
                        MessageBox.Show($"导出成功！\n{saveDialog.FileName}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// 表格格式化（设置状态颜色）
        /// </summary>
        private void DgvRecords_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // 状态列
            if (dgvRecords.Columns[e.ColumnIndex].Name == "colStatus" && e.Value != null)
            {
                string status = e.Value.ToString();

                if (status == "成功")
                {
                    e.CellStyle.ForeColor = Color.Green;
                    e.CellStyle.Font = new Font(dgvRecords.Font, FontStyle.Bold);
                }
                else if (status == "失败")
                {
                    e.CellStyle.ForeColor = Color.Red;
                    e.CellStyle.Font = new Font(dgvRecords.Font, FontStyle.Bold);
                }
            }

            // 耗时列：超过200ms显示橙色
            if (dgvRecords.Columns[e.ColumnIndex].Name == "colDuration" && e.Value != null)
            {
                if (Convert.ToInt32(e.Value) > 200)
                {
                    e.CellStyle.ForeColor = Color.Orange;
                }
            }
        }
    }
}
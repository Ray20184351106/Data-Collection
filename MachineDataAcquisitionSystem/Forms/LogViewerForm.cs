using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MachineDataAcquisitionSystem.Forms
{
    public partial class LogViewerForm : Form
    {

        // ========== 添加这些字段 ==========
        private RichTextBox _txtLog;
        private DateTimePicker _dtpDate;
        private TextBox _txtSearch;
        private Button _btnSearch;
        private Button _btnRefresh;
        private Button _btnExport;
        private Button _btnClose;
        private Label _lblStatus;
        private string _logDirectory;
        // =================================
        public LogViewerForm()
        {
            InitializeComponent();

            // 设置窗体属性
            this.Text = "日志查看器";
            this.Size = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(800, 400);

            // 初始化日志目录
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            // 创建界面控件
            CreateControls();

            // 加载今天的日志
            LoadLog();
        }


        private void CreateControls()
        {
            // 顶部工具栏
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 55,
                BackColor = Color.FromArgb(245, 245, 245)
            };

            int currentX = 15;  // 起始X坐标

            // 1. 日期标签
            Label lblDate = new Label
            {
                Text = "日期：",
                Location = new Point(currentX, 15),
                Size = new Size(45, 28),
                Font = new Font("微软雅黑", 10F)
            };
            topPanel.Controls.Add(lblDate);
            currentX += 50;

            // 2. 日期选择器
            _dtpDate = new DateTimePicker
            {
                Location = new Point(currentX, 12),
                Size = new Size(130, 28),
                Format = DateTimePickerFormat.Short,
                Font = new Font("微软雅黑", 10F)
            };
            _dtpDate.ValueChanged += (s, e) => LoadLog();
            topPanel.Controls.Add(_dtpDate);
            currentX += 150;  // 日期选择器宽度 + 间距

            // 3. 间距（加大间距）
            currentX += 25;  // 增加25像素间距

            // 4. 搜索标签
            Label lblSearch = new Label
            {
                Text = "搜索：",
                Location = new Point(currentX, 15),
                Size = new Size(45, 28),
                Font = new Font("微软雅黑", 10F)
            };
            topPanel.Controls.Add(lblSearch);
            currentX += 50;

            // 5. 搜索框
            _txtSearch = new TextBox
            {
                Location = new Point(currentX, 12),
                Size = new Size(180, 28),
                Font = new Font("微软雅黑", 10F)
            };
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) SearchLog(); };
            topPanel.Controls.Add(_txtSearch);
            currentX += 190;  // 搜索框宽度 + 间距

            // 6. 间距
            currentX += 20;

            // 7. 查找按钮
            _btnSearch = new Button
            {
                Text = "查找",
                Location = new Point(currentX, 11),
                Size = new Size(70, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            _btnSearch.Click += (s, e) => SearchLog();
            topPanel.Controls.Add(_btnSearch);
            currentX += 80;

            // 8. 刷新按钮
            _btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(currentX, 11),
                Size = new Size(70, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            _btnRefresh.Click += (s, e) => LoadLog();
            topPanel.Controls.Add(_btnRefresh);
            currentX += 80;

            // 9. 导出按钮
            _btnExport = new Button
            {
                Text = "导出",
                Location = new Point(currentX, 11),
                Size = new Size(70, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            _btnExport.Click += (s, e) => ExportLog();
            topPanel.Controls.Add(_btnExport);
            currentX += 80;

            // 10. 关闭按钮
            _btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(currentX, 11),
                Size = new Size(70, 30),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.FromArgb(240, 240, 240)
            };
            _btnClose.Click += (s, e) => this.Close();
            topPanel.Controls.Add(_btnClose);

            // 日志显示区域
            _txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 10F),
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.ForcedBoth
            };

            // 底部状态栏
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = Color.FromArgb(245, 245, 245)
            };

            _lblStatus = new Label
            {
                Text = "就绪",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0),
                Font = new Font("微软雅黑", 9F),
                ForeColor = Color.Gray
            };
            bottomPanel.Controls.Add(_lblStatus);

            this.Controls.Add(_txtLog);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(topPanel);
        }
        private void LoadLog()
        {
            string selectedDate = _dtpDate.Value.ToString("yyyy-MM-dd");
            string logPath = Path.Combine(_logDirectory, $"{selectedDate}.log");

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                    _txtLog.Text = "暂无日志记录";
                    _lblStatus.Text = "日志目录不存在，已自动创建";
                    return;
                }

                if (!File.Exists(logPath))
                {
                    _txtLog.Text = $"暂无 {selectedDate} 的日志记录";
                    _lblStatus.Text = $"没有找到 {selectedDate} 的日志文件";
                    return;
                }

                string content = File.ReadAllText(logPath);
                _txtLog.Text = content;

                int lineCount = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
                _lblStatus.Text = $"共 {lineCount} 行日志 | 文件：{selectedDate}.log";

                _txtLog.SelectionStart = _txtLog.Text.Length;
                _txtLog.ScrollToCaret();
            }
            catch (Exception ex)
            {
                _txtLog.Text = $"读取日志失败：{ex.Message}";
                _lblStatus.Text = "读取失败";
            }
        }
        private void SearchLog()
        {
            string keyword = _txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                MessageBox.Show("请输入搜索关键词", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadLog();

            int index = 0;
            int count = 0;

            while (index < _txtLog.TextLength)
            {
                int findIndex = _txtLog.Find(keyword, index, RichTextBoxFinds.None);
                if (findIndex == -1) break;

                _txtLog.SelectionStart = findIndex;
                _txtLog.SelectionLength = keyword.Length;
                _txtLog.SelectionBackColor = Color.Yellow;
                _txtLog.SelectionColor = Color.Black;

                index = findIndex + keyword.Length;
                count++;
            }

            _lblStatus.Text = $"找到 {count} 处匹配";

            if (count == 0)
            {
                MessageBox.Show($"未找到关键词：{keyword}", "搜索", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _txtLog.SelectionStart = 0;
                _txtLog.ScrollToCaret();
            }
        }
        private void ExportLog()
        {
            string selectedDate = _dtpDate.Value.ToString("yyyy-MM-dd");
            string logPath = Path.Combine(_logDirectory, $"{selectedDate}.log");

            if (!File.Exists(logPath))
            {
                MessageBox.Show($"没有 {selectedDate} 的日志可导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = "导出日志";
                saveDialog.Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log";
                saveDialog.FileName = $"日志_{selectedDate}_{DateTime.Now:HHmmss}.txt";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.Copy(logPath, saveDialog.FileName, true);
                        MessageBox.Show($"导出成功！\n{saveDialog.FileName}", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _lblStatus.Text = $"已导出到：{saveDialog.FileName}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}

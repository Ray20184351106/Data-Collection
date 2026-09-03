namespace Acquisition.Agent.Setup;

public sealed class SetupForm : Form
{
    private readonly InstallationPlan _plan;
    private readonly InstallationExecutor _executor;
    private readonly Button _installButton;
    private readonly Button _closeButton;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progress;

    public SetupForm(InstallationPlan plan, InstallationExecutor executor)
    {
        _plan = plan;
        _executor = executor;

        Text = "文件数据采集 Agent 安装";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 620);
        Size = new Size(820, 690);
        Font = new Font("Microsoft YaHei UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "设备专用 Agent 安装"
        });
        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            Margin = new Padding(0, 10, 0, 12),
            ForeColor = Color.DimGray,
            Text = "请核对以下最终生效路径。安装器只安装 Agent 服务，不会删除或替换现有 WinForms 采集程序。"
        });

        var summary = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = string.Join(Environment.NewLine, InstallationSummaryBuilder.Build(plan.Deployment)) +
                   Environment.NewLine + $"Agent安装目录：{plan.VersionDirectory}" +
                   Environment.NewLine + $"共享Agent数据库：{plan.AgentDatabasePath}"
        };
        root.Controls.Add(summary);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 6),
            ForeColor = Color.DarkOrange,
            Text = "安装需要管理员权限。若安装失败，已复制文件将保留供诊断，不会递归删除目录。"
        });

        _progress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 18,
            Style = ProgressBarStyle.Blocks
        };
        root.Controls.Add(_progress);

        _statusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 10),
            Text = "已完成安装包与路径校验，等待开始安装。"
        };
        root.Controls.Add(_statusLabel);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        _installButton = new Button { AutoSize = true, Text = "安装并启动 Agent", Padding = new Padding(14, 5, 14, 5) };
        _closeButton = new Button { AutoSize = true, Text = "取消", Padding = new Padding(14, 5, 14, 5) };
        _installButton.Click += InstallButton_Click;
        _closeButton.Click += (_, _) => Close();
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_closeButton);
        root.Controls.Add(buttons);

        Controls.Add(root);
        AcceptButton = _installButton;
        CancelButton = _closeButton;
    }

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        _installButton.Enabled = false;
        _closeButton.Enabled = false;
        _progress.Style = ProgressBarStyle.Marquee;
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = "正在复制明确的 payload 文件、设置共享数据库并创建 Windows 服务…";
        try
        {
            var result = await _executor.ExecuteAsync(_plan, CancellationToken.None);
            _statusLabel.Text = result.Message + (result.Succeeded ? "" : $" 诊断：{result.DiagnosticLogPath}");
            _statusLabel.ForeColor = result.Succeeded ? Color.DarkGreen : Color.Firebrick;
            if (result.Succeeded)
            {
                _installButton.Text = "安装完成";
                _closeButton.Text = "关闭";
            }
            else
            {
                _installButton.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            var message = ex.Message.Replace(_plan.Deployment.EnrollmentToken, "[REDACTED]", StringComparison.Ordinal);
            _statusLabel.Text = "安装未完成：" + message;
            _statusLabel.ForeColor = Color.Firebrick;
            _installButton.Enabled = true;
        }
        finally
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _closeButton.Enabled = true;
        }
    }
}

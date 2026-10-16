using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Acquisition.Updater
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--wait-for-pid")
                {
                    try
                    {
                        using (var previous = Process.GetProcessById(int.Parse(args[1]))) previous.WaitForExit();
                    }
                    catch (ArgumentException) { /* 旧进程已经退出。 */ }
                }
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                string applicationPath = Path.Combine(directory, UpdateService.ApplicationFileName);
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(applicationPath)))
                    using (process)
                    {
                        string runningPath;
                        try { runningPath = process.MainModule.FileName; }
                        catch (System.ComponentModel.Win32Exception) { continue; }
                        catch (InvalidOperationException) { continue; }
                        if (string.Equals(runningPath, applicationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("采集程序正在运行。请先从托盘退出程序，再通过更新器启动。", "文件采集更新");
                            return;
                        }
                    }
                string settingsPath = Path.Combine(directory, "updater.json");
                if (!File.Exists(settingsPath))
                    throw new FileNotFoundException("尚未配置共享目录，请先运行 Install-WinFormsUpdater.ps1 接入脚本。", settingsPath);
                var settings = UpdateService.ReadJson<UpdaterSettings>(settingsPath);
                Directory.CreateDirectory(Path.Combine(directory, "UpdateCache"));
                // 本地文件占用覆盖整个更新和启动过程，多个启动入口不会并行写同一缓存。
                using (File.Open(Path.Combine(directory, "UpdateCache", "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                using (var window = new UpdateWindow(directory, settings.SourceDirectory)) Application.Run(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "文件采集更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class UpdateWindow : Form
    {
        public UpdateWindow(string directory, string source)
        {
            Text = "文件采集更新";
            ClientSize = new Size(440, 100);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            var label = new Label { Text = "正在检查共享目录版本，请稍候……", AutoSize = false, Location = new Point(18, 16), Size = new Size(404, 30) };
            Controls.Add(label);
            Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, Location = new Point(18, 57), Size = new Size(404, 18) });
            Shown += async (sender, args) =>
            {
                try
                {
                    var result = await Task.Run(() => new UpdateService(directory, source).ApplyLatest());
                    label.Text = result.Message;
                    Process.Start(new ProcessStartInfo(Path.Combine(directory, UpdateService.ApplicationFileName)) { WorkingDirectory = directory });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message + "\r\n请确认问题后重新启动更新器。", "更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { Close(); }
            };
        }
    }
}

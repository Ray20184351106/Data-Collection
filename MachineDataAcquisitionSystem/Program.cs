using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Forms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yitter.IdGenerator;

namespace MachineDataAcquisitionSystem
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {

            // 确保只初始化一次
            var options = new IdGeneratorOptions(1);
            YitIdHelper.SetIdGenerator(options);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                DatabaseHelper.Initialize();
            }
            catch (LegacyMigrationConflictException ex) when (
                ex.Report != null &&
                ex.Report.Conflicts.Count > 0 &&
                !ex.Report.Issues.Any(issue => issue.Severity == LegacyMigrationIssueSeverity.Blocking))
            {
                using (var dialog = new LegacyMigrationConflictDialog(ex.Report))
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return;
                    try
                    {
                        DatabaseHelper.Initialize(dialog.Choices);
                    }
                    catch (Exception retryException)
                    {
                        MessageBox.Show(
                            "解析规则迁移失败，数据库未切换到新绑定：" + retryException.Message,
                            "启动已停止",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "数据库安全检查或迁移失败，采集程序未启动：" + ex.Message,
                    "启动已停止",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            Application.Run(new Form1());
        }
    }
}

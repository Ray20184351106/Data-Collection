using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ConfigFormAiMappingActionsTests
    {
        [Fact]
        public void Advanced_configuration_exposes_save_and_connection_test_actions_for_ai_mapping()
        {
            string[] buttonTexts = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ConfigForm())
                    {
                        TabControl tabs = form.Controls.OfType<TabControl>().Single();
                        TabPage advanced = tabs.TabPages.Cast<TabPage>()
                            .Single(page => page.Text == "高级配置");
                        buttonTexts = Descendants(advanced)
                            .OfType<Button>()
                            .Select(button => button.Text)
                            .ToArray();
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.Contains("保存 AI 配置", buttonTexts);
            Assert.Contains("测试 AI 连接", buttonTexts);
        }

        private static Control[] Descendants(Control root)
        {
            return root.Controls.Cast<Control>()
                .SelectMany(control => new[] { control }.Concat(Descendants(control)))
                .ToArray();
        }
    }
}

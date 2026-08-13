using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ModelContextMenuTests
    {
        [Fact]
        public void Model_list_exposes_copy_and_delete_actions_in_its_context_menu()
        {
            string[] menuTexts = null;
            bool attachedToModelList = false;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var list = Assert.IsType<ListBox>(
                            typeof(ModelConfigForm).GetField("listBoxModels", Flags).GetValue(form));
                        ContextMenuStrip menu = list.ContextMenuStrip;
                        attachedToModelList = menu != null;
                        menuTexts = menu.Items.Cast<ToolStripItem>()
                            .Select(item => item.Text)
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
            Assert.True(attachedToModelList);
            Assert.Contains("复制模型", menuTexts);
            Assert.Contains("删除模型", menuTexts);
        }
    }
}

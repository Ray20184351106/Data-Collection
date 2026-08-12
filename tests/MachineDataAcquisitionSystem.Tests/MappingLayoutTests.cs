using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingLayoutTests
    {
        [Fact]
        public void Mapping_header_reserves_two_lines_for_machine_choices()
        {
            float machineRowHeight = 0;
            int headerHeight = 0;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        var headerField = typeof(ModelConfigForm).GetField(
                            "panel2",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        var header = Assert.IsType<Panel>(headerField.GetValue(form));
                        var layout = Assert.IsType<TableLayoutPanel>(header.Controls[0]);

                        machineRowHeight = layout.RowStyles[3].Height;
                        headerHeight = header.Height;
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
            Assert.True(machineRowHeight >= 64F, "适用机台区域应至少容纳两行复选框。");
            Assert.True(headerHeight >= 170, "顶部配置区应完整包含适用机台区域。");
        }

        [Fact]
        public void Mapping_sample_area_is_between_header_and_footer()
        {
            int headerBottom = 0;
            int sampleTop = 0;
            int sampleBottom = 0;
            int footerTop = 0;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var header = Assert.IsType<Panel>(
                            typeof(ModelConfigForm).GetField("panel2", Flags).GetValue(form));
                        var footer = Assert.IsType<Panel>(
                            typeof(ModelConfigForm).GetField("panel3", Flags).GetValue(form));
                        var sampleArea = Assert.IsType<SplitContainer>(
                            typeof(ModelConfigForm).GetField("_mappingCenterSplit", Flags).GetValue(form));
                        var editorPanel = Assert.IsType<SplitContainer>(
                            typeof(ModelConfigForm).GetField("splitContainer3", Flags).GetValue(form)).Panel2;

                        editorPanel.PerformLayout();
                        headerBottom = header.Bottom;
                        sampleTop = sampleArea.Top;
                        sampleBottom = sampleArea.Bottom;
                        footerTop = footer.Top;
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
            Assert.True(sampleTop >= headerBottom, "样本区域不得被顶部配置区遮挡。");
            Assert.True(sampleBottom <= footerTop, "样本区域不得进入底部保存栏。");
        }
    }
}

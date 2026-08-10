using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingFooterTests
    {
        [Fact]
        public void Mapping_footer_exposes_only_one_save_action()
        {
            string[] actionTexts = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        var footerField = typeof(ModelConfigForm).GetField(
                            "panel3",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        var footer = Assert.IsType<Panel>(footerField.GetValue(form));
                        actionTexts = footer.Controls
                            .Cast<Control>()
                            .SelectMany(control => control.Controls.Cast<Control>())
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
            Assert.Equal(new[] { "保存" }, actionTexts);
        }
    }
}

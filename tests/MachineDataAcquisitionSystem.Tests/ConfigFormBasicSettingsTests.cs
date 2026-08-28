using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ConfigFormBasicSettingsTests
    {
        [Fact]
        public void Constructor_places_basic_settings_before_the_existing_configuration_tabs()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ConfigForm())
                    {
                        TabControl tabs = form.Controls.OfType<TabControl>().Single();

                        Assert.Equal(6, tabs.TabPages.Count);
                        Assert.Equal("基础配置", tabs.TabPages[0].Text);
                        Assert.Equal("基础配置", tabs.SelectedTab.Text);
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

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Forms;
using MachineDataAcquisitionSystem.Models;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ConfigFormBasicSettingsTests
    {
        [Fact]
        public void Machine_selection_loads_by_id_and_edits_stay_local_until_saved()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ConfigForm())
                    {
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var settings = new AppSettings { AutoStartMachineIds = new List<int> { 5 } };
                        typeof(ConfigForm).GetField("_appSettings", flags).SetValue(form, settings);
                        typeof(ConfigForm).GetField("_machineConfigs", flags).SetValue(form, new List<MachineConfig>
                        {
                            new MachineConfig { Id = 5, Name = "AOI 五号" },
                            new MachineConfig { Id = 2, Name = "检测二号" },
                            new MachineConfig { Id = 8, Name = "不支持的机台" }
                        });
                        typeof(ConfigForm).GetMethod("LoadBasicSettings", flags).Invoke(form, null);
                        var machines = (CheckedListBox)form.Controls.Find("machineAutoStartList", true).Single();

                        Assert.Equal(new[] { 2, 5 }, machines.Items.Cast<MachineConfig>().Select(machine => machine.Id));
                        Assert.Contains("AOI 五号", machines.GetItemText(machines.Items[1]));
                        Assert.Equal(new[] { 5 }, machines.CheckedItems.Cast<MachineConfig>().Select(machine => machine.Id));
                        Assert.False((bool)typeof(ConfigForm).GetField("_basicConfigurationHasUnsavedEdits", flags).GetValue(form));

                        machines.SetItemChecked(0, true);
                        machines.SetItemChecked(1, false);

                        Assert.True((bool)typeof(ConfigForm).GetField("_basicConfigurationHasUnsavedEdits", flags).GetValue(form));
                        Assert.Equal(new[] { 2 }, machines.CheckedItems.Cast<MachineConfig>().Select(machine => machine.Id));
                        Assert.Equal(new[] { 5 }, settings.AutoStartMachineIds);
                        Assert.False(settings.AutoStart);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "配置界面测试超时。");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

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
                        CheckedListBox machines = Assert.IsType<CheckedListBox>(
                            Assert.Single(tabs.TabPages[0].Controls.Find("machineAutoStartList", true)));
                        Assert.True(machines.CheckOnClick);
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

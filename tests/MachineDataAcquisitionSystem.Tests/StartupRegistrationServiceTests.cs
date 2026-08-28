using System;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class StartupRegistrationServiceTests
    {
        [Fact]
        public void SetEnabled_writes_a_quoted_executable_path_for_the_current_application()
        {
            var store = new RecordingStartupRegistrationStore();
            var service = new StartupRegistrationService(store, "文件数据采集系统");

            service.SetEnabled(true, @"C:\Program Files\采集系统\文件数据采集系统.exe");

            Assert.Equal("文件数据采集系统", store.SetName);
            Assert.Equal("\"C:\\Program Files\\采集系统\\文件数据采集系统.exe\"", store.WrittenValue);
            Assert.Null(store.DeletedName);
        }

        [Fact]
        public void SetEnabled_removes_only_the_current_application_value_when_disabled()
        {
            var store = new RecordingStartupRegistrationStore();
            var service = new StartupRegistrationService(store, "文件数据采集系统");

            service.SetEnabled(false, @"C:\Apps\文件数据采集系统.exe");

            Assert.Equal("文件数据采集系统", store.DeletedName);
            Assert.Null(store.SetName);
        }

        [Fact]
        public void SetEnabled_rejects_an_empty_executable_path_when_enabling()
        {
            var service = new StartupRegistrationService(
                new RecordingStartupRegistrationStore(),
                "文件数据采集系统");

            ArgumentException error = Assert.Throws<ArgumentException>(() => service.SetEnabled(true, "  "));

            Assert.Contains("程序路径", error.Message);
        }

        private sealed class RecordingStartupRegistrationStore : IStartupRegistrationStore
        {
            public string SetName { get; private set; }
            public string WrittenValue { get; private set; }
            public string DeletedName { get; private set; }

            public void SetValue(string name, string value)
            {
                SetName = name;
                WrittenValue = value;
            }

            public void DeleteValue(string name)
            {
                DeletedName = name;
            }
        }
    }
}

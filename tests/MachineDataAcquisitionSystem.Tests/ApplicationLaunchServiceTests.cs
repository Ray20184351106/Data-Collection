using System;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ApplicationLaunchServiceTests
    {
        [Fact]
        public void Existing_deployments_without_updater_keep_the_original_startup_path()
        {
            string application = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "文件数据采集系统.exe");
            Assert.Equal(application, ApplicationLaunchService.GetStartupExecutablePath(application));
        }

        [Fact]
        public void Startup_and_restart_use_updater_and_wait_for_the_previous_process()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AcquisitionLaunchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string application = Path.Combine(directory, "文件数据采集系统.exe");
            string updater = Path.Combine(directory, "Updater.exe");
            File.WriteAllText(updater, "test-updater");
            try
            {
                Assert.Equal(updater, ApplicationLaunchService.GetStartupExecutablePath(application));
                var start = ApplicationLaunchService.CreateRestartStartInfo(application, 1234);
                Assert.Equal(updater, start.FileName);
                Assert.Equal("--wait-for-pid 1234", start.Arguments);
                Assert.Equal(directory, start.WorkingDirectory);
            }
            finally { File.Delete(updater); }
        }

        [Fact]
        public void Restart_without_updater_keeps_existing_direct_launch_behavior()
        {
            string application = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "文件数据采集系统.exe");
            var start = ApplicationLaunchService.CreateRestartStartInfo(application, 1234);
            Assert.Equal(application, start.FileName);
            Assert.Equal(string.Empty, start.Arguments);
        }
    }
}

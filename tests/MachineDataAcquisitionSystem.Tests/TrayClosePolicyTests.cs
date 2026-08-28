using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class TrayClosePolicyTests
    {
        [Fact]
        public void ShouldHide_returns_true_for_the_close_button_when_exit_was_not_requested()
        {
            Assert.True(TrayClosePolicy.ShouldHide(CloseReason.UserClosing, false));
        }

        [Fact]
        public void ShouldHide_returns_false_after_the_tray_exit_command()
        {
            Assert.False(TrayClosePolicy.ShouldHide(CloseReason.UserClosing, true));
        }

        [Theory]
        [InlineData(CloseReason.WindowsShutDown)]
        [InlineData(CloseReason.ApplicationExitCall)]
        [InlineData(CloseReason.TaskManagerClosing)]
        public void ShouldHide_does_not_intercept_system_or_application_shutdown(CloseReason closeReason)
        {
            Assert.False(TrayClosePolicy.ShouldHide(closeReason, false));
        }
    }
}

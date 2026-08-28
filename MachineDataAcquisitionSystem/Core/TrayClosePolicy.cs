using System.ComponentModel;
using System.Windows.Forms;

namespace MachineDataAcquisitionSystem.Core
{
    internal static class TrayClosePolicy
    {
        public static bool ShouldHide(CloseReason closeReason, bool exitRequested)
        {
            return closeReason == CloseReason.UserClosing && !exitRequested;
        }
    }

    internal static class TrayComponentContainer
    {
        public static IContainer Ensure(IContainer container)
        {
            return container ?? new Container();
        }
    }
}

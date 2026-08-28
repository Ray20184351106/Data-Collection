using System.ComponentModel;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class TrayComponentContainerTests
    {
        [Fact]
        public void Ensure_creates_a_usable_container_when_the_designer_components_field_is_null()
        {
            using (IContainer container = TrayComponentContainer.Ensure(null))
            {
                var menu = new ContextMenuStrip(container);

                Assert.NotNull(menu);
            }
        }
    }
}

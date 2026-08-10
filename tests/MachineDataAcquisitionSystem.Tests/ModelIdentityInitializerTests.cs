using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ModelIdentityInitializerTests
    {
        [Fact]
        public void EnsureCid_assigns_a_generated_value_when_the_model_CID_is_zero()
        {
            var model = new ModelWithCid();

            ModelIdentityInitializer.EnsureCid(model, () => 123456789L);

            Assert.Equal(123456789L, model.CID);
        }

        [Fact]
        public void EnsureCid_preserves_a_model_CID_that_was_already_assigned()
        {
            var model = new ModelWithCid { CID = 42L };

            ModelIdentityInitializer.EnsureCid(model, () => 123456789L);

            Assert.Equal(42L, model.CID);
        }

        private sealed class ModelWithCid
        {
            public long CID { get; set; }
        }
    }
}

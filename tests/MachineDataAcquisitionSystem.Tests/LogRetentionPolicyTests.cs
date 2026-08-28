using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class LogRetentionPolicyTests
    {
        [Theory]
        [InlineData(100, 200, 0)]
        [InlineData(200, 200, 0)]
        [InlineData(250, 200, 50)]
        [InlineData(1000, 200, 800)]
        public void Calculates_the_oldest_characters_to_remove(
            int currentLength,
            int maximumLength,
            int expectedRemoval)
        {
            Assert.Equal(
                expectedRemoval,
                LogRetentionPolicy.GetRemovalLength(currentLength, maximumLength));
        }
    }
}

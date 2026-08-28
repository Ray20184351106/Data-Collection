using System;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class DatabaseRetryPolicyTests
    {
        [Theory]
        [InlineData(1, 5)]
        [InlineData(2, 15)]
        [InlineData(3, 30)]
        [InlineData(4, 60)]
        [InlineData(20, 60)]
        public void Retry_delay_uses_a_bounded_backoff(int attempt, int expectedSeconds)
        {
            Assert.Equal(
                TimeSpan.FromSeconds(expectedSeconds),
                DatabaseRetryPolicy.GetDelay(attempt));
        }

        [Fact]
        public void Timeout_is_transient_but_schema_errors_are_permanent()
        {
            Assert.True(DatabaseRetryPolicy.IsTransient(new TimeoutException("连接超时")));
            Assert.False(DatabaseRetryPolicy.IsTransient(new InvalidOperationException("列名 'MissingColumn' 无效。")));
            Assert.False(DatabaseRetryPolicy.IsTransient(new InvalidOperationException("不能将值 NULL 插入列 'CID'。")));
        }
    }
}

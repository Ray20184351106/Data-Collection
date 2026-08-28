using System;
using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class UploadCommitSignalTests
    {
        [Fact]
        public async Task Upload_does_not_complete_before_the_database_commit_signal()
        {
            var signal = new UploadCommitSignal();

            Assert.False(signal.Completion.IsCompleted);
            signal.MarkCommitted();

            await signal.WaitAsync(CancellationToken.None);
            Assert.Equal(TaskStatus.RanToCompletion, signal.Completion.Status);
        }

        [Fact]
        public async Task Permanent_database_failure_is_propagated_to_the_file_flow()
        {
            var signal = new UploadCommitSignal();
            signal.MarkPermanentFailure(new InvalidOperationException("列名无效"));

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                signal.WaitAsync(CancellationToken.None));

            Assert.Contains("列名无效", error.Message);
        }
    }
}

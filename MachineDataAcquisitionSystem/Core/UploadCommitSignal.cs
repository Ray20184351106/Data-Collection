using System;
using System.Threading;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Core
{
    public sealed class UploadCommitSignal
    {
        private readonly TaskCompletionSource<bool> _completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public void MarkCommitted()
        {
            _completion.TrySetResult(true);
        }

        public void MarkPermanentFailure(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            _completion.TrySetException(exception);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cancellationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellationCompletion.TrySetCanceled()))
            {
                Task completed = await Task.WhenAny(
                    _completion.Task,
                    cancellationCompletion.Task).ConfigureAwait(false);
                if (completed == cancellationCompletion.Task)
                    cancellationToken.ThrowIfCancellationRequested();
                await _completion.Task.ConfigureAwait(false);
            }
        }
    }
}

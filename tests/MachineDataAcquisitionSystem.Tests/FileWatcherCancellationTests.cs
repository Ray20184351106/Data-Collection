using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Parser.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public class FileWatcherCancellationTests
    {
        [Fact]
        public async Task StopAsync_cancels_in_flight_handler_and_waits_for_it_to_exit()
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                "FileWatcherCancellationTests_" + Guid.NewGuid().ToString("N"));
            string monitorPath = Path.Combine(rootPath, "Incoming");
            string successPath = Path.Combine(rootPath, "Success");
            string errorPath = Path.Combine(rootPath, "Error");
            string inputPath = Path.Combine(monitorPath, "sample.txt");
            var handlerStarted = new TaskCompletionSource<bool>();
            var handlerExited = new TaskCompletionSource<bool>();
            int sideEffectCount = 0;

            var watcher = new FileWatcher(monitorPath, successPath, errorPath);
            watcher.OnFileCreated += async (filePath, cancellationToken) =>
            {
                handlerStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    Interlocked.Increment(ref sideEffectCount);
                }
                catch (OperationCanceledException)
                {
                    handlerExited.TrySetResult(true);
                    throw;
                }
            };

            try
            {
                watcher.Start();
                File.WriteAllText(inputPath, "test");
                await WaitWithTimeout(handlerStarted.Task, TimeSpan.FromSeconds(5));

                await watcher.StopAsync();

                Assert.True(handlerExited.Task.IsCompleted);
                Assert.Equal(0, Volatile.Read(ref sideEffectCount));
            }
            finally
            {
                watcher.Dispose();
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (Directory.Exists(monitorPath)) Directory.Delete(monitorPath);
                if (Directory.Exists(successPath)) Directory.Delete(successPath);
                if (Directory.Exists(errorPath)) Directory.Delete(errorPath);
                if (Directory.Exists(rootPath)) Directory.Delete(rootPath);
            }
        }

        private static async Task WaitWithTimeout(Task task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            Assert.Same(task, completed);
            await task;
        }
    }
}

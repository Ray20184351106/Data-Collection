// Core/FileWatcher.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace MachineDataAcquisitionSystem.Core.Parser
.Core
{
    public class FileWatcher : IDisposable
    {
        private readonly object _stateLock = new object();
        private readonly HashSet<Task> _activeTasks = new HashSet<Task>();
        private FileSystemWatcher _watcher;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly string _monitorPath;
        private readonly string _successPath;
        private readonly string _errorPath;
        private bool _isRunning;

        // 当有新文件时触发
        public event Func<string, CancellationToken, Task> OnFileCreated;
        public event Action<Exception> OnError;

        public FileWatcher(string monitorPath, string successPath, string errorPath)
        {
            _monitorPath = monitorPath;
            _successPath = successPath;
            _errorPath = errorPath;

            // 确保目录存在
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_monitorPath);
            Directory.CreateDirectory(_successPath);
            Directory.CreateDirectory(_errorPath);
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                _watcher = new FileSystemWatcher
                {
                    Path = _monitorPath,
                    Filter = "*.*",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                _watcher.Created += OnFileCreatedHandler;
                _watcher.Error += OnWatcherErrorHandler;
                _isRunning = true;
                _watcher.EnableRaisingEvents = true;
            }

            ProcessExistingFiles();
        }

        public void Stop()
        {
            BeginStop();
        }

        public async Task StopAsync()
        {
            Task[] activeTasks = BeginStop();
            if (activeTasks.Length == 0) return;

            try
            {
                await Task.WhenAll(activeTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 停止时取消在途任务属于预期行为。
            }
        }

        private Task[] BeginStop()
        {
            lock (_stateLock)
            {
                if (_isRunning)
                {
                    _isRunning = false;

                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = false;
                        _watcher.Created -= OnFileCreatedHandler;
                        _watcher.Error -= OnWatcherErrorHandler;
                        _watcher.Dispose();
                        _watcher = null;
                    }

                    _cancellationTokenSource?.Cancel();
                }

                return _activeTasks.ToArray();
            }
        }

        private void OnFileCreatedHandler(object sender, FileSystemEventArgs e)
        {
            QueueFile(e.FullPath);
        }

        private void OnWatcherErrorHandler(object sender, ErrorEventArgs e)
        {
            OnError?.Invoke(e.GetException());
        }

        private void QueueFile(string filePath)
        {
            Task processingTask;
            lock (_stateLock)
            {
                if (!_isRunning || _cancellationTokenSource == null) return;

                CancellationToken cancellationToken = _cancellationTokenSource.Token;
                processingTask = Task.Run(
                    () => ProcessFileAsync(filePath, cancellationToken),
                    CancellationToken.None);
                _activeTasks.Add(processingTask);
            }

            processingTask.ContinueWith(
                completedTask =>
                {
                    lock (_stateLock)
                    {
                        _activeTasks.Remove(completedTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void ProcessExistingFiles()
        {
            var files = Directory.GetFiles(_monitorPath);
            foreach (var file in files)
            {
                QueueFile(file);
            }
        }

        private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                if (!await WaitForFileReadyAsync(filePath, cancellationToken).ConfigureAwait(false)) return;

                cancellationToken.ThrowIfCancellationRequested();
                var handler = OnFileCreated;
                if (handler != null)
                {
                    await handler.Invoke(filePath, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 停止机台后丢弃在途处理。
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        private async Task<bool> WaitForFileReadyAsync(
            string filePath,
            CancellationToken cancellationToken,
            int maxRetries = 10)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public void MoveToSuccess(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // 文件不存在，直接返回
                return;
            }
            MoveFile(filePath, _successPath);
        }

        public void MoveToError(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // 文件不存在，直接返回
                return;
            }
            MoveFile(filePath, _errorPath);
        }

        private void MoveFile(string sourcePath, string targetFolder)
        {
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = Path.Combine(targetFolder, fileName);

            int count = 1;
            while (File.Exists(targetPath))
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                targetPath = Path.Combine(targetFolder, $"{name}_{count}{ext}");
                count++;
            }

            File.Move(sourcePath, targetPath);
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            lock (_stateLock)
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }
}

// Core/FileWatcher.cs
using System;
using System.IO;
using System.Threading.Tasks;
namespace MachineDataAcquisitionSystem.Core.Parser
.Core
{
    public class FileWatcher : IDisposable
    {
        private FileSystemWatcher _watcher;
        private string _monitorPath;
        private string _successPath;
        private string _errorPath;
        private bool _isRunning;

        // 当有新文件时触发
        public event Func<string, Task> OnFileCreated;

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
            if (_isRunning) return;

            _watcher = new FileSystemWatcher
            {
                Path = _monitorPath,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreatedHandler;
            _isRunning = true;

            // 处理已有文件
            ProcessExistingFiles();
        }

        public void Stop()
        {
            if (!_isRunning) return;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            _isRunning = false;
        }

        private async void OnFileCreatedHandler(object sender, FileSystemEventArgs e)
        {
            // 等待文件完全写入
            if (!WaitForFileReady(e.FullPath)) return;

            if (OnFileCreated != null)
            {
                await OnFileCreated.Invoke(e.FullPath);
            }
        }

        private void ProcessExistingFiles()
        {
            var files = Directory.GetFiles(_monitorPath);
            foreach (var file in files)
            {
                Task.Run(async () =>
                {
                    if (OnFileCreated != null)
                        await OnFileCreated.Invoke(file);
                });
            }
        }

        private bool WaitForFileReady(string filePath, int maxRetries = 10)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    Task.Delay(100).Wait();
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
            Stop();
        }
    }
}
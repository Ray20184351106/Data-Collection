// Core/Monitor/FileMonitor.cs
using System;
using System.IO;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core.Monitor
{
    public class FileMonitor : IDisposable
    {
        private FileSystemWatcher _watcher;
        private readonly string _monitorPath;
        private readonly string _successPath;
        private readonly string _errorPath;
        private bool _isRunning;

        // 文件处理完成事件（由外部订阅）
        public event Func<string, Task<ProcessResult>> OnFileReady;

        public FileMonitor(string monitorPath, string successPath, string errorPath)
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

        /// <summary>
        /// 启动监控
        /// </summary>
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

            _watcher.Created += OnFileCreated;
            _isRunning = true;

            // 处理已存在的文件
            ProcessExistingFiles();
        }

        /// <summary>
        /// 停止监控
        /// </summary>
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

        /// <summary>
        /// 新文件创建事件
        /// </summary>
        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // 等待文件完全写入
            if (!WaitForFileReady(e.FullPath))
                return;

            // 异步处理，不阻塞监控线程
            await Task.Run(async () => await ProcessFileAsync(e.FullPath));
        }

        /// <summary>
        /// 处理已存在的文件
        /// </summary>
        private void ProcessExistingFiles()
        {
            var existingFiles = Directory.GetFiles(_monitorPath);
            foreach (var file in existingFiles)
            {
                Task.Run(async () => await ProcessFileAsync(file));
            }
        }

        /// <summary>
        /// 等待文件写入完成
        /// </summary>
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

        /// <summary>
        /// 处理单个文件
        /// </summary>
        private async Task ProcessFileAsync(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            ProcessResult result = null;

            try
            {
                // 触发外部处理逻辑
                if (OnFileReady != null)
                {
                    result = await OnFileReady.Invoke(filePath);

                    // 根据处理结果移动文件
                    if (result.Success)
                    {
                        MoveFile(filePath, _successPath);
                    }
                    else
                    {
                        MoveFile(filePath, _errorPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // 异常时移动到错误目录
                MoveFile(filePath, _errorPath);
                result = ProcessResult.Fail(fileName, ex.Message);
            }
        }

        /// <summary>
        /// 移动文件（处理重名）
        /// </summary>
        private void MoveFile(string sourcePath, string targetFolder)
        {
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = Path.Combine(targetFolder, fileName);

            // 处理重名
            int count = 1;
            while (File.Exists(targetPath))
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                targetPath = Path.Combine(targetFolder, $"{nameWithoutExt}_{count}{ext}");
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
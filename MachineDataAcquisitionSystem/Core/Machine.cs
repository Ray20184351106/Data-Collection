// Core/Machine.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Parser;
using MachineDataAcquisitionSystem.Data;
using MachineDataAcquisitionSystem.Models;
using MachineDataAcquisitionSystem.Core.Monitor;
using MachineDataAcquisitionSystem.Core.Parser;
using MachineDataAcquisitionSystem.Data;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core
{
    public class Machine
    {
        private readonly MachineConfig _config;
        private readonly IDataRepository _repository;
        private readonly List<IParser> _parsers;
        private FileMonitor _monitor;

        private int _todayTotal;
        private int _todaySuccess;
        private int _todayFail;
        private MachineStatus _status;

        // 公开属性
        public int Id => _config.Id;
        public string Name => _config.Name;
        public MachineStatus Status => _status;
        public int TodayTotal => _todayTotal;
        public int TodaySuccess => _todaySuccess;
        public int TodayFail => _todayFail;

        // 事件
        public event Action<Machine> OnStatusChanged;
        public event Action<Machine, ProcessResult> OnFileProcessed;

        public Machine(MachineConfig config, IDataRepository repository, List<IParser> parsers)
        {
            _config = config;
            _repository = repository;
            _parsers = parsers;
            _status = MachineStatus.Stopped;

            // 加载今日统计
            _ = LoadTodayStatistics();
        }

        private async Task LoadTodayStatistics()
        {
            var (total, success, fail) = await _repository.GetMachineStatisticsAsync(_config.Id);
            _todayTotal = total;
            _todaySuccess = success;
            _todayFail = fail;
        }

        /// <summary>
        /// 启动机台
        /// </summary>
        public void Start()
        {
            if (_status == MachineStatus.Running) return;

            _monitor = new FileMonitor(_config.MonitorPath, _config.SuccessPath, _config.ErrorPath);
            _monitor.OnFileReady += ProcessFileAsync;
            _monitor.Start();

            _status = MachineStatus.Running;
            OnStatusChanged?.Invoke(this);
        }

        /// <summary>
        /// 停止机台
        /// </summary>
        public void Stop()
        {
            if (_status != MachineStatus.Running) return;

            _monitor?.Stop();
            _monitor?.Dispose();

            _status = MachineStatus.Stopped;
            OnStatusChanged?.Invoke(this);
        }

        /// <summary>
        /// 处理文件
        /// </summary>
        private async Task<ProcessResult> ProcessFileAsync(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            try
            {
                // 1. 检查是否已处理
                if (await _repository.IsFileProcessedAsync(fileName, _config.Id))
                {
                    return ProcessResult.Fail(fileName, "文件已处理过");
                }

                // 2. 找到合适的解析器
                IParser parser = null;
                foreach (var p in _parsers)
                {
                    if (p.CanParse(filePath))
                    {
                        parser = p;
                        break;
                    }
                }

                if (parser == null)
                {
                    return ProcessResult.Fail(fileName, "未找到合适的解析器");
                }

                // 3. 解析文件
                var dataList = parser.Parse(filePath, _config.Id, out string errorMsg);

                if (dataList == null || dataList.Count == 0)
                {
                    await _repository.RecordFileProcessAsync(fileName, _config.Id, false, errorMsg);
                    return ProcessResult.Fail(fileName, errorMsg ?? "无有效数据");
                }

                // 4. 保存到数据库
                int savedCount = await _repository.SaveTestDataAsync(dataList);

                // 5. 记录处理成功
                await _repository.RecordFileProcessAsync(fileName, _config.Id, true);

                // 6. 更新统计
                _todayTotal++;
                _todaySuccess++;

                var result = ProcessResult.Ok(fileName, $"解析成功，共{dataList.Count}条数据", dataList.Count);
                OnFileProcessed?.Invoke(this, result);

                return result;
            }
            catch (Exception ex)
            {
                // 记录失败
                await _repository.RecordFileProcessAsync(fileName, _config.Id, false, ex.Message);

                _todayTotal++;
                _todayFail++;

                var result = ProcessResult.Fail(fileName, ex.Message);
                OnFileProcessed?.Invoke(this, result);

                return result;
            }
        }

        /// <summary>
        /// 刷新统计
        /// </summary>
        public async Task RefreshStatistics()
        {
            var (total, success, fail) = await _repository.GetMachineStatisticsAsync(_config.Id);
            _todayTotal = total;
            _todaySuccess = success;
            _todayFail = fail;
        }
    }
}
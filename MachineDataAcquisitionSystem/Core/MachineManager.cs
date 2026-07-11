// Core/MachineManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Parser;
using MachineDataAcquisitionSystem.Data;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core
{
    public class MachineManager
    {
        private readonly List<Machine> _machines;
        private readonly IDataRepository _repository;
        private readonly List<IParser> _parsers;

        // 事件
        public event Action<Machine> OnMachineStatusChanged;
        public event Action<Machine, ProcessResult> OnFileProcessed;

        public MachineManager(IDataRepository repository)
        {
            _repository = repository;
            _machines = new List<Machine>();

            // 初始化解析器列表
            _parsers = new List<IParser>
            {
                new JsonParser(),
                new TxtParser()
                // 可继续添加其他解析器
            };
        }

        /// <summary>
        /// 获取所有机台
        /// </summary>
        public IReadOnlyList<Machine> GetAllMachines()
        {
            return _machines.AsReadOnly();
        }

        /// <summary>
        /// 获取运行中的机台数量
        /// </summary>
        public int GetRunningCount()
        {
            return _machines.Count(m => m.Status == MachineStatus.Running);
        }

        /// <summary>
        /// 添加机台
        /// </summary>
        public void AddMachine(MachineConfig config)
        {
            var machine = new Machine(config, _repository, _parsers);
            machine.OnStatusChanged += OnMachineStatusChanged;
            machine.OnFileProcessed += OnFileProcessed;

            _machines.Add(machine);
        }

        /// <summary>
        /// 移除机台
        /// </summary>
        public bool RemoveMachine(int machineId)
        {
            var machine = _machines.FirstOrDefault(m => m.Id == machineId);
            if (machine != null)
            {
                machine.Stop();
                _machines.Remove(machine);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清空所有机台
        /// </summary>
        public void Clear()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
            _machines.Clear();
        }

        /// <summary>
        /// 启动所有机台
        /// </summary>
        public void StartAll()
        {
            foreach (var machine in _machines)
            {
                machine.Start();
            }
        }

        /// <summary>
        /// 停止所有机台
        /// </summary>
        public void StopAll()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
        }

        /// <summary>
        /// 启动指定机台
        /// </summary>
        public void StartMachine(int machineId)
        {
            var machine = _machines.FirstOrDefault(m => m.Id == machineId);
            machine?.Start();
        }

        /// <summary>
        /// 停止指定机台
        /// </summary>
        public void StopMachine(int machineId)
        {
            var machine = _machines.FirstOrDefault(m => m.Id == machineId);
            machine?.Stop();
        }

        /// <summary>
        /// 获取总统计
        /// </summary>
        public async Task<Statistics> GetTotalStatistics()
        {
            var stats = new Statistics();

            foreach (var machine in _machines)
            {
                await machine.RefreshStatistics();
                stats.TotalFiles += machine.TodayTotal;
                stats.SuccessFiles += machine.TodaySuccess;
                stats.FailFiles += machine.TodayFail;
            }

            stats.CalculateRate();
            return stats;
        }

        /// <summary>
        /// 从配置加载机台
        /// </summary>
        public void LoadFromConfig()
        {
            var configs = MachineConfig.Load();
            Clear();

            foreach (var config in configs)
            {
                AddMachine(config);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Data
{
    public class MockRepository : IDataRepository
    {
        private readonly List<string> _processedFiles = new List<string>();

        public Task<int> SaveTestDataAsync(List<TestData> dataList)
        {
            // 模拟保存成功
            System.Diagnostics.Debug.WriteLine($"保存 {dataList.Count} 条数据");
            return Task.FromResult(dataList.Count);
        }

        public Task<bool> IsFileProcessedAsync(string fileName, int machineId)
        {
            string key = $"{machineId}_{fileName}";
            bool isProcessed = _processedFiles.Contains(key);
            return Task.FromResult(isProcessed);
        }

        public Task RecordFileProcessAsync(string fileName, int machineId, bool success, string errorMsg = null)
        {
            string key = $"{machineId}_{fileName}";
            if (success && !_processedFiles.Contains(key))
            {
                _processedFiles.Add(key);
            }

            System.Diagnostics.Debug.WriteLine($"记录文件: {fileName}, 成功: {success}");
            return Task.CompletedTask;
        }

        public Task<(int total, int success, int fail)> GetMachineStatisticsAsync(int machineId)
        {
            // 返回模拟数据
            return Task.FromResult((total: 100, success: 98, fail: 2));
        }
    }
}

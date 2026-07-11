using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Data
{
    public interface IDataRepository
    {
        // 保存测试数据
        Task<int> SaveTestDataAsync(List<TestData> dataList);

        // 检查文件是否已处理
        Task<bool> IsFileProcessedAsync(string fileName, int machineId);

        // 记录文件处理状态
        Task RecordFileProcessAsync(string fileName, int machineId, bool success, string errorMsg = null);

        // 获取机台统计
        Task<(int total, int success, int fail)> GetMachineStatisticsAsync(int machineId);
    }
}

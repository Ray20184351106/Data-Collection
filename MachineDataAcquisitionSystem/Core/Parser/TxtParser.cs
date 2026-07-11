// Core/Parser/TxtParser.cs
using System;
using System.Collections.Generic;
using System.IO;
using MachineDataAcquisitionSystem.Models;
using MachineDataAcquisitionSystem.Core.Parser;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core.Parser
{
    public class TxtParser : IParser
    {
        public bool CanParse(string filePath)
        {
            return filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        public List<TestData> Parse(string filePath, int machineId, out string errorMsg)
        {
            errorMsg = null;
            var result = new List<TestData>();

            try
            {
                string[] lines = File.ReadAllLines(filePath);

                if (lines.Length == 0)
                {
                    errorMsg = "文件为空";
                    return result;
                }

                string productSn = "";

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 假设格式1: SN:123456
                    if (line.StartsWith("SN:") && string.IsNullOrEmpty(productSn))
                    {
                        productSn = line.Substring(3);
                        continue;
                    }

                    // 假设格式2: 测试项|测试值|结果|时间
                    string[] parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        result.Add(new TestData
                        {
                            MachineId = machineId,
                            ProductSn = productSn,
                            TestItem = parts[0].Trim(),
                            TestValue = decimal.TryParse(parts[1], out decimal val) ? val : 0,
                            TestResult = parts[2].Trim(),
                            TestTime = parts.Length >= 4 && DateTime.TryParse(parts[3], out DateTime dt) ? dt : DateTime.Now,
                            FileName = Path.GetFileName(filePath),
                            CreateTime = DateTime.Now
                        });
                    }
                }

                if (result.Count == 0)
                {
                    errorMsg = "未找到有效的测试数据";
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"解析失败: {ex.Message}";
            }

            return result;
        }
    }
}
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core.Parser
{
    public class JsonParser : IParser
    {
        public bool CanParse(string filePath)
        {
            return filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        public List<TestData> Parse(string filePath, int machineId, out string errorMsg)
        {
            errorMsg = null;
            var result = new List<TestData>();

            try
            {
                string json = File.ReadAllText(filePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                if (data == null)
                {
                    errorMsg = "文件内容为空或格式错误";
                    return result;
                }

                // ================= 修复这里 =================
                // 原来：data.GetValueOrDefault("ProductSn") → 报错
                // 现在：自己获取，兼容 .NET4.8

                string productSn = "Unknown";
                if (data.ContainsKey("ProductSn") && data["ProductSn"] != null)
                {
                    productSn = data["ProductSn"].ToString();
                }

                decimal value = 0;
                if (data.ContainsKey("Value") && data["Value"] != null)
                {
                    decimal.TryParse(data["Value"].ToString(), out value);
                }
                // ============================================

                result.Add(new TestData
                {
                    MachineId = machineId,
                    ProductSn = productSn,
                    TestItem = "测试项",
                    TestValue = value,
                    TestResult = value > 0 ? "PASS" : "FAIL",
                    TestTime = DateTime.Now,
                    FileName = Path.GetFileName(filePath),
                    CreateTime = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                errorMsg = $"解析失败: {ex.Message}";
            }

            return result;
        }
    }
}
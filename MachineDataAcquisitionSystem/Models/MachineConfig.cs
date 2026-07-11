using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MachineDataAcquisitionSystem.Models
{
    public class MachineConfig
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string MonitorPath { get; set; }    // 监控目录
        public string SuccessPath { get; set; }     // 成功目录
        public string ErrorPath { get; set; }       // 失败目录

        // 保存配置到文件
        public static void Save(List<MachineConfig> configs)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            // 这是 .NET4.8 正确的 JSON 序列化（替换你原来的代码）
            string json = JsonConvert.SerializeObject(configs, Formatting.Indented);

            File.WriteAllText(configPath, json);
        }

        // 加载配置
        public static List<MachineConfig> Load()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            // 文件不存在，返回空列表
            if (!File.Exists(configPath))
            {
                return new List<MachineConfig>();
            }

            string json = File.ReadAllText(configPath);

            // 👇 替换成这个（.NET4.8 正确写法）
            return JsonConvert.DeserializeObject<List<MachineConfig>>(json) ?? new List<MachineConfig>();
        }
    }
}

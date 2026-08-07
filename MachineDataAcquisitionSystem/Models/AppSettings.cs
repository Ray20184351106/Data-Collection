// Models/AppSettings.cs
using System.Collections.Generic;

namespace MachineDataAcquisitionSystem.Models
{
    public class AppSettings
    {
        // 路径配置
        public string DefaultBasePath { get; set; } = @"D:\Test";
        public string MachineNamePrefix { get; set; } = "机台";
        public string IncomingFolder { get; set; } = "Incoming";
        public string SuccessFolder { get; set; } = "Success";
        public string ErrorFolder { get; set; } = "Error";

        // 数据库配置列表
        public List<DatabaseConfig> Databases { get; set; } = new List<DatabaseConfig>();

        // 预警配置
        public bool EnableAlert { get; set; } = false;
        public int AlertThreshold { get; set; } = 10;
        public bool AlertSound { get; set; } = true;
        public bool AlertPopup { get; set; } = true;

        // 日志配置
        public string LogLevel { get; set; } = "信息";
        public int LogRetentionDays { get; set; } = 30;

        // AI 仅用于填充声明式映射草稿，不拥有保存、执行或发布权限。
        public AiMappingConfig AiMapping { get; set; } = new AiMappingConfig();

        // 高级配置
        public bool AutoStart { get; set; } = false;
        public bool AutoStartMonitor { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
    }
}

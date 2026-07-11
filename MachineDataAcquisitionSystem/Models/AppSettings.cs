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

        // 高级配置
        public bool AutoStart { get; set; } = false;
        public bool AutoStartMonitor { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
    }
}
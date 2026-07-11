using System;
using System.ComponentModel;

namespace MachineDataAcquisitionSystem.Models
{
    public class DatabaseConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Category("基本信息")]
        [DisplayName("数据库名称")]
        [Description("自定义数据库名称")]
        public string Name { get; set; } = "新数据库";

        [Category("基本信息")]
        [DisplayName("数据库类型")]
        [Description("选择数据库类型")]
        [TypeConverter(typeof(DbTypeConverter))]
        public string DbType { get; set; } = "SQLite";

        [Category("基本信息")]
        [DisplayName("启用")]
        [Description("是否启用此数据库")]
        public bool IsEnabled { get; set; } = true;

        [Category("基本信息")]
        [DisplayName("主数据库")]
        [Description("是否作为主数据库")]
        public bool IsPrimary { get; set; } = false;

        [Category("连接设置")]
        [DisplayName("服务器地址")]
        [Description("IP地址或域名，SQLite时填写文件路径")]
        public string Server { get; set; } = "localhost";

        [Category("连接设置")]
        [DisplayName("端口")]
        [Description("数据库端口号")]
        public int Port { get; set; } = 0;

        [Category("连接设置")]
        [DisplayName("数据库名")]
        [Description("数据库名称")]
        public string DatabaseName { get; set; } = "";

        [Category("连接设置")]
        [DisplayName("用户名")]
        [Description("登录用户名")]
        public string UserId { get; set; } = "";

        [Category("连接设置")]
        [DisplayName("密码")]
        [Description("登录密码")]
        [PasswordPropertyText(true)]
        public string Password { get; set; } = "";

        [Category("高级设置")]
        [DisplayName("连接字符串")]
        [Description("完整的连接字符串，留空则自动生成")]
        public string ConnectionString { get; set; } = "";

        [Category("高级设置")]
        [DisplayName("说明")]
        [Description("备注信息")]
        public string Description { get; set; } = "";

        [Browsable(false)]
        public DateTime LastTestTime { get; set; }

        [Browsable(false)]
        public bool LastTestResult { get; set; }

        /// <summary>
        /// 获取连接字符串（如果 ConnectionString 为空，则根据其他字段自动生成）
        /// </summary>
        public string GetConnectionString()
        {
            // 如果已有连接字符串，直接使用
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                return ConnectionString;
            }

            // 调试：输出 DbType 原始值
            System.Diagnostics.Debug.WriteLine($"DbType 原始值: '{DbType}'");

            // 根据数据库类型生成连接字符串
            if (DbType == null) return "";

            // 处理各种可能的写法
            string dbTypeLower = DbType.ToLower().Trim();
            if (dbTypeLower == "sqlserver" || dbTypeLower == "sql server" || dbTypeLower == "sqlserver")
            {
                if (Port > 0 && Port != 1433)
                {
                    return $"Server={Server},{Port};Database={DatabaseName};User Id={UserId};Password={Password};";
                }
                else
                {
                    return $"Server={Server};Database={DatabaseName};User Id={UserId};Password={Password};";
                }
            }
            else if (dbTypeLower == "mysql")
            {
                int myport = Port > 0 ? Port : 3306;
                return $"Server={Server};Port={myport};Database={DatabaseName};Uid={UserId};Pwd={Password};";
            }
            else if (dbTypeLower == "postgresql")
            {
                int pgport = Port > 0 ? Port : 5432;
                return $"Host={Server};Port={pgport};Database={DatabaseName};Username={UserId};Password={Password};";
            }
            else if (dbTypeLower == "sqlite")
            {
                return Server;
            }
            else
            {
                // 未知类型，尝试返回默认 SQL Server 格式
                return $"Server={Server};Database={DatabaseName};User Id={UserId};Password={Password};";
            }
        }
    }

    // ========== 下拉框转换器（放在外面） ==========
    public class DbTypeConverter : TypeConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;  // 支持下拉列表
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;  // 只能从列表中选择，不能手动输入
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new[] { "SQLite", "SQL Server", "MySQL", "PostgreSQL" });
        }
    }
}
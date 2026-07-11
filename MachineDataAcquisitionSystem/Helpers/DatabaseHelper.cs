using System;
using System.Data.SQLite;
using System.IO;

namespace MachineDataAcquisitionSystem.Helpers
{
    public static class DatabaseHelper
    {
        private static string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "采集记录.db");
        private static string ConnectionString => $"Data Source={DbPath};Version=3;";

        public static void Initialize()
        {
            // 确保目录存在
            string dbDir = Path.GetDirectoryName(DbPath);
            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();

                string sql = @"
                    -- 文件处理记录表
                    CREATE TABLE IF NOT EXISTS FileProcessRecord (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MachineId INTEGER NOT NULL,
                        FileName TEXT NOT NULL,
                        Status TEXT,
                        RecordCount INTEGER DEFAULT 0,
                        ErrorMsg TEXT,
                        ProcessTime DATETIME,
                        Duration INTEGER,
                        CreateTime DATETIME DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE INDEX IF NOT EXISTS idx_MachineId ON FileProcessRecord(MachineId);
                    CREATE INDEX IF NOT EXISTS idx_ProcessTime ON FileProcessRecord(ProcessTime);
                    CREATE INDEX IF NOT EXISTS idx_Status ON FileProcessRecord(Status);

                    -- 数据模型表
                    CREATE TABLE IF NOT EXISTS DataModels (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ModelName TEXT NOT NULL,
                        TableName TEXT NOT NULL,
                        ParentModelId INTEGER DEFAULT 0,
                        Description TEXT,
                        IsActive INTEGER DEFAULT 1
                    );

                    -- 模型字段表（完整列）
                    CREATE TABLE IF NOT EXISTS ModelFields (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ModelId INTEGER NOT NULL,
                        FieldName TEXT NOT NULL,
                        FieldType TEXT NOT NULL,
                        FieldLength INTEGER DEFAULT 0,
                        IsRequired INTEGER DEFAULT 0,
                        IsPrimaryKey INTEGER DEFAULT 0,
                        IsIdentity INTEGER DEFAULT 0,
                        Description TEXT
                    );

                    -- 机台配置表
                    CREATE TABLE IF NOT EXISTS Machines (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MachineName TEXT NOT NULL,
                        MachineCode TEXT NOT NULL,
                        SortOrder INTEGER DEFAULT 0,
                        IsEnabled INTEGER DEFAULT 1,
                        Description TEXT,
                        CreateTime DATETIME DEFAULT CURRENT_TIMESTAMP
                    );
                    -- 插入默认机台数据
                        INSERT OR IGNORE INTO Machines (Id, MachineName, MachineCode, SortOrder) VALUES
                        (1, '机台1', 'MC001', 1),
                        (2, '机台2', 'MC002', 2),
                        (3, '机台3', 'MC003', 3),
                        (4, '机台4', 'MC004', 4),
                        (5, '机台5', 'MC005', 5),
                        (6, '机台6', 'MC006', 6);

                    -- 解析脚本表
                    CREATE TABLE IF NOT EXISTS ParseScripts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        ModelId INTEGER NOT NULL,
                        MachineId INTEGER DEFAULT 0,
                        FileExtension TEXT NOT NULL,
                        ScriptCode TEXT NOT NULL,
                        IsEnabled INTEGER DEFAULT 1,
                        CreateTime DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UpdateTime DATETIME DEFAULT CURRENT_TIMESTAMP
                    );
                    -- 字段映射表
                    CREATE TABLE IF NOT EXISTS FieldMappings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScriptId INTEGER NOT NULL,
                        ScriptVariable TEXT NOT NULL,
                        ModelField TEXT NOT NULL,
                        TransformExpression TEXT,
                        SortOrder INTEGER DEFAULT 0
                    );

                    -- 脚本适用机台关联表
                    CREATE TABLE IF NOT EXISTS ScriptMachines (
                        ScriptId INTEGER NOT NULL,
                        MachineId INTEGER NOT NULL,
                        PRIMARY KEY (ScriptId, MachineId)
                    );

                    -- 基类字段配置表
                    CREATE TABLE IF NOT EXISTS BaseFields (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FieldName TEXT NOT NULL,
                        FieldType TEXT NOT NULL,
                        FieldLength INTEGER DEFAULT 0,
                        DefaultValue TEXT,
                        IsRequired INTEGER DEFAULT 0,
                        IsReadOnly INTEGER DEFAULT 0,
                        SortOrder INTEGER DEFAULT 0,
                        Description TEXT
                    );
                ";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                // 插入默认基类字段
                InsertDefaultBaseFields(conn);
            }
        }

        private static void InsertDefaultBaseFields(SQLiteConnection conn)
        {
            // 检查是否已有数据
            string checkSql = "SELECT COUNT(*) FROM BaseFields";
            using (var cmd = new SQLiteCommand(checkSql, conn))
            {
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                if (count > 0) return;
            }

            // 插入默认基类字段
            string insertSql = @"
                INSERT INTO BaseFields (FieldName, FieldType, FieldLength, DefaultValue, IsRequired, IsReadOnly, SortOrder, Description) VALUES
                ('CID', 'long', 0, 'YitIdHelper.NextId()', 1, 1, 1, '主键ID（雪花ID）'),
                ('CDATETIME_CREATED', 'datetime', 0, 'DateTime.Now', 1, 0, 2, '创建时间'),
                ('CDATETIME_MODIFIED', 'datetime', 0, 'DateTime.Now', 1, 0, 3, '修改时间'),
                ('CUSER_CREATED', 'string', 50, 'SYS', 1, 0, 4, '创建人'),
                ('CUSER_MODIFIED', 'string', 50, 'SYS', 1, 0, 5, '修改人'),
                ('CSTATE', 'string', 1, 'A', 1, 0, 6, '状态（A=有效）'),
                ('CINSTANCE_ID', 'string', 50, '*', 0, 0, 7, '实例ID'),
                ('CROWREMARK', 'string', 200, '', 0, 0, 8, '备注'),
                ('CENTERPRISE_CODE', 'long', 0, '0', 1, 0, 9, '企业编码'),
                ('CORG_CODE', 'long', 0, '0', 1, 0, 10, '组织编码');
            ";

            using (var cmd = new SQLiteCommand(insertSql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public static string GetConnectionString() => ConnectionString;
    }
}
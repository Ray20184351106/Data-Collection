using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Helpers;
using NPOI.XSSF.UserModel;
using SqlSugar;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public class ExcelDemoScriptTemplateTests
    {
        [Theory]
        [InlineData("123Model")]
        [InlineData("Model; return null;")]
        public void Create_rejects_model_names_that_are_not_csharp_identifiers(string modelName)
        {
            Assert.Throws<ArgumentException>(() => ExcelDemoScriptTemplate.Create(modelName));
        }

        [Fact]
        public void Execute_reads_three_fields_from_the_first_excel_data_row()
        {
            const int modelId = 910001;
            const string modelName = "ExcelDemoData";
            DatabaseHelper.Initialize();
            EnsureModelMetadata(modelId, modelName);

            string excelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                CreateWorkbook(excelPath);

                string script = ExcelDemoScriptTemplate.Create(modelName);
                object result = ScriptEngine.Execute(script, excelPath, 1, modelId);
                Type resultType = result.GetType();

                Assert.Equal("SN-20260713-001", resultType.GetProperty("ProductCode").GetValue(result));
                Assert.Equal(12.34m, resultType.GetProperty("Measurement").GetValue(result));
                Assert.Equal("PASS", resultType.GetProperty("Result").GetValue(result));
            }
            finally
            {
                if (File.Exists(excelPath)) File.Delete(excelPath);
            }
        }

        [Fact]
        public void Parsed_excel_data_can_be_inserted_into_real_sql_server()
        {
            const int modelId = 910001;
            const string modelName = "ExcelDemoData";
            const string databaseName = "FileAcquisitionDemo";
            const string masterConnection = @"Server=.\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true";
            string demoConnection = @"Server=.\SQLEXPRESS;Database=" + databaseName + ";Integrated Security=true;TrustServerCertificate=true";

            DatabaseHelper.Initialize();
            EnsureModelMetadata(modelId, modelName);
            EnsureSqlServerDatabase(masterConnection, demoConnection, databaseName);

            string excelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            try
            {
                CreateWorkbook(excelPath);
                object parsed = ScriptEngine.Execute(ExcelDemoScriptTemplate.Create(modelName), excelPath, 1, modelId);

                using (var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = demoConnection,
                    DbType = DbType.SqlServer,
                    IsAutoCloseConnection = true
                }))
                {
                    db.Ado.ExecuteCommand("TRUNCATE TABLE dbo.ExcelDemoData");
                    Assert.Equal(1, db.InsertableByObject(parsed).ExecuteCommand());
                }

                using (var connection = new SqlConnection(demoConnection))
                using (var command = new SqlCommand("SELECT ProductCode, Measurement, Result FROM dbo.ExcelDemoData", connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        Assert.True(reader.Read());
                        Assert.Equal("SN-20260713-001", reader.GetString(0));
                        Assert.Equal(12.34m, reader.GetDecimal(1));
                        Assert.Equal("PASS", reader.GetString(2));
                        Assert.False(reader.Read());
                    }
                }
            }
            finally
            {
                if (File.Exists(excelPath)) File.Delete(excelPath);
            }
        }

        private static void CreateWorkbook(string path)
        {
            using (var workbook = new XSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Data");
                var header = sheet.CreateRow(0);
                header.CreateCell(0).SetCellValue("ProductCode");
                header.CreateCell(1).SetCellValue("Measurement");
                header.CreateCell(2).SetCellValue("Result");

                var row = sheet.CreateRow(1);
                row.CreateCell(0).SetCellValue("SN-20260713-001");
                row.CreateCell(1).SetCellValue(12.34);
                row.CreateCell(2).SetCellValue("PASS");

                using (var stream = File.Create(path)) workbook.Write(stream);
            }
        }

        private static void EnsureModelMetadata(int modelId, string modelName)
        {
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
INSERT OR REPLACE INTO DataModels (Id, ModelName, TableName, ParentModelId, Description, IsActive)
VALUES (@Id, @ModelName, @TableName, 0, 'Excel三字段解析测试模型', 1);", connection))
                {
                    command.Parameters.AddWithValue("@Id", modelId);
                    command.Parameters.AddWithValue("@ModelName", modelName);
                    command.Parameters.AddWithValue("@TableName", modelName);
                    command.ExecuteNonQuery();
                }
            }

            string modelDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels");
            Directory.CreateDirectory(modelDirectory);
            File.WriteAllText(Path.Combine(modelDirectory, modelName + ".cs"), @"
namespace MachineDataAcquisitionSystem.Models
{
    public class ExcelDemoData
    {
        public string ProductCode { get; set; }
        public decimal Measurement { get; set; }
        public string Result { get; set; }
    }
}");
        }

        private static void EnsureSqlServerDatabase(string masterConnection, string demoConnection, string databaseName)
        {
            string createDatabaseSql =
                "IF DB_ID(@DatabaseName) IS NULL EXEC(N'CREATE DATABASE [" + databaseName + "]')";
            using (var connection = new SqlConnection(masterConnection))
            using (var command = new SqlCommand(createDatabaseSql, connection))
            {
                command.Parameters.AddWithValue("@DatabaseName", databaseName);
                connection.Open();
                command.ExecuteNonQuery();
            }

            using (var connection = new SqlConnection(demoConnection))
            using (var command = new SqlCommand(@"
IF OBJECT_ID(N'dbo.ExcelDemoData', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExcelDemoData
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProductCode nvarchar(100) NOT NULL,
        Measurement decimal(18,4) NOT NULL,
        Result nvarchar(20) NOT NULL
    );
END", connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
        }
    }
}

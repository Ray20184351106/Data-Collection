using System;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Forms;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ModelExcelImportServiceTests
    {
        private static readonly string[] Headers =
        {
            "模型名称", "表名", "模型说明", "父模型", "启用", "字段名称",
            "字段类型", "字段长度", "必填", "主键", "自增", "字段说明"
        };

        [Theory]
        [InlineData(".xlsx")]
        [InlineData(".xls")]
        public void Preview_groups_field_rows_and_defaults_blank_enabled_to_false(string extension)
        {
            string databasePath = CreateDatabase();
            string workbookPath = CreateWorkbook(extension, sheet =>
            {
                AddRow(sheet, 1, "InspectionRecord", "T_InspectionRecord", "检测结果", "BaseEntity", "是",
                    "ProductCode", "string", "50", "是", "否", "否", "产品编码");
                AddRow(sheet, 2, "InspectionRecord", "T_InspectionRecord", "检测结果", "BaseEntity", "是",
                    "Temperature", "decimal", "", "否", "否", "否", "检测温度");
                AddRow(sheet, 3, "AlarmRecord", "T_AlarmRecord", "报警记录", "", "",
                    "AlarmCode", "string", "50", "1", "0", "0", "报警编码");
            });

            try
            {
                var service = new ModelExcelImportService(ConnectionString(databasePath));

                ModelExcelImportPreview preview = service.Preview(workbookPath);

                Assert.True(preview.CanImport, string.Join(Environment.NewLine, preview.Errors));
                Assert.Equal(2, preview.ModelCount);
                Assert.Equal(3, preview.FieldCount);
                Assert.Equal(1, preview.EnabledModelCount);
                Assert.Equal(2, preview.Models[0].Fields.Count);
                Assert.False(preview.Models[1].IsActive);
                Assert.Equal("decimal", preview.Models[0].Fields[1].FieldType);
                Assert.Equal(0, preview.Models[0].Fields[1].FieldLength);
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void Preview_reports_row_and_column_for_invalid_and_inconsistent_values()
        {
            string databasePath = CreateDatabase();
            string workbookPath = CreateWorkbook(".xlsx", sheet =>
            {
                AddRow(sheet, 1, "InspectionRecord", "T_InspectionRecord", "检测结果", "", "否",
                    "ProductCode", "number", "50", "否", "否", "否", "产品编码");
                AddRow(sheet, 2, "InspectionRecord", "T_Changed", "检测结果", "", "否",
                    "ProductCode", "string", "50", "否", "否", "否", "产品编码");
            });

            try
            {
                ModelExcelImportPreview preview = new ModelExcelImportService(ConnectionString(databasePath))
                    .Preview(workbookPath);

                Assert.False(preview.CanImport);
                Assert.Contains(preview.Errors, error =>
                    error.RowNumber == 2 && error.ColumnName == "字段类型" && error.Message.Contains("number"));
                Assert.Contains(preview.Errors, error =>
                    error.RowNumber == 3 && error.ColumnName == "表名" && error.Message.Contains("不一致"));
                Assert.Contains(preview.Errors, error =>
                    error.RowNumber == 3 && error.ColumnName == "字段名称" && error.Message.Contains("重复"));
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void Preview_rejects_existing_model_and_table_names_without_overwriting()
        {
            string databasePath = CreateDatabase();
            InsertExistingModel(databasePath, "ExistingModel", "T_Existing");
            string workbookPath = CreateWorkbook(".xlsx", sheet =>
            {
                AddRow(sheet, 1, "ExistingModel", "T_New", "", "", "否",
                    "Code", "string", "20", "否", "否", "否", "");
                AddRow(sheet, 2, "NewModel", "T_Existing", "", "", "否",
                    "Code", "string", "20", "否", "否", "否", "");
            });

            try
            {
                ModelExcelImportPreview preview = new ModelExcelImportService(ConnectionString(databasePath))
                    .Preview(workbookPath);

                Assert.False(preview.CanImport);
                Assert.Contains(preview.Errors, error => error.ColumnName == "模型名称" && error.Message.Contains("ExistingModel"));
                Assert.Contains(preview.Errors, error => error.ColumnName == "表名" && error.Message.Contains("T_Existing"));
                Assert.Equal(1, ScalarInt(databasePath, "SELECT COUNT(*) FROM DataModels"));
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void Preview_rejects_parent_cycles_with_source_rows()
        {
            string databasePath = CreateDatabase();
            string workbookPath = CreateWorkbook(".xlsx", sheet =>
            {
                AddRow(sheet, 1, "ModelA", "T_ModelA", "", "ModelB", "否",
                    "CodeA", "string", "20", "否", "否", "否", "");
                AddRow(sheet, 2, "ModelB", "T_ModelB", "", "ModelA", "否",
                    "CodeB", "string", "20", "否", "否", "否", "");
            });

            try
            {
                ModelExcelImportPreview preview = new ModelExcelImportService(ConnectionString(databasePath))
                    .Preview(workbookPath);

                Assert.False(preview.CanImport);
                Assert.Contains(preview.Errors, error =>
                    error.RowNumber == 2 && error.ColumnName == "父模型" && error.Message.Contains("循环"));
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void Import_writes_all_models_and_resolves_parent_from_same_workbook()
        {
            string databasePath = CreateDatabase();
            string workbookPath = CreateWorkbook(".xlsx", sheet =>
            {
                AddRow(sheet, 1, "BaseInspection", "T_BaseInspection", "基础检测", "BaseEntity", "否",
                    "BatchNo", "string", "50", "是", "否", "否", "批次号");
                AddRow(sheet, 2, "ChildInspection", "T_ChildInspection", "子检测", "BaseInspection", "是",
                    "Result", "bool", "0", "是", "否", "否", "结果");
            });

            try
            {
                var service = new ModelExcelImportService(ConnectionString(databasePath));
                ModelExcelImportPreview preview = service.Preview(workbookPath);

                ModelExcelImportResult result = service.Import(preview);

                Assert.Equal(2, result.ModelCount);
                Assert.Equal(2, result.FieldCount);
                Assert.True(result.FirstModelId > 0);
                Assert.Equal(2, ScalarInt(databasePath, "SELECT COUNT(*) FROM DataModels"));
                Assert.Equal(2, ScalarInt(databasePath, "SELECT COUNT(*) FROM ModelFields"));
                Assert.Equal(
                    ScalarInt(databasePath, "SELECT Id FROM DataModels WHERE ModelName = 'BaseInspection'"),
                    ScalarInt(databasePath, "SELECT ParentModelId FROM DataModels WHERE ModelName = 'ChildInspection'"));
                Assert.Equal(-2, ScalarInt(databasePath, "SELECT ParentModelId FROM DataModels WHERE ModelName = 'BaseInspection'"));
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void Import_rolls_back_every_model_when_a_later_field_insert_fails()
        {
            string databasePath = CreateDatabase();
            Execute(databasePath, @"
CREATE TRIGGER RejectCrashField
BEFORE INSERT ON ModelFields
WHEN NEW.FieldName = 'Crash'
BEGIN
    SELECT RAISE(ABORT, 'test failure');
END;");
            string workbookPath = CreateWorkbook(".xlsx", sheet =>
            {
                AddRow(sheet, 1, "FirstModel", "T_First", "", "", "否",
                    "Safe", "string", "20", "否", "否", "否", "");
                AddRow(sheet, 2, "SecondModel", "T_Second", "", "", "否",
                    "Crash", "string", "20", "否", "否", "否", "");
            });

            try
            {
                var service = new ModelExcelImportService(ConnectionString(databasePath));
                ModelExcelImportPreview preview = service.Preview(workbookPath);
                Assert.True(preview.CanImport, string.Join(Environment.NewLine, preview.Errors));

                Assert.Throws<SQLiteException>(() => service.Import(preview));

                Assert.Equal(0, ScalarInt(databasePath, "SELECT COUNT(*) FROM DataModels"));
                Assert.Equal(0, ScalarInt(databasePath, "SELECT COUNT(*) FROM ModelFields"));
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void CreateTemplate_writes_expected_headers_and_instructions()
        {
            string databasePath = CreateDatabase();
            string workbookPath = Path.Combine(Path.GetTempPath(), "model-import-template-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                new ModelExcelImportService(ConnectionString(databasePath)).CreateTemplate(workbookPath);

                using (FileStream stream = File.OpenRead(workbookPath))
                using (IWorkbook workbook = WorkbookFactory.Create(stream))
                {
                    ISheet sheet = workbook.GetSheet("数据模型");
                    Assert.NotNull(sheet);
                    Assert.NotNull(workbook.GetSheet("填写说明"));
                    for (int column = 0; column < Headers.Length; column++)
                        Assert.Equal(Headers[column], sheet.GetRow(0).GetCell(column).StringCellValue);
                    Assert.Equal(0, sheet.LastRowNum);
                }
            }
            finally
            {
                DeleteFile(workbookPath);
                DeleteFile(databasePath);
            }
        }

        [Fact]
        public void ModelConfigForm_exposes_excel_import_and_template_buttons()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new ModelConfigForm())
                    {
                        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var importButton = Assert.IsType<Button>(
                            typeof(ModelConfigForm).GetField("_btnImportModels", Flags)?.GetValue(form));
                        var templateButton = Assert.IsType<Button>(
                            typeof(ModelConfigForm).GetField("_btnCreateModelImportTemplate", Flags)?.GetValue(form));

                        Assert.Equal("Excel 导入", importButton.Text);
                        Assert.Equal("下载模板", templateButton.Text);
                        Assert.NotNull(importButton.Parent);
                        Assert.NotNull(templateButton.Parent);
                        Assert.True(importButton.Parent.Controls.Contains(importButton));
                        Assert.True(templateButton.Parent.Controls.Contains(templateButton));
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
        }

        private static string CreateWorkbook(string extension, Action<ISheet> configure)
        {
            string path = Path.Combine(Path.GetTempPath(), "model-import-" + Guid.NewGuid().ToString("N") + extension);
            using (IWorkbook workbook = extension == ".xls" ? (IWorkbook)new HSSFWorkbook() : new XSSFWorkbook())
            using (FileStream stream = File.Create(path))
            {
                ISheet sheet = workbook.CreateSheet("数据模型");
                IRow header = sheet.CreateRow(0);
                for (int column = 0; column < Headers.Length; column++)
                    header.CreateCell(column).SetCellValue(Headers[column]);
                configure(sheet);
                workbook.Write(stream);
            }
            return path;
        }

        private static void AddRow(ISheet sheet, int rowIndex, params string[] values)
        {
            IRow row = sheet.CreateRow(rowIndex);
            for (int column = 0; column < values.Length; column++)
                row.CreateCell(column).SetCellValue(values[column]);
        }

        private static string CreateDatabase()
        {
            string path = Path.Combine(Path.GetTempPath(), "model-import-db-" + Guid.NewGuid().ToString("N") + ".db");
            Execute(path, @"
CREATE TABLE DataModels (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelName TEXT NOT NULL,
    TableName TEXT NOT NULL,
    ParentModelId INTEGER DEFAULT 0,
    Description TEXT,
    IsActive INTEGER DEFAULT 1
);
CREATE TABLE ModelFields (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ModelId INTEGER NOT NULL,
    FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL,
    FieldLength INTEGER DEFAULT 0,
    IsRequired INTEGER DEFAULT 0,
    IsPrimaryKey INTEGER DEFAULT 0,
    IsIdentity INTEGER DEFAULT 0,
    Description TEXT
);");
            return path;
        }

        private static void InsertExistingModel(string databasePath, string modelName, string tableName)
        {
            using (var connection = new SQLiteConnection(ConnectionString(databasePath)))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = "INSERT INTO DataModels (ModelName, TableName, ParentModelId, Description, IsActive) VALUES (@ModelName, @TableName, 0, '', 1)";
                command.Parameters.AddWithValue("@ModelName", modelName);
                command.Parameters.AddWithValue("@TableName", tableName);
                command.ExecuteNonQuery();
            }
        }

        private static void Execute(string databasePath, string sql)
        {
            using (var connection = new SQLiteConnection(ConnectionString(databasePath)))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static int ScalarInt(string databasePath, string sql)
        {
            using (var connection = new SQLiteConnection(ConnectionString(databasePath)))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = sql;
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static string ConnectionString(string databasePath)
        {
            return "Data Source=" + databasePath + ";Version=3;";
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

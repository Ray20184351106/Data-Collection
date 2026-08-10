using System;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
using System.Data.SQLite;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public class ScriptMatcherSnapshotTests
    {
        [Fact]
        public void PublishedScript_can_be_enriched_with_model_source_snapshot_before_execution()
        {
            string modelName = "SnapshotProbe" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeneratedModels");
            string path = Path.Combine(directory, modelName + ".cs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "namespace GeneratedModels { public class " + modelName + " { public string Value { get; set; } } }");

            try
            {
                int configuredModelId = GetConfiguredModelId();
                var script = new ParseScript
                {
                    ParserVersionId = 1,
                    IsEnabled = true,
                    ModelId = configuredModelId,
                    TargetModelType = modelName,
                    ScriptCode = "return new " + modelName + "();",
                    RuleType = ParseRuleType.LegacyCode,
                    ContentSha256 = "content",
                    ModelSchemaHash = "schema"
                };

                ScriptMatcher.CaptureModelSourceSnapshot(script);

                Assert.False(string.IsNullOrWhiteSpace(script.GeneratedModelCodeSnapshot));
                Assert.False(string.IsNullOrWhiteSpace(script.GeneratedModelCodeSha256));
                object result = ScriptEngine.Execute(script, "sample.xlsx", 1);
                Assert.Equal(modelName, result.GetType().Name);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static int GetConfiguredModelId()
        {
            using (var connection = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id
FROM DataModels
WHERE TableName IS NOT NULL AND TRIM(TableName) <> ''
ORDER BY Id
LIMIT 1;";
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    throw new InvalidOperationException("测试数据库缺少已配置目标表的数据模型。");
                return Convert.ToInt32(value);
            }
        }
    }
}

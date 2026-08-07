using System;
using System.IO;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
using MachineDataAcquisitionSystem.Models;
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
                var script = new ParseScript
                {
                    ParserVersionId = 1,
                    IsEnabled = true,
                    ModelId = 1,
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
    }
}

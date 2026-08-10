using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ModelTableMappingTests
    {
        [Fact]
        public void ApplySqlSugarTableAttribute_maps_a_model_name_to_its_configured_table()
        {
            string source = "public class 新模型 { public string CR { get; set; } }";

            string mappedSource = ModelTableMapping.ApplySqlSugarTableAttribute(source, "NewTable");

            Assert.Contains("[SqlSugar.SugarTable(\"NewTable\")]", mappedSource);
            Assert.Contains("public class 新模型", mappedSource);
        }

        [Fact]
        public void ApplySqlSugarTableAttribute_does_not_duplicate_an_existing_mapping()
        {
            string source = "[SqlSugar.SugarTable(\"NewTable\")]\r\npublic class 新模型 { }";

            string mappedSource = ModelTableMapping.ApplySqlSugarTableAttribute(source, "NewTable");

            Assert.Equal(1, CountOccurrences(mappedSource, "[SqlSugar.SugarTable(\"NewTable\")]") );
            Assert.Contains("public long CID { get; set; }", mappedSource);
        }

        [Fact]
        public void ApplySqlSugarTableAttribute_adds_a_CID_property_for_legacy_model_snapshots()
        {
            string source = "public class LegacyModel { public string CR { get; set; } }";

            string mappedSource = ModelTableMapping.ApplySqlSugarTableAttribute(source, "NewTable");

            Assert.Contains("public long CID { get; set; }", mappedSource);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int startIndex = 0;
            while ((startIndex = text.IndexOf(value, startIndex, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += value.Length;
            }

            return count;
        }
    }
}

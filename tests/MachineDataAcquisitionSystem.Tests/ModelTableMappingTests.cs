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

            Assert.Equal(source, mappedSource);
        }
    }
}

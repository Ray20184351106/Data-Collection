using System.Collections.Generic;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ModelSchemaServiceTests
    {
        [Fact]
        public void Published_schema_guard_treats_an_additive_field_as_hash_changing()
        {
            var existing = new List<ModelSchemaField>
            {
                Field("SerialNumber", "string", false)
            };
            var proposed = new List<ModelSchemaField>
            {
                Field("SerialNumber", "string", false),
                Field("MeasuredAt", "datetime", false)
            };

            Assert.True(ModelSchemaService.HasDestructiveChange(existing, proposed));
        }

        [Fact]
        public void Published_schema_guard_ignores_description_only_changes()
        {
            ModelSchemaField existing = Field("SerialNumber", "string", true);
            existing.Description = "old";
            ModelSchemaField proposed = Field("SerialNumber", "string", true);
            proposed.Description = "new";

            Assert.False(ModelSchemaService.HasDestructiveChange(
                new[] { existing },
                new[] { proposed }));
        }

        private static ModelSchemaField Field(string name, string type, bool required)
        {
            return new ModelSchemaField
            {
                FieldName = name,
                FieldType = type,
                IsRequired = required
            };
        }
    }
}

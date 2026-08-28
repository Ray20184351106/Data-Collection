using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Mapping;

namespace MachineDataAcquisitionSystem.Models
{
    public class ParseScript
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ModelId { get; set; }
        public string FileExtension { get; set; }
        public string ScriptCode { get; set; }
        public bool IsEnabled { get; set; }
        public long? ParserVersionId { get; set; }
        public string ContentSha256 { get; set; }
        public string ModelSchemaHash { get; set; }
        public ParseRuleType RuleType { get; set; }
        public string TargetModelType { get; set; }
        public string DefinitionJson { get; set; }
        public string GeneratedModelCodeSnapshot { get; set; }
        public string GeneratedModelCodeSha256 { get; set; }
        public List<ParseScriptModelSnapshot> ModelSnapshots { get; set; } =
            new List<ParseScriptModelSnapshot>();
    }

    public sealed class ParseScriptModelSnapshot
    {
        public int ModelId { get; set; }
        public string ModelType { get; set; }
        public string ModelSchemaHash { get; set; }
        public string GeneratedModelCodeSnapshot { get; set; }
        public string GeneratedModelCodeSha256 { get; set; }
    }
}

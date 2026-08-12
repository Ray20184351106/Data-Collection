using System;
using System.Collections.Generic;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class MappingRuleDefinition
    {
        public MappingRuleDefinition()
        {
            Fields = new List<FieldMappingRule>();
        }

        public long DefinitionId { get; set; }
        public string RuleName { get; set; }
        public int ModelId { get; set; }
        public string TargetModelType { get; set; }
        public string ModelSchemaHash { get; set; }
        public string NormalizedExtension { get; set; }
        public string SheetName { get; set; }
        public string TemplateSignature { get; set; }
        public MappingRecordMode RecordMode { get; set; }
        public RepeatedRowDefinition RepeatedRows { get; set; }
        public List<FieldMappingRule> Fields { get; set; }
    }

    public enum MappingRecordMode
    {
        SingleRecord = 0,
        RepeatingRows = 1
    }

    public enum MappingFieldScope
    {
        Common = 0,
        RowColumn = 1
    }

    public enum MappingTableAnchorMode
    {
        HeaderText = 0,
        FixedCell = 1
    }

    public sealed class RepeatedRowDefinition
    {
        public MappingTableAnchorMode AnchorMode { get; set; }
        public string AnchorText { get; set; }
        public string AnchorCell { get; set; }
        public int FirstDataRowOffset { get; set; }
        public int KeyColumnOffset { get; set; }
        public int FirstColumnOffset { get; set; }
        public int LastColumnOffset { get; set; }
        public bool StopOnBlankKey { get; set; }
    }

    public sealed class FieldMappingRule
    {
        public FieldMappingRule()
        {
            Transforms = new List<string>();
        }

        public string TargetField { get; set; }
        public string TargetType { get; set; }
        public string TargetDescription { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        public MappingFieldScope Scope { get; set; }
        public MappingLocator Locator { get; set; }
        public List<string> Transforms { get; set; }
        public Dictionary<string, string> ExactValueMap { get; set; }
        public MappingConfirmationState ConfirmationState { get; set; }
    }

    public sealed class MappingLocator
    {
        public string Type { get; set; }
        public string Cell { get; set; }
        public string AnchorCell { get; set; }
        public string AnchorText { get; set; }
        public string Text { get; set; }
        public int RowOffset { get; set; }
        public int ColumnOffset { get; set; }
        public string ValueColumn { get; set; }
        public int DataRowOffset { get; set; }
    }

    public enum MappingConfirmationState
    {
        Unconfirmed = 0,
        LocalCandidate = 1,
        AiPendingConfirmation = 2,
        HumanConfirmed = 3
    }

    public enum ParseRuleStatus
    {
        Draft = 0,
        Validated = 1,
        Published = 2,
        Superseded = 3
    }

    public enum ParseRuleType
    {
        Mapping = 0,
        LegacyCode = 1
    }

    public sealed class ParseRuleVersion
    {
        public long Id { get; set; }
        public long DefinitionId { get; set; }
        public int VersionNumber { get; set; }
        public int Revision { get; set; }
        public ParseRuleType RuleType { get; set; }
        public ParseRuleStatus Status { get; set; }
        public string RuleName { get; set; }
        public int ModelId { get; set; }
        public string TargetModelType { get; set; }
        public string NormalizedExtension { get; set; }
        public string DefinitionJson { get; set; }
        public string DerivedScriptCode { get; set; }
        public string ContentSha256 { get; set; }
        public string ModelSchemaHash { get; set; }
        public string ValidationSummary { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? ValidatedTime { get; set; }
        public DateTime? PublishedTime { get; set; }
    }

    public sealed class MappingPreviewResult
    {
        public MappingPreviewResult()
        {
            ErrorCodes = new List<string>();
            WarningCodes = new List<string>();
            Fields = new Dictionary<string, MappingPreviewFieldResult>(StringComparer.Ordinal);
            Records = new List<MappingPreviewRecordResult>();
        }

        public bool IsValid { get; set; }
        public string SampleSha256 { get; set; }
        public string TemplateSignature { get; set; }
        public List<string> ErrorCodes { get; set; }
        public List<string> WarningCodes { get; set; }
        public Dictionary<string, MappingPreviewFieldResult> Fields { get; set; }
        public List<MappingPreviewRecordResult> Records { get; set; }
    }

    public sealed class MappingPreviewRecordResult
    {
        public MappingPreviewRecordResult()
        {
            Fields = new Dictionary<string, MappingPreviewFieldResult>(StringComparer.Ordinal);
        }

        public int ExcelRowNumber { get; set; }
        public bool IsValid { get; set; }
        public Dictionary<string, MappingPreviewFieldResult> Fields { get; set; }
    }

    public sealed class MappingPreviewFieldResult
    {
        public string TargetField { get; set; }
        public string SourceCell { get; set; }
        public object RawValue { get; set; }
        public object Value { get; set; }
        public string ErrorCode { get; set; }
        public string WarningCode { get; set; }
    }

    public sealed class MappingWorkbookSnapshot
    {
        public MappingWorkbookSnapshot()
        {
            Sheets = new List<MappingSheetSnapshot>();
        }

        public string FileExtension { get; set; }
        public string FileSha256 { get; set; }
        public List<MappingSheetSnapshot> Sheets { get; set; }
    }

    public sealed class MappingSheetSnapshot
    {
        public MappingSheetSnapshot()
        {
            Cells = new List<MappingCellSnapshot>();
            MergedRegions = new List<string>();
        }

        public string Name { get; set; }
        public List<MappingCellSnapshot> Cells { get; set; }
        public List<string> MergedRegions { get; set; }
    }

    public sealed class MappingCellSnapshot
    {
        public string Coordinate { get; set; }
        public string DisplayText { get; set; }
        public string ValueType { get; set; }
        public bool IsFormula { get; set; }
        public bool FormulaCacheMissing { get; set; }
    }

    public sealed class AiMappingRequest
    {
        public AiMappingRequest()
        {
            Sheets = new List<AiSheetSummary>();
            Targets = new List<AiTargetField>();
            AllowedLocators = new List<string>();
            AllowedTransforms = new List<string>();
        }

        public string SchemaVersion { get; set; }
        public string ModelSchemaHash { get; set; }
        public List<AiSheetSummary> Sheets { get; set; }
        public List<AiTargetField> Targets { get; set; }
        public List<string> AllowedLocators { get; set; }
        public List<string> AllowedTransforms { get; set; }
    }

    public sealed class AiSheetSummary
    {
        public AiSheetSummary()
        {
            MergedRegions = new List<string>();
            Cells = new List<AiCellSummary>();
        }

        public string Name { get; set; }
        public List<string> MergedRegions { get; set; }
        public List<AiCellSummary> Cells { get; set; }
    }

    public sealed class AiCellSummary
    {
        public string Coordinate { get; set; }
        public string LabelText { get; set; }
        public string ValuePlaceholder { get; set; }
    }

    public sealed class AiTargetField
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public string Description { get; set; }
        public bool IsRequired { get; set; }
    }

    public sealed class AiMappingResponse
    {
        public AiMappingResponse()
        {
            Suggestions = new List<AiMappingSuggestion>();
            UnmappedTargets = new List<string>();
            Assumptions = new List<string>();
        }

        public string SchemaVersion { get; set; }
        public string ModelSchemaHash { get; set; }
        public List<AiMappingSuggestion> Suggestions { get; set; }
        public List<string> UnmappedTargets { get; set; }
        public List<string> Assumptions { get; set; }

        // Deliberately accepted for rejection at the trust boundary. It is never executed.
        public string ScriptCode { get; set; }
    }

    public sealed class AiMappingSuggestion
    {
        public AiMappingSuggestion()
        {
            Transforms = new List<string>();
        }

        public string TargetField { get; set; }
        public MappingLocator Locator { get; set; }
        public List<string> Transforms { get; set; }
        public decimal Confidence { get; set; }
        public string Reason { get; set; }
    }

    public sealed class AiMappingValidationResult
    {
        public AiMappingValidationResult()
        {
            ErrorCodes = new List<string>();
            AcceptedSuggestions = new List<AiMappingSuggestion>();
        }

        public bool IsValid { get; set; }
        public List<string> ErrorCodes { get; set; }
        public List<AiMappingSuggestion> AcceptedSuggestions { get; set; }
    }

    public sealed class ParseRuleBindingConflictException : InvalidOperationException
    {
        public ParseRuleBindingConflictException(string message) : base(message) { }
    }

    public sealed class ParseRuleRevisionConflictException : InvalidOperationException
    {
        public ParseRuleRevisionConflictException(string message) : base(message) { }
    }

    public sealed class ParseRuleStateException : InvalidOperationException
    {
        public ParseRuleStateException(string message) : base(message) { }
    }

    public sealed class MappingValidationException : InvalidOperationException
    {
        public MappingValidationException(string message) : base(message) { }
    }
}

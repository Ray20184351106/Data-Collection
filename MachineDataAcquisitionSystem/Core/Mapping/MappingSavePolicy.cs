using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public static class MappingSavePolicy
    {
        public static string FormatValidationFailure(MappingRuleDefinition definition, MappingPreviewResult preview)
        {
            var messages = new List<string>();
            bool masterDetail = definition.RecordMode == MappingRecordMode.MasterDetail;
            AppendFieldErrors(messages, preview.Fields.Values,
                masterDetail ? definition.MasterDetail.Master.Fields : definition.Fields,
                masterDetail ? "主表" : "公共字段");
            foreach (var record in preview.Records)
                AppendFieldErrors(messages, record.Fields.Values,
                    masterDetail ? definition.MasterDetail.Detail.Fields : definition.Fields,
                    (masterDetail ? "子表" : "数据") + "第" + record.ExcelRowNumber + "行");

            string summary = "样本自动验证未通过：" + string.Join(", ", preview.ErrorCodes);
            if (messages.Count == 0) return summary;
            summary += Environment.NewLine + string.Join(Environment.NewLine, messages.Take(8));
            if (messages.Count > 8)
                summary += Environment.NewLine + "另有 " + (messages.Count - 8) + " 项错误，请在“采集结果”中查看。";
            return summary;
        }

        private static void AppendFieldErrors(List<string> messages,
            IEnumerable<MappingPreviewFieldResult> fields, IEnumerable<FieldMappingRule> rules, string scope)
        {
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.ErrorCode)) continue;
                var rule = rules.FirstOrDefault(item => item.TargetField == field.TargetField);
                string name = field.TargetField;
                if (!string.IsNullOrWhiteSpace(rule?.TargetDescription)) name += "（" + Brief(rule.TargetDescription) + "）";
                string message = scope + "，字段“" + name + "”，来源“" + Brief(field.SourceCell) + "”，原值“" + Brief(field.RawValue) + "”：";
                if (field.ErrorCode == "CONVERSION_FAILED")
                {
                    message += "无法转换为目标类型 " + (rule?.TargetType ?? "未知") + "，或配置的转换步骤执行失败。请检查字段定位、值格式及数值范围。";
                    if (rule?.Transforms != null && rule.Transforms.Count > 0)
                        message += " 转换步骤：" + string.Join(",", rule.Transforms) + "。";
                    if (!string.IsNullOrEmpty(rule?.DefaultValue))
                        message += " 默认值：“" + Brief(rule.DefaultValue) + "”。";
                }
                else message += field.ErrorCode;
                messages.Add(message);
            }
        }

        private static string Brief(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            text = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return text.Length <= 100 ? text : text.Substring(0, 100) + "…";
        }

        public static bool RequiresSample(
            ParseRuleVersion currentVersion,
            bool hasContentChanges)
        {
            if (currentVersion == null || hasContentChanges)
                return true;

            return currentVersion.Status != ParseRuleStatus.Validated &&
                   currentVersion.Status != ParseRuleStatus.Published;
        }

        public static ParseRuleVersion SelectPreferredEditorVersion(
            IEnumerable<ParseRuleVersion> versions)
        {
            ParseRuleVersion latest = null;
            ParseRuleVersion latestReusable = null;
            if (versions == null) return null;

            foreach (ParseRuleVersion version in versions)
            {
                if (version == null) continue;
                if (latest == null || version.VersionNumber > latest.VersionNumber)
                    latest = version;

                if ((version.Status == ParseRuleStatus.Validated ||
                     version.Status == ParseRuleStatus.Published) &&
                    (latestReusable == null || version.VersionNumber > latestReusable.VersionNumber))
                {
                    latestReusable = version;
                }
            }

            return latestReusable ?? latest;
        }
    }
}

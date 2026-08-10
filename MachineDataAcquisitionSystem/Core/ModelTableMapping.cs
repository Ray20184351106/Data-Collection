using System;

namespace MachineDataAcquisitionSystem.Core
{
    public static class ModelTableMapping
    {
        public static string GetSqlSugarTableAttribute(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("The database table name is required.", nameof(tableName));

            return "[SqlSugar.SugarTable(\"" + EscapeCSharpString(tableName.Trim()) + "\")]";
        }

        public static string ApplySqlSugarTableAttribute(string modelSource, string tableName)
        {
            if (string.IsNullOrWhiteSpace(modelSource))
                throw new ArgumentException("The generated model source is required.", nameof(modelSource));

            string attribute = GetSqlSugarTableAttribute(tableName);
            int classIndex = modelSource.IndexOf("public class", StringComparison.Ordinal);
            if (classIndex < 0)
                throw new InvalidOperationException("The generated model source does not contain a public class.");

            string beforeClass = modelSource.Substring(0, classIndex);
            int attributeStart = beforeClass.LastIndexOf("[SqlSugar.SugarTable(", StringComparison.Ordinal);
            if (attributeStart < 0)
                attributeStart = beforeClass.LastIndexOf("[SugarTable(", StringComparison.Ordinal);
            if (attributeStart >= 0)
            {
                int attributeEnd = modelSource.IndexOf(']', attributeStart);
                if (attributeEnd < 0 || attributeEnd > classIndex)
                    throw new InvalidOperationException("The generated model source contains an incomplete SugarTable attribute.");
                return modelSource.Substring(0, attributeStart) +
                    attribute +
                    modelSource.Substring(attributeEnd + 1);
            }

            return modelSource.Insert(classIndex, attribute + Environment.NewLine);
        }

        private static string EscapeCSharpString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}

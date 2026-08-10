using System;
using System.Text.RegularExpressions;

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

        public static string GetSqlSugarColumnAttribute(
            int fieldLength,
            bool isRequired,
            bool isPrimaryKey,
            bool isIdentity,
            string description)
        {
            bool isNullable = !isRequired && !isPrimaryKey;
            return "[SqlSugar.SugarColumn(" +
                "IsNullable = " + BoolLiteral(isNullable) +
                ", IsPrimaryKey = " + BoolLiteral(isPrimaryKey) +
                ", IsIdentity = " + BoolLiteral(isIdentity) +
                ", Length = " + Math.Max(0, fieldLength) +
                ", ColumnDescription = \"" + EscapeCSharpString(description ?? string.Empty) + "\")]";
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
            string mappedSource;
            if (attributeStart >= 0)
            {
                int attributeEnd = modelSource.IndexOf(']', attributeStart);
                if (attributeEnd < 0 || attributeEnd > classIndex)
                    throw new InvalidOperationException("The generated model source contains an incomplete SugarTable attribute.");
                mappedSource = modelSource.Substring(0, attributeStart) +
                    attribute +
                    modelSource.Substring(attributeEnd + 1);
            }
            else
            {
                mappedSource = modelSource.Insert(classIndex, attribute + Environment.NewLine);
            }

            return EnsureCidProperty(mappedSource);
        }

        /// <summary>
        /// Legacy model snapshots may predate the mandatory snowflake-key field.
        /// Add it before compiling so the batch writer can provide CID explicitly.
        /// </summary>
        private static string EnsureCidProperty(string modelSource)
        {
            if (Regex.IsMatch(modelSource, @"\bCID\s*\{", RegexOptions.CultureInvariant))
                return modelSource;

            int classIndex = modelSource.IndexOf("public class", StringComparison.Ordinal);
            int openBraceIndex = modelSource.IndexOf('{', classIndex);
            if (openBraceIndex < 0)
                throw new InvalidOperationException("The generated model source does not contain a class body.");

            int depth = 0;
            for (int index = openBraceIndex; index < modelSource.Length; index++)
            {
                if (modelSource[index] == '{') depth++;
                if (modelSource[index] == '}' && --depth == 0)
                {
                    const string cidProperty = "\r\n        public long CID { get; set; }\r\n";
                    return modelSource.Insert(index, cidProperty);
                }
            }

            throw new InvalidOperationException("The generated model source contains an incomplete class body.");
        }

        private static string EscapeCSharpString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string BoolLiteral(bool value)
        {
            return value ? "true" : "false";
        }
    }
}

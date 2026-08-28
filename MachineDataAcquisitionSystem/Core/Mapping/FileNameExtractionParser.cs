using System;
using System.Collections.Generic;
using System.IO;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class FileNameExtractionResult
    {
        public FileNameExtractionResult(string fullName, string stem, IReadOnlyList<string> segments)
        {
            FullName = fullName;
            Stem = stem;
            Segments = segments;
        }

        public string FullName { get; private set; }
        public string Stem { get; private set; }
        public IReadOnlyList<string> Segments { get; private set; }
    }

    public static class FileNameExtractionParser
    {
        public const int MaximumSegments = 64;

        public static FileNameExtractionResult Parse(
            string filePath,
            FileNameExtractionDefinition definition)
        {
            if (definition == null)
                throw new MappingValidationException("缺少文件名解析配置。");
            if (definition.ExpectedSegmentCount < 0 ||
                definition.ExpectedSegmentCount > MaximumSegments)
                throw new MappingValidationException("文件名片段数量超出允许范围。");

            FileNameExtractionResult result = Inspect(filePath);
            if (result.Segments.Count != definition.ExpectedSegmentCount)
                throw new MappingValidationException(
                    string.Format(
                        "文件名括号片段数量不匹配：期望 {0} 个，实际 {1} 个。",
                        definition.ExpectedSegmentCount,
                        result.Segments.Count));
            return result;
        }

        public static FileNameExtractionResult Inspect(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new MappingValidationException("文件路径不能为空。");
            string fullName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fullName))
                throw new MappingValidationException("文件名不能为空。");
            string stem = Path.GetFileNameWithoutExtension(fullName);
            var segments = new List<string>();
            char expectedClose = '\0';
            int contentStart = -1;

            for (int index = 0; index < stem.Length; index++)
            {
                char current = stem[index];
                bool isOpen = current == '[' || current == '【';
                bool isClose = current == ']' || current == '】';
                if (expectedClose == '\0')
                {
                    if (isClose)
                        throw new MappingValidationException("文件名括号不完整。");
                    if (!isOpen) continue;
                    expectedClose = current == '[' ? ']' : '】';
                    contentStart = index + 1;
                    continue;
                }

                if (isOpen)
                    throw new MappingValidationException("文件名括号不能嵌套。");
                if (!isClose) continue;
                if (current != expectedClose)
                    throw new MappingValidationException("文件名括号格式不匹配。");

                string segment = stem.Substring(contentStart, index - contentStart);
                if (string.IsNullOrWhiteSpace(segment))
                    throw new MappingValidationException("文件名括号片段不能为空。");
                segments.Add(segment);
                if (segments.Count > MaximumSegments)
                    throw new MappingValidationException("文件名片段数量超出允许范围。");
                expectedClose = '\0';
                contentStart = -1;
            }

            if (expectedClose != '\0')
                throw new MappingValidationException("文件名括号不完整。");
            return new FileNameExtractionResult(fullName, stem, segments.AsReadOnly());
        }
    }
}

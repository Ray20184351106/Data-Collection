using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ImageFileMappingService
    {
        public MappingWorkbookSnapshot Inspect(string filePath)
        {
            string extension = MappingRuleSerializer.NormalizeExtension(Path.GetExtension(filePath));
            ImageFileValidator.Validate(filePath, extension);
            return new MappingWorkbookSnapshot
            {
                FileExtension = extension,
                FileSha256 = MappingRuleSerializer.FileSha256(filePath)
            };
        }

        public MappingPreviewResult Preview(string filePath, MappingRuleDefinition rule)
        {
            var result = new MappingPreviewResult();
            try
            {
                MappingRuleSerializer.ValidateDefinition(rule);
                ImageFileValidator.Validate(filePath, rule.NormalizedExtension);
                result.SampleSha256 = MappingRuleSerializer.FileSha256(filePath);
                FileNameExtractionResult extracted = FileNameExtractionParser.Parse(
                    filePath,
                    rule.ImageArchive.FileName);

                foreach (FieldMappingRule field in rule.Fields)
                    ResolveField(extracted, field, result);

                result.TemplateSignature = BuildTemplateSignature(rule);
                if (!string.IsNullOrWhiteSpace(rule.TemplateSignature) &&
                    !string.Equals(rule.TemplateSignature, result.TemplateSignature, StringComparison.Ordinal))
                    AddError(result, "TEMPLATE_SIGNATURE_MISMATCH");
            }
            catch (MappingValidationException ex)
            {
                AddError(result, ex.Message);
            }
            catch (IOException)
            {
                AddError(result, "IMAGE_READ_FAILED");
            }
            catch (UnauthorizedAccessException)
            {
                AddError(result, "IMAGE_ACCESS_DENIED");
            }
            result.IsValid = result.ErrorCodes.Count == 0;
            return result;
        }

        private static void ResolveField(
            FileNameExtractionResult extracted,
            FieldMappingRule field,
            MappingPreviewResult result)
        {
            object rawValue;
            string source;
            switch (field.Locator.Type)
            {
                case "fileNameFull":
                    rawValue = extracted.FullName;
                    source = "完整文件名";
                    break;
                case "fileNameStem":
                    rawValue = extracted.Stem;
                    source = "无扩展名文件名";
                    break;
                case "fileNameSegment":
                    rawValue = extracted.Segments[field.Locator.SegmentIndex];
                    source = "文件名片段" + (field.Locator.SegmentIndex + 1)
                        .ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new MappingValidationException("IMAGE_FILENAME_LOCATOR_INVALID");
            }

            var fieldResult = new MappingPreviewFieldResult
            {
                TargetField = field.TargetField,
                SourceCell = source,
                RawValue = rawValue
            };
            result.Fields[field.TargetField] = fieldResult;
            try
            {
                object transformed = ExcelMappingPreviewService.ApplyTransforms(rawValue, field);
                if (ExcelMappingPreviewService.IsMissing(transformed) && field.IsRequired)
                {
                    fieldResult.ErrorCode = "MISSING_REQUIRED";
                    AddError(result, fieldResult.ErrorCode);
                    return;
                }
                fieldResult.Value = ExcelMappingPreviewService.ConvertToTargetType(transformed, field.TargetType);
            }
            catch (FormatException)
            {
                fieldResult.ErrorCode = "CONVERSION_FAILED";
                AddError(result, fieldResult.ErrorCode);
            }
            catch (OverflowException)
            {
                fieldResult.ErrorCode = "CONVERSION_FAILED";
                AddError(result, fieldResult.ErrorCode);
            }
            catch (InvalidCastException)
            {
                fieldResult.ErrorCode = "CONVERSION_FAILED";
                AddError(result, fieldResult.ErrorCode);
            }
        }

        private static string BuildTemplateSignature(MappingRuleDefinition rule)
        {
            string fields = string.Join("\n", rule.Fields
                .OrderBy(field => field.TargetField, StringComparer.Ordinal)
                .Select(field => string.Join("|", new[]
                {
                    field.TargetField,
                    field.Locator.Type ?? string.Empty,
                    field.Locator.SegmentIndex.ToString(CultureInfo.InvariantCulture)
                })));
            return MappingRuleSerializer.Sha256(string.Join("\n", new[]
            {
                "imageFileName",
                rule.NormalizedExtension,
                rule.ImageArchive.FileName.ExpectedSegmentCount.ToString(CultureInfo.InvariantCulture),
                fields
            }));
        }

        private static void AddError(MappingPreviewResult result, string errorCode)
        {
            string code = string.IsNullOrWhiteSpace(errorCode) ? "IMAGE_MAPPING_INVALID" : errorCode;
            if (!result.ErrorCodes.Contains(code, StringComparer.Ordinal))
                result.ErrorCodes.Add(code);
        }
    }

    internal static class ImageFileValidator
    {
        public const long MaximumImageBytes = 50L * 1024L * 1024L;

        public static void Validate(string filePath, string expectedExtension)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new MappingValidationException("IMAGE_NOT_FOUND");
            string fullPath = Path.GetFullPath(filePath);
            string extension = MappingRuleSerializer.NormalizeExtension(Path.GetExtension(fullPath));
            if (!string.Equals(extension, expectedExtension, StringComparison.Ordinal))
                throw new MappingValidationException("EXTENSION_MISMATCH");
            var info = new FileInfo(fullPath);
            if (info.Length == 0 || info.Length > MaximumImageBytes)
                throw new MappingValidationException("IMAGE_SIZE_LIMIT");
            if (!HasExpectedSignature(fullPath, extension))
                throw new MappingValidationException("IMAGE_SIGNATURE_MISMATCH");
        }

        private static bool HasExpectedSignature(string filePath, string extension)
        {
            byte[] header = new byte[8];
            int read;
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                read = stream.Read(header, 0, header.Length);
            if ((extension == ".jpg" || extension == ".jpeg") && read >= 3)
                return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            if (extension == ".png" && read >= 8)
                return header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            if (extension == ".bmp" && read >= 2)
                return header[0] == 0x42 && header[1] == 0x4D;
            return false;
        }
    }
}

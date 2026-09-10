using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class CsvMappingPreviewService
    {
        public const string VirtualSheetName = "CSV";

        private const FileShare SharedEditorAccess = FileShare.ReadWrite | FileShare.Delete;
        private readonly string[] _forbiddenRoots;

        static CsvMappingPreviewService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public CsvMappingPreviewService(IEnumerable<string> forbiddenRoots = null)
        {
            _forbiddenRoots = (forbiddenRoots ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .ToArray();
        }

        public MappingWorkbookSnapshot Inspect(string filePath, CsvMappingOptions options)
        {
            ValidatePath(filePath);
            ValidateOptions(options);
            ParsedCsvDocument document = ReadDocument(filePath, options);
            return CreateSnapshot(filePath, document, options);
        }

        public MappingPreviewResult Preview(string filePath, MappingRuleDefinition rule)
        {
            string workbookPath = null;
            try
            {
                MappingRuleSerializer.ValidateDefinition(rule);
                if (!string.Equals(rule.NormalizedExtension, ".csv", StringComparison.Ordinal))
                    throw new MappingValidationException("EXTENSION_MISMATCH");

                MappingWorkbookSnapshot snapshot = Inspect(filePath, rule.CsvOptions);
                workbookPath = WriteWorkbookSnapshot(snapshot);
                MappingRuleDefinition workbookRule = CreateWorkbookRule(rule, snapshot);
                MappingPreviewResult result = new ExcelMappingPreviewService()
                    .PreviewPreparedWorkbook(
                        workbookPath,
                        filePath,
                        workbookRule,
                        rule.CsvOptions.SkipBlankRows);

                result.SourceFormat = "CSV";
                result.SampleSha256 = snapshot.FileSha256;
                result.TemplateSignature = BuildTemplateSignature(
                    rule.CsvOptions,
                    result.TemplateSignature);
                if (!string.IsNullOrWhiteSpace(rule.TemplateSignature) &&
                    !string.Equals(rule.TemplateSignature, result.TemplateSignature, StringComparison.Ordinal))
                    AddError(result, "TEMPLATE_SIGNATURE_MISMATCH");
                result.IsValid = result.ErrorCodes.Count == 0;
                return result;
            }
            catch (MappingValidationException ex)
            {
                return Invalid(ClassifyValidationError(ex.Message));
            }
            catch (DecoderFallbackException)
            {
                return Invalid("CSV_ENCODING_INVALID");
            }
            catch (IOException)
            {
                return Invalid("SAMPLE_READ_FAILED");
            }
            catch (UnauthorizedAccessException)
            {
                return Invalid("SAMPLE_ACCESS_DENIED");
            }
            catch (Exception)
            {
                return Invalid("INVALID_CSV");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(workbookPath) && File.Exists(workbookPath))
                    File.Delete(workbookPath);
            }
        }

        private void ValidatePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new MappingValidationException("SAMPLE_NOT_FOUND");

            string fullPath = Path.GetFullPath(filePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".csv", StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("EXTENSION_MISMATCH");
            if (_forbiddenRoots.Any(root => IsUnderRoot(fullPath, root)))
                throw new MappingValidationException("ACQUISITION_DIRECTORY_FORBIDDEN");

            var info = new FileInfo(fullPath);
            if (info.Length == 0 || info.Length > ExcelMappingPreviewService.MaximumFileBytes)
                throw new MappingValidationException("FILE_SIZE_LIMIT");
        }

        private static void ValidateOptions(CsvMappingOptions options)
        {
            var rule = new MappingRuleDefinition
            {
                RuleName = "csv-options-validation",
                ModelId = 1,
                TargetModelType = "CsvOptionsValidation",
                ModelSchemaHash = "validation",
                NormalizedExtension = ".csv",
                SheetName = VirtualSheetName,
                CsvOptions = options,
                RecordMode = MappingRecordMode.SingleRecord,
                Fields = new List<FieldMappingRule>
                {
                    new FieldMappingRule
                    {
                        TargetField = "Value",
                        TargetType = "string",
                        Locator = new MappingLocator
                        {
                            Type = "cell",
                            Cell = "A2",
                            AnchorCell = "A1",
                            AnchorText = "Header"
                        }
                    }
                }
            };
            MappingRuleSerializer.ValidateDefinition(rule);
        }

        private static ParsedCsvDocument ReadDocument(string filePath, CsvMappingOptions options)
        {
            try
            {
                Encoding encoding = CreateEncoding(options.EncodingName);
                using (FileStream stream = File.Open(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    SharedEditorAccess))
                {
                    SkipMatchingBomOrRejectMismatch(stream, encoding);
                    using (var reader = new StreamReader(stream, encoding, false, 4096, false))
                    {
                        List<ParsedCsvRow> rows = ParseRows(
                            reader,
                            options.Delimiter[0],
                            options.QuoteCharacter);
                        return ValidateStructure(rows, options);
                    }
                }
            }
            catch (DecoderFallbackException)
            {
                throw new MappingValidationException("CSV_ENCODING_INVALID");
            }
        }

        private static Encoding CreateEncoding(string name)
        {
            string normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "utf-8" || normalized == "utf8")
                return new UTF8Encoding(false, true);
            if (normalized == "gb18030")
                return Encoding.GetEncoding(
                    "GB18030",
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
            if (normalized == "gbk")
                return Encoding.GetEncoding(
                    936,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
            throw new MappingValidationException("INVALID_CSV_OPTIONS");
        }

        private static void SkipMatchingBomOrRejectMismatch(FileStream stream, Encoding encoding)
        {
            var prefix = new byte[4];
            int count = stream.Read(prefix, 0, prefix.Length);
            stream.Position = 0;

            bool utf8Bom = count >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF;
            bool unsupportedBom = count >= 2 &&
                ((prefix[0] == 0xFF && prefix[1] == 0xFE) ||
                 (prefix[0] == 0xFE && prefix[1] == 0xFF)) ||
                count >= 4 && prefix[0] == 0x00 && prefix[1] == 0x00 &&
                prefix[2] == 0xFE && prefix[3] == 0xFF;
            if (utf8Bom)
            {
                if (encoding.CodePage != Encoding.UTF8.CodePage)
                    throw new MappingValidationException("CSV_ENCODING_INVALID");
                stream.Position = 3;
                return;
            }
            if (unsupportedBom)
                throw new MappingValidationException("CSV_ENCODING_INVALID");
        }

        private static List<ParsedCsvRow> ParseRows(TextReader reader, char delimiter, char quote)
        {
            var rows = new List<ParsedCsvRow>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool closedQuote = false;
            bool firstCharacter = true;
            int logicalRowNumber = 1;

            while (true)
            {
                int read = reader.Read();
                if (read < 0) break;
                char current = (char)read;
                if (firstCharacter)
                {
                    firstCharacter = false;
                    if (current == '\uFEFF')
                        continue;
                }

                if (inQuotes)
                {
                    if (current == quote)
                    {
                        if (reader.Peek() == quote)
                        {
                            reader.Read();
                            field.Append(quote);
                        }
                        else
                        {
                            inQuotes = false;
                            closedQuote = true;
                        }
                    }
                    else
                    {
                        field.Append(current);
                    }
                    continue;
                }

                if (closedQuote)
                {
                    if (current == delimiter)
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        closedQuote = false;
                        continue;
                    }
                    if (current == '\r' || current == '\n')
                    {
                        if (current == '\r' && reader.Peek() == '\n') reader.Read();
                        fields.Add(field.ToString());
                        rows.Add(new ParsedCsvRow(logicalRowNumber++, fields));
                        fields = new List<string>();
                        field.Clear();
                        closedQuote = false;
                        continue;
                    }
                    throw new MappingValidationException("INVALID_CSV");
                }

                if (current == quote)
                {
                    if (field.Length != 0)
                        throw new MappingValidationException("INVALID_CSV");
                    inQuotes = true;
                }
                else if (current == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (current == '\r' || current == '\n')
                {
                    if (current == '\r' && reader.Peek() == '\n') reader.Read();
                    fields.Add(field.ToString());
                    rows.Add(new ParsedCsvRow(logicalRowNumber++, fields));
                    fields = new List<string>();
                    field.Clear();
                }
                else
                {
                    field.Append(current);
                }
            }

            if (inQuotes)
                throw new MappingValidationException("INVALID_CSV");
            if (closedQuote || fields.Count > 0 || field.Length > 0)
            {
                fields.Add(field.ToString());
                rows.Add(new ParsedCsvRow(logicalRowNumber, fields));
            }
            return rows;
        }

        private static ParsedCsvDocument ValidateStructure(
            IList<ParsedCsvRow> rows,
            CsvMappingOptions options)
        {
            if (rows.Count == 0)
                throw new MappingValidationException("NO_RECORDS");
            if (rows.Count > ExcelMappingPreviewService.MaximumRows)
                throw new MappingValidationException("ROW_COUNT_LIMIT");

            ParsedCsvRow header = rows.FirstOrDefault(row => row.RowNumber == options.HeaderRowNumber);
            if (header == null || header.Fields.Count == 0)
                throw new MappingValidationException("CSV_HEADER_NOT_FOUND");
            if (header.Fields.Count > ExcelMappingPreviewService.MaximumColumns)
                throw new MappingValidationException("COLUMN_COUNT_LIMIT");

            var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in header.Fields)
            {
                string normalized = NormalizeCsvText(value);
                if (normalized.Length == 0)
                    throw new MappingValidationException("EMPTY_CSV_HEADER");
                if (!headers.Add(normalized))
                    throw new MappingValidationException("DUPLICATE_CSV_HEADER");
            }

            int nonEmptyCells = 0;
            foreach (ParsedCsvRow row in rows)
            {
                if (row.Fields.Count > ExcelMappingPreviewService.MaximumColumns)
                    throw new MappingValidationException("COLUMN_COUNT_LIMIT");
                bool blank = row.Fields.All(string.IsNullOrWhiteSpace);
                if (row.RowNumber >= options.FirstDataRowNumber && !(blank && options.SkipBlankRows) &&
                    row.Fields.Count != header.Fields.Count)
                    throw new MappingValidationException("CSV_COLUMN_COUNT_MISMATCH");

                foreach (string value in row.Fields)
                {
                    if ((value ?? string.Empty).Length > ExcelMappingPreviewService.MaximumCellTextLength)
                        throw new MappingValidationException("CELL_TEXT_LIMIT");
                    if (!string.IsNullOrWhiteSpace(value) &&
                        ++nonEmptyCells > ExcelMappingPreviewService.MaximumNonEmptyCells)
                        throw new MappingValidationException("NON_EMPTY_CELL_LIMIT");
                }
            }

            if (!rows.Any(row => row.RowNumber >= options.FirstDataRowNumber &&
                                  !(options.SkipBlankRows && row.Fields.All(string.IsNullOrWhiteSpace))))
                throw new MappingValidationException("NO_RECORDS");
            return new ParsedCsvDocument(rows.ToList(), header.Fields.Count);
        }

        private static MappingWorkbookSnapshot CreateSnapshot(
            string filePath,
            ParsedCsvDocument document,
            CsvMappingOptions options)
        {
            var sheet = new MappingSheetSnapshot { Name = VirtualSheetName };
            foreach (ParsedCsvRow row in document.Rows)
            {
                if (options.SkipBlankRows && row.Fields.All(string.IsNullOrWhiteSpace))
                    continue;
                for (int columnIndex = 0; columnIndex < row.Fields.Count; columnIndex++)
                {
                    string value = row.Fields[columnIndex];
                    if (string.IsNullOrEmpty(value)) continue;
                    sheet.Cells.Add(new MappingCellSnapshot
                    {
                        Coordinate = new CellReference(row.RowNumber - 1, columnIndex).FormatAsString(),
                        DisplayText = value,
                        ValueType = "String"
                    });
                }
            }
            return new MappingWorkbookSnapshot
            {
                FileExtension = ".csv",
                FileSha256 = MappingRuleSerializer.FileSha256(filePath),
                Sheets = new List<MappingSheetSnapshot> { sheet }
            };
        }

        private static string WriteWorkbookSnapshot(MappingWorkbookSnapshot snapshot)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "CsvMappingWorkbook_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                using (var workbook = new XSSFWorkbook())
                {
                    var sheet = workbook.CreateSheet(VirtualSheetName);
                    foreach (MappingCellSnapshot cell in snapshot.Sheets[0].Cells)
                    {
                        var reference = new CellReference(cell.Coordinate);
                        var row = sheet.GetRow(reference.Row) ?? sheet.CreateRow(reference.Row);
                        row.CreateCell(reference.Col).SetCellValue(cell.DisplayText ?? string.Empty);
                    }
                    using (FileStream stream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        workbook.Write(stream);
                }
                return path;
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }

        private static MappingRuleDefinition CreateWorkbookRule(
            MappingRuleDefinition csvRule,
            MappingWorkbookSnapshot snapshot)
        {
            MappingRuleDefinition result = MappingRuleSerializer.Deserialize(
                MappingRuleSerializer.Serialize(csvRule));
            PrepareCsvTableAnchor(result, snapshot.Sheets[0], csvRule.CsvOptions);
            PrepareCsvHeaderColumnLocators(result, snapshot.Sheets[0], csvRule.CsvOptions);
            result.NormalizedExtension = ".xlsx";
            result.CsvOptions = null;
            result.TemplateSignature = null;
            return result;
        }

        private static void PrepareCsvTableAnchor(
            MappingRuleDefinition rule,
            MappingSheetSnapshot sheet,
            CsvMappingOptions options)
        {
            RepeatedRowDefinition rows = rule.RecordMode == MappingRecordMode.MasterDetail &&
                rule.MasterDetail != null && rule.MasterDetail.Detail != null
                ? rule.MasterDetail.Detail.RepeatedRows
                : rule.RepeatedRows;
            if (rows == null) return;

            int headerRowIndex = options.HeaderRowNumber - 1;
            if (rows.AnchorMode == MappingTableAnchorMode.FixedCell)
            {
                var reference = new CellReference(rows.AnchorCell);
                if (reference.Row != headerRowIndex)
                    throw new MappingValidationException("CSV_ROW_CONFIGURATION_MISMATCH");
                return;
            }

            List<MappingCellSnapshot> matches = sheet.Cells.Where(cell =>
            {
                var reference = new CellReference(cell.Coordinate);
                return reference.Row == headerRowIndex && string.Equals(
                    NormalizeCsvText(cell.DisplayText),
                    NormalizeCsvText(rows.AnchorText),
                    StringComparison.OrdinalIgnoreCase);
            }).ToList();
            if (matches.Count != 1)
                throw new MappingValidationException("CSV_HEADER_ANCHOR_NOT_FOUND");

            rows.AnchorMode = MappingTableAnchorMode.FixedCell;
            rows.AnchorCell = matches[0].Coordinate;
        }

        private static void PrepareCsvHeaderColumnLocators(
            MappingRuleDefinition rule,
            MappingSheetSnapshot sheet,
            CsvMappingOptions options)
        {
            IEnumerable<FieldMappingRule> fields = rule.RecordMode == MappingRecordMode.MasterDetail &&
                rule.MasterDetail != null && rule.MasterDetail.Detail != null
                ? rule.MasterDetail.Detail.Fields
                : rule.Fields;
            int headerRowIndex = options.HeaderRowNumber - 1;
            foreach (FieldMappingRule field in fields.Where(item =>
                item != null && item.Locator != null &&
                string.Equals(item.Locator.Type, "headerColumn", StringComparison.Ordinal)))
            {
                MappingLocator locator = field.Locator;
                List<MappingCellSnapshot> matches = sheet.Cells.Where(cell =>
                {
                    var reference = new CellReference(cell.Coordinate);
                    return reference.Row == headerRowIndex && string.Equals(
                        NormalizeCsvText(cell.DisplayText),
                        NormalizeCsvText(locator.Text),
                        StringComparison.OrdinalIgnoreCase);
                }).ToList();
                if (matches.Count != 1) continue;

                var header = new CellReference(matches[0].Coordinate);
                int valueRow = header.Row + locator.DataRowOffset;
                int valueColumn = header.Col + locator.ColumnOffset;
                if (valueRow < 0 || valueRow >= ExcelMappingPreviewService.MaximumRows ||
                    valueColumn < 0 || valueColumn >= ExcelMappingPreviewService.MaximumColumns)
                    continue;
                field.Locator = new MappingLocator
                {
                    Type = "cell",
                    Cell = new CellReference(valueRow, valueColumn).FormatAsString(),
                    AnchorCell = matches[0].Coordinate,
                    AnchorText = matches[0].DisplayText
                };
            }
        }

        private static string NormalizeCsvText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Normalize(NormalizationForm.FormKC).Trim();
        }

        private static string BuildTemplateSignature(CsvMappingOptions options, string workbookSignature)
        {
            string optionsIdentity = string.Join("|", new[]
            {
                (options.EncodingName ?? string.Empty).Trim().ToLowerInvariant(),
                options.Delimiter ?? string.Empty,
                options.QuoteCharacter.ToString(),
                options.HeaderRowNumber.ToString(CultureInfo.InvariantCulture),
                options.FirstDataRowNumber.ToString(CultureInfo.InvariantCulture),
                options.SkipBlankRows ? "1" : "0"
            });
            return MappingRuleSerializer.Sha256("csv\n" + optionsIdentity + "\n" + workbookSignature);
        }

        private static bool IsUnderRoot(string filePath, string root)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string ClassifyValidationError(string message)
        {
            string[] knownCodes =
            {
                "SAMPLE_NOT_FOUND", "EXTENSION_MISMATCH", "ACQUISITION_DIRECTORY_FORBIDDEN",
                "FILE_SIZE_LIMIT", "ROW_COUNT_LIMIT", "COLUMN_COUNT_LIMIT", "NON_EMPTY_CELL_LIMIT",
                "CELL_TEXT_LIMIT", "CSV_ENCODING_INVALID", "INVALID_CSV", "INVALID_CSV_OPTIONS",
                "CSV_HEADER_NOT_FOUND", "EMPTY_CSV_HEADER", "DUPLICATE_CSV_HEADER",
                "CSV_COLUMN_COUNT_MISMATCH", "CSV_ROW_CONFIGURATION_MISMATCH",
                "CSV_HEADER_ANCHOR_NOT_FOUND", "NO_RECORDS"
            };
            return knownCodes.Contains(message) ? message : "INVALID_RULE";
        }

        private static MappingPreviewResult Invalid(string code)
        {
            var result = new MappingPreviewResult { SourceFormat = "CSV" };
            result.ErrorCodes.Add(code);
            result.IsValid = false;
            return result;
        }

        private static void AddError(MappingPreviewResult result, string code)
        {
            if (!result.ErrorCodes.Contains(code)) result.ErrorCodes.Add(code);
        }

        private sealed class ParsedCsvDocument
        {
            public ParsedCsvDocument(IReadOnlyList<ParsedCsvRow> rows, int columnCount)
            {
                Rows = rows;
                ColumnCount = columnCount;
            }

            public IReadOnlyList<ParsedCsvRow> Rows { get; private set; }
            public int ColumnCount { get; private set; }
        }

        private sealed class ParsedCsvRow
        {
            public ParsedCsvRow(int rowNumber, IList<string> fields)
            {
                RowNumber = rowNumber;
                Fields = new List<string>(fields).AsReadOnly();
            }

            public int RowNumber { get; private set; }
            public IReadOnlyList<string> Fields { get; private set; }
        }
    }
}

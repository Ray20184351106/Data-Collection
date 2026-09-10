using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ExcelMappingPreviewService
    {
        private const FileShare SharedEditorAccess =
            FileShare.ReadWrite | FileShare.Delete;

        public const long MaximumFileBytes = 20L * 1024L * 1024L;
        public const int MaximumSheets = 10;
        public const int MaximumRows = 2000;
        public const int MaximumColumns = 128;
        public const int MaximumNonEmptyCells = 20000;
        public const int MaximumCellTextLength = 2048;

        private readonly string[] _forbiddenRoots;

        public ExcelMappingPreviewService(IEnumerable<string> forbiddenRoots = null)
        {
            _forbiddenRoots = (forbiddenRoots ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .ToArray();
        }

        public MappingPreviewResult Preview(string filePath, MappingRuleDefinition rule)
        {
            return PreviewCore(filePath, filePath, rule, false);
        }

        internal MappingPreviewResult PreviewPreparedWorkbook(
            string workbookPath,
            string logicalSourcePath,
            MappingRuleDefinition rule,
            bool skipFullyBlankRows)
        {
            return PreviewCore(workbookPath, logicalSourcePath, rule, skipFullyBlankRows);
        }

        private MappingPreviewResult PreviewCore(
            string workbookPath,
            string logicalSourcePath,
            MappingRuleDefinition rule,
            bool skipFullyBlankRows)
        {
            var result = new MappingPreviewResult { SourceFormat = "Excel" };
            try
            {
                MappingRuleSerializer.ValidateDefinition(rule);
                ValidatePath(workbookPath, rule.NormalizedExtension);

                using (WorkbookLease lease = OpenReadonlyCopy(workbookPath))
                {
                    result.SampleSha256 = lease.FileSha256;
                    MappingWorkbookSnapshot snapshot = CreateSnapshot(
                        lease.Workbook,
                        Path.GetExtension(workbookPath),
                        result.SampleSha256);
                    MappingSheetSnapshot sheetSnapshot = snapshot.Sheets.FirstOrDefault(
                        item => string.Equals(item.Name, rule.SheetName, StringComparison.Ordinal));
                    if (sheetSnapshot == null)
                    {
                        AddError(result, "SHEET_NOT_FOUND");
                        return Complete(result);
                    }

                    ISheet sheet = lease.Workbook.GetSheet(rule.SheetName);
                    var displayCells = sheetSnapshot.Cells.ToDictionary(cell => cell.Coordinate, StringComparer.Ordinal);
                    if (rule.RecordMode == MappingRecordMode.MasterDetail)
                    {
                        ResolveFileNameFields(logicalSourcePath, rule.MasterDetail, result);
                        MappingRuleDefinition detailRule = CreateDetailPreviewRule(rule);
                        ResolveRepeatingRows(
                            sheet,
                            sheetSnapshot,
                            displayCells,
                            detailRule,
                            result,
                            skipFullyBlankRows);
                    }
                    else if (rule.RecordMode == MappingRecordMode.RepeatingRows)
                        ResolveRepeatingRows(
                            sheet,
                            sheetSnapshot,
                            displayCells,
                            rule,
                            result,
                            skipFullyBlankRows);
                    else
                    {
                        foreach (FieldMappingRule field in rule.Fields)
                            ResolveField(sheet, displayCells, field, result);
                    }

                    result.TemplateSignature = rule.RecordMode == MappingRecordMode.MasterDetail
                        ? BuildMasterDetailTemplateSignature(rule, sheetSnapshot)
                        : BuildTemplateSignature(rule, sheetSnapshot);
                    if (!string.IsNullOrWhiteSpace(rule.TemplateSignature) &&
                        !string.Equals(rule.TemplateSignature, result.TemplateSignature, StringComparison.Ordinal))
                        AddError(result, "TEMPLATE_SIGNATURE_MISMATCH");
                }
            }
            catch (MappingValidationException ex)
            {
                AddError(result, ClassifyValidationError(ex.Message));
            }
            catch (IOException)
            {
                AddError(result, "SAMPLE_READ_FAILED");
            }
            catch (UnauthorizedAccessException)
            {
                AddError(result, "SAMPLE_ACCESS_DENIED");
            }
            catch (Exception)
            {
                AddError(result, "INVALID_WORKBOOK");
            }
            return Complete(result);
        }

        private static MappingRuleDefinition CreateDetailPreviewRule(MappingRuleDefinition rule)
        {
            MasterDetailMappingDefinition definition = rule.MasterDetail;
            return new MappingRuleDefinition
            {
                RuleName = rule.RuleName,
                ModelId = definition.Detail.ModelId,
                TargetModelType = definition.Detail.TargetModelType,
                ModelSchemaHash = definition.Detail.ModelSchemaHash,
                NormalizedExtension = rule.NormalizedExtension,
                SheetName = rule.SheetName,
                RecordMode = MappingRecordMode.RepeatingRows,
                RepeatedRows = definition.Detail.RepeatedRows,
                Fields = definition.Detail.Fields
            };
        }

        private static void ResolveFileNameFields(
            string filePath,
            MasterDetailMappingDefinition definition,
            MappingPreviewResult result)
        {
            FileNameExtractionResult extracted = FileNameExtractionParser.Parse(
                filePath,
                definition.FileName);
            foreach (FieldMappingRule field in definition.Master.Fields)
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
                        throw new MappingValidationException("主表包含不支持的文件名定位器。");
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
                    object value = ApplyTransforms(rawValue, field);
                    if (IsMissing(value) && field.IsRequired)
                    {
                        fieldResult.ErrorCode = "MISSING_REQUIRED";
                        AddError(result, fieldResult.ErrorCode);
                        continue;
                    }
                    fieldResult.Value = ConvertToTargetType(value, field.TargetType);
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
        }

        private static string BuildMasterDetailTemplateSignature(
            MappingRuleDefinition rule,
            MappingSheetSnapshot sheet)
        {
            MappingRuleDefinition detailRule = CreateDetailPreviewRule(rule);
            string detailSignature = BuildTemplateSignature(detailRule, sheet);
            IEnumerable<string> masterParts = rule.MasterDetail.Master.Fields
                .OrderBy(field => field.TargetField, StringComparer.Ordinal)
                .Select(field => string.Join("|", new[]
                {
                    field.TargetField,
                    field.Locator.Type ?? string.Empty,
                    field.Locator.SegmentIndex.ToString(CultureInfo.InvariantCulture)
                }));
            return MappingRuleSerializer.Sha256(string.Join("\n", new[]
            {
                "masterDetail",
                rule.MasterDetail.FileName.ExpectedSegmentCount.ToString(CultureInfo.InvariantCulture),
                string.Join("\n", masterParts),
                detailSignature
            }));
        }

        public MappingWorkbookSnapshot Inspect(string filePath)
        {
            ValidatePath(filePath, Path.GetExtension(filePath));
            using (WorkbookLease lease = OpenReadonlyCopy(filePath))
                return CreateSnapshot(lease.Workbook, Path.GetExtension(filePath), lease.FileSha256);
        }

        private void ValidatePath(string filePath, string expectedExtension)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new MappingValidationException("SAMPLE_NOT_FOUND");

            string fullPath = Path.GetFullPath(filePath);
            string extension = MappingRuleSerializer.NormalizeExtension(Path.GetExtension(fullPath));
            if (!string.Equals(extension, MappingRuleSerializer.NormalizeExtension(expectedExtension), StringComparison.Ordinal))
                throw new MappingValidationException("EXTENSION_MISMATCH");
            if (_forbiddenRoots.Any(root => IsUnderRoot(fullPath, root)))
                throw new MappingValidationException("ACQUISITION_DIRECTORY_FORBIDDEN");

            var info = new FileInfo(fullPath);
            if (info.Length == 0 || info.Length > MaximumFileBytes)
                throw new MappingValidationException("FILE_SIZE_LIMIT");
            if (!HasExpectedSignature(fullPath, extension))
                throw new MappingValidationException("FILE_SIGNATURE_MISMATCH");
        }

        private static bool IsUnderRoot(string filePath, string root)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return filePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasExpectedSignature(string filePath, string extension)
        {
            byte[] header = new byte[8];
            using (FileStream stream = File.Open(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                SharedEditorAccess))
            {
                if (stream.Read(header, 0, header.Length) < header.Length) return false;
            }

            if (extension == ".xls")
            {
                byte[] ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
                return header.SequenceEqual(ole);
            }

            return header[0] == 0x50 && header[1] == 0x4B &&
                   ((header[2] == 0x03 && header[3] == 0x04) ||
                    (header[2] == 0x05 && header[3] == 0x06) ||
                    (header[2] == 0x07 && header[3] == 0x08));
        }

        private static WorkbookLease OpenReadonlyCopy(string sourcePath)
        {
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                "MappingPreview_" + Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath).ToLowerInvariant());
            try
            {
                using (FileStream source = File.Open(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    SharedEditorAccess))
                using (FileStream destination = File.Open(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);

                var copiedFile = new FileInfo(tempPath);
                if (copiedFile.Length == 0 || copiedFile.Length > MaximumFileBytes)
                    throw new MappingValidationException("FILE_SIZE_LIMIT");
                string extension = MappingRuleSerializer.NormalizeExtension(Path.GetExtension(sourcePath));
                if (!HasExpectedSignature(tempPath, extension))
                    throw new MappingValidationException("FILE_SIGNATURE_MISMATCH");
                string hash = MappingRuleSerializer.FileSha256(tempPath);
                var stream = File.Open(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    IWorkbook workbook = WorkbookFactory.Create(stream);
                    return new WorkbookLease(tempPath, stream, workbook, hash);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch
            {
                File.Delete(tempPath);
                throw;
            }
        }

        private static MappingWorkbookSnapshot CreateSnapshot(IWorkbook workbook, string extension, string hash)
        {
            if (workbook.NumberOfSheets > MaximumSheets)
                throw new MappingValidationException("SHEET_COUNT_LIMIT");

            var snapshot = new MappingWorkbookSnapshot
            {
                FileExtension = MappingRuleSerializer.NormalizeExtension(extension),
                FileSha256 = hash
            };
            var formatter = new DataFormatter(CultureInfo.InvariantCulture);
            int nonEmptyCount = 0;

            for (int sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
            {
                ISheet sheet = workbook.GetSheetAt(sheetIndex);
                if (sheet.LastRowNum + 1 > MaximumRows)
                    throw new MappingValidationException("ROW_COUNT_LIMIT");
                var sheetSnapshot = new MappingSheetSnapshot { Name = sheet.SheetName };

                for (int regionIndex = 0; regionIndex < sheet.NumMergedRegions; regionIndex++)
                    sheetSnapshot.MergedRegions.Add(sheet.GetMergedRegion(regionIndex).FormatAsString());

                for (int rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    IRow row = sheet.GetRow(rowIndex);
                    if (row == null) continue;
                    if (row.LastCellNum > MaximumColumns)
                        throw new MappingValidationException("COLUMN_COUNT_LIMIT");

                    for (int columnIndex = 0; columnIndex < row.LastCellNum; columnIndex++)
                    {
                        ICell cell = row.GetCell(columnIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL);
                        if (cell == null) continue;
                        string text = GetDisplayText(cell, formatter);
                        if (text.Length > MaximumCellTextLength)
                            throw new MappingValidationException("CELL_TEXT_LIMIT");
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (++nonEmptyCount > MaximumNonEmptyCells)
                            throw new MappingValidationException("NON_EMPTY_CELL_LIMIT");

                        bool isFormula = cell.CellType == CellType.Formula;
                        bool cacheMissing = IsFormulaCacheMissing(cell);
                        sheetSnapshot.Cells.Add(new MappingCellSnapshot
                        {
                            Coordinate = ToCoordinate(rowIndex, columnIndex),
                            DisplayText = text,
                            ValueType = GetEffectiveCellType(cell) == CellType.Numeric && DateUtil.IsCellDateFormatted(cell)
                                ? "DateTime"
                                : GetEffectiveCellType(cell).ToString(),
                            IsFormula = isFormula,
                            FormulaCacheMissing = cacheMissing
                        });
                    }
                }
                snapshot.Sheets.Add(sheetSnapshot);
            }
            return snapshot;
        }

        private static void ResolveField(
            ISheet sheet,
            IDictionary<string, MappingCellSnapshot> displayCells,
            FieldMappingRule field,
            MappingPreviewResult result)
        {
            var fieldResult = new MappingPreviewFieldResult { TargetField = field.TargetField };
            result.Fields[field.TargetField] = fieldResult;

            string locatorError;
            ICell cell = ResolveCell(sheet, displayCells, field.Locator, out locatorError);
            if (locatorError != null)
            {
                if (IsMissingLocatorError(locatorError) && !field.IsRequired)
                {
                    CompleteOptionalMissingField(field, fieldResult, result);
                    return;
                }

                fieldResult.ErrorCode = IsMissingLocatorError(locatorError)
                    ? "MISSING_REQUIRED"
                    : locatorError;
                AddError(result, fieldResult.ErrorCode);
                return;
            }

            fieldResult.SourceCell = ToCoordinate(cell.RowIndex, cell.ColumnIndex);
            if (IsFormulaCacheMissing(cell))
            {
                fieldResult.ErrorCode = "FORMULA_CACHE_MISSING";
                AddError(result, fieldResult.ErrorCode);
                return;
            }

            object rawValue = GetRawValue(cell);
            fieldResult.RawValue = rawValue;
            try
            {
                object value = ApplyTransforms(rawValue, field);
                if (IsMissing(value) && field.IsRequired)
                {
                    fieldResult.ErrorCode = "MISSING_REQUIRED";
                    AddError(result, fieldResult.ErrorCode);
                    return;
                }
                fieldResult.Value = ConvertToTargetType(value, field.TargetType);
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

        private static void ResolveRepeatingRows(
            ISheet sheet,
            MappingSheetSnapshot sheetSnapshot,
            IDictionary<string, MappingCellSnapshot> displayCells,
            MappingRuleDefinition rule,
            MappingPreviewResult result,
            bool skipFullyBlankRows)
        {
            int anchorRow;
            int anchorColumn;
            string anchorError;
            if (!TryResolveTableAnchor(sheetSnapshot, rule.RepeatedRows, out anchorRow, out anchorColumn, out anchorError))
            {
                AddError(result, anchorError);
                return;
            }

            List<FieldMappingRule> commonFields = rule.Fields
                .Where(field => field.Scope == MappingFieldScope.Common)
                .ToList();
            foreach (FieldMappingRule field in commonFields)
                ResolveField(sheet, displayCells, field, result);

            List<FieldMappingRule> rowFields = rule.Fields
                .Where(field => field.Scope == MappingFieldScope.RowColumn)
                .ToList();
            foreach (FieldMappingRule field in rowFields)
            {
                if (string.IsNullOrWhiteSpace(field.Locator.Text)) continue;
                int headerColumn = anchorColumn + field.Locator.ColumnOffset;
                if (headerColumn < 0 || headerColumn >= MaximumColumns)
                {
                    AddError(result, "TABLE_REGION_OUT_OF_RANGE");
                    continue;
                }
                MappingCellSnapshot actualHeader;
                displayCells.TryGetValue(ToCoordinate(anchorRow, headerColumn), out actualHeader);
                if (actualHeader == null || !string.Equals(
                        NormalizeText(actualHeader.DisplayText),
                        NormalizeText(field.Locator.Text),
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddError(result, "TABLE_HEADER_MISMATCH");
                }
            }

            int firstDataRow = anchorRow + rule.RepeatedRows.FirstDataRowOffset;
            int keyColumn = anchorColumn + rule.RepeatedRows.KeyColumnOffset;
            if (firstDataRow < 0 || firstDataRow >= MaximumRows ||
                keyColumn < 0 || keyColumn >= MaximumColumns ||
                anchorColumn + rule.RepeatedRows.FirstColumnOffset < 0 ||
                anchorColumn + rule.RepeatedRows.LastColumnOffset >= MaximumColumns)
            {
                AddError(result, "TABLE_REGION_OUT_OF_RANGE");
                return;
            }

            for (int rowIndex = firstDataRow; rowIndex < MaximumRows; rowIndex++)
            {
                IRow row = sheet.GetRow(rowIndex);
                ICell keyCell = row == null
                    ? null
                    : row.GetCell(keyColumn, MissingCellPolicy.RETURN_BLANK_AS_NULL);
                bool missingKey = keyCell == null ||
                    (!IsFormulaCacheMissing(keyCell) && IsMissing(GetRawValue(keyCell)));
                if (missingKey)
                {
                    bool fullyBlankRow = row == null || row.Cells.All(cell => IsMissing(GetRawValue(cell)));
                    if (skipFullyBlankRows && fullyBlankRow && rowIndex < sheet.LastRowNum)
                        continue;
                    if (skipFullyBlankRows && !fullyBlankRow)
                    {
                        AddError(result, "MISSING_ROW_KEY");
                        return;
                    }
                    break;
                }

                var record = new MappingPreviewRecordResult
                {
                    ExcelRowNumber = rowIndex + 1
                };
                foreach (FieldMappingRule common in commonFields)
                {
                    MappingPreviewFieldResult commonResult;
                    if (result.Fields.TryGetValue(common.TargetField, out commonResult))
                        record.Fields[common.TargetField] = CloneFieldResult(commonResult);
                }
                foreach (FieldMappingRule field in rowFields)
                    ResolveRowField(sheet, rowIndex, anchorColumn, field, record, result);
                record.IsValid = record.Fields.Values.All(field => string.IsNullOrWhiteSpace(field.ErrorCode));
                result.Records.Add(record);
            }

            if (result.Records.Count == 0)
                AddError(result, "NO_RECORDS");
        }

        private static bool TryResolveTableAnchor(
            MappingSheetSnapshot sheet,
            RepeatedRowDefinition definition,
            out int rowIndex,
            out int columnIndex,
            out string error)
        {
            rowIndex = -1;
            columnIndex = -1;
            error = null;
            if (definition.AnchorMode == MappingTableAnchorMode.FixedCell)
            {
                if (!TryParseCoordinate(definition.AnchorCell, out rowIndex, out columnIndex) ||
                    rowIndex >= MaximumRows || columnIndex >= MaximumColumns)
                {
                    error = "TABLE_ANCHOR_OUT_OF_RANGE";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(definition.AnchorText))
                {
                    string fixedCoordinate = ToCoordinate(rowIndex, columnIndex);
                    MappingCellSnapshot actual = sheet.Cells.FirstOrDefault(cell =>
                        string.Equals(cell.Coordinate, fixedCoordinate, StringComparison.Ordinal));
                    if (actual == null || !string.Equals(
                            NormalizeText(actual.DisplayText),
                            NormalizeText(definition.AnchorText),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        error = "TABLE_ANCHOR_MISMATCH";
                        return false;
                    }
                }
                return true;
            }

            List<MappingCellSnapshot> matches = sheet.Cells.Where(cell => string.Equals(
                NormalizeText(cell.DisplayText),
                NormalizeText(definition.AnchorText),
                StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                error = "TABLE_ANCHOR_NOT_FOUND";
                return false;
            }
            if (matches.Count > 1)
            {
                error = "AMBIGUOUS_TABLE_ANCHOR";
                return false;
            }
            if (!TryParseCoordinate(matches[0].Coordinate, out rowIndex, out columnIndex))
            {
                error = "INVALID_TABLE_ANCHOR";
                return false;
            }
            return true;
        }

        private static void ResolveRowField(
            ISheet sheet,
            int rowIndex,
            int anchorColumn,
            FieldMappingRule field,
            MappingPreviewRecordResult record,
            MappingPreviewResult result)
        {
            int columnIndex = anchorColumn + field.Locator.ColumnOffset;
            var fieldResult = new MappingPreviewFieldResult
            {
                TargetField = field.TargetField,
                SourceCell = columnIndex >= 0 && columnIndex < MaximumColumns
                    ? ToCoordinate(rowIndex, columnIndex)
                    : string.Empty
            };
            record.Fields[field.TargetField] = fieldResult;
            if (columnIndex < 0 || columnIndex >= MaximumColumns)
            {
                fieldResult.ErrorCode = "LOCATOR_OUT_OF_RANGE";
                AddError(result, fieldResult.ErrorCode);
                return;
            }

            IRow row = sheet.GetRow(rowIndex);
            ICell cell = row == null ? null : row.GetCell(columnIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL);
            if (cell == null)
            {
                CompleteMissingRowField(field, fieldResult, result);
                return;
            }
            if (IsFormulaCacheMissing(cell))
            {
                fieldResult.ErrorCode = "FORMULA_CACHE_MISSING";
                AddError(result, fieldResult.ErrorCode);
                return;
            }

            fieldResult.RawValue = GetRawValue(cell);
            try
            {
                object value = ApplyTransforms(fieldResult.RawValue, field);
                if (IsMissing(value) && field.IsRequired)
                {
                    fieldResult.ErrorCode = "MISSING_REQUIRED";
                    AddError(result, fieldResult.ErrorCode);
                    return;
                }
                fieldResult.Value = ConvertToTargetType(value, field.TargetType);
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

        private static void CompleteMissingRowField(
            FieldMappingRule field,
            MappingPreviewFieldResult fieldResult,
            MappingPreviewResult result)
        {
            if (field.IsRequired)
            {
                fieldResult.ErrorCode = "MISSING_REQUIRED";
                AddError(result, fieldResult.ErrorCode);
                return;
            }
            CompleteOptionalMissingField(field, fieldResult, result);
        }

        private static MappingPreviewFieldResult CloneFieldResult(MappingPreviewFieldResult source)
        {
            return new MappingPreviewFieldResult
            {
                TargetField = source.TargetField,
                SourceCell = source.SourceCell,
                RawValue = source.RawValue,
                Value = source.Value,
                ErrorCode = source.ErrorCode,
                WarningCode = source.WarningCode
            };
        }

        private static ICell ResolveCell(
            ISheet sheet,
            IDictionary<string, MappingCellSnapshot> displayCells,
            MappingLocator locator,
            out string error)
        {
            error = null;
            if (locator == null || !MappingRuleSerializer.AllowedLocatorTypes.Contains(locator.Type ?? string.Empty))
            {
                error = "INVALID_LOCATOR";
                return null;
            }

            int rowIndex;
            int columnIndex;
            if (locator.Type == "cell")
            {
                int anchorRowIndex;
                int anchorColumnIndex;
                if (!TryParseCoordinate(locator.AnchorCell, out anchorRowIndex, out anchorColumnIndex) ||
                    anchorRowIndex >= MaximumRows || anchorColumnIndex >= MaximumColumns)
                {
                    error = "CELL_ANCHOR_OUT_OF_RANGE";
                    return null;
                }
                MappingCellSnapshot anchor;
                if (!displayCells.TryGetValue(
                        ToCoordinate(anchorRowIndex, anchorColumnIndex),
                        out anchor))
                {
                    error = "CELL_ANCHOR_NOT_FOUND";
                    return null;
                }
                if (!string.Equals(
                        NormalizeText(anchor.DisplayText),
                        NormalizeText(locator.AnchorText),
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "CELL_ANCHOR_MISMATCH";
                    return null;
                }

                if (!TryParseCoordinate(locator.Cell, out rowIndex, out columnIndex) ||
                    rowIndex >= MaximumRows || columnIndex >= MaximumColumns)
                {
                    error = "LOCATOR_OUT_OF_RANGE";
                    return null;
                }
                IRow fixedRow = sheet.GetRow(rowIndex);
                ICell fixedCell = fixedRow == null ? null : fixedRow.GetCell(columnIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL);
                if (fixedCell == null)
                {
                    error = "VALUE_NOT_FOUND";
                    return null;
                }
                return fixedCell;
            }

            List<MappingCellSnapshot> anchors = displayCells.Values
                .Where(cell => string.Equals(NormalizeText(cell.DisplayText), NormalizeText(locator.Text), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (anchors.Count > 1)
            {
                error = "AMBIGUOUS_LABEL";
                return null;
            }
            if (anchors.Count == 0)
            {
                error = "ANCHOR_NOT_FOUND";
                return null;
            }
            if (!TryParseCoordinate(anchors[0].Coordinate, out rowIndex, out columnIndex))
            {
                error = "INVALID_LOCATOR";
                return null;
            }

            if (locator.Type == "labelOffset")
            {
                rowIndex += locator.RowOffset;
                columnIndex += locator.ColumnOffset;
            }
            else if (locator.Type == "rowKey")
            {
                int valueColumn;
                if (!TryParseColumn(locator.ValueColumn, out valueColumn))
                {
                    error = "INVALID_LOCATOR";
                    return null;
                }
                rowIndex += locator.RowOffset;
                columnIndex = valueColumn;
            }
            else
            {
                rowIndex += locator.DataRowOffset;
                columnIndex += locator.ColumnOffset;
            }

            if (rowIndex < 0 || rowIndex >= MaximumRows || columnIndex < 0 || columnIndex >= MaximumColumns)
            {
                error = "LOCATOR_OUT_OF_RANGE";
                return null;
            }
            IRow targetRow = sheet.GetRow(rowIndex);
            ICell targetCell = targetRow == null ? null : targetRow.GetCell(columnIndex, MissingCellPolicy.RETURN_BLANK_AS_NULL);
            if (targetCell == null)
                error = "VALUE_NOT_FOUND";
            return targetCell;
        }

        private static bool IsMissingLocatorError(string error)
        {
            return string.Equals(error, "ANCHOR_NOT_FOUND", StringComparison.Ordinal) ||
                   string.Equals(error, "VALUE_NOT_FOUND", StringComparison.Ordinal);
        }

        private static void CompleteOptionalMissingField(
            FieldMappingRule field,
            MappingPreviewFieldResult fieldResult,
            MappingPreviewResult result)
        {
            try
            {
                fieldResult.Value = ConvertToTargetType(ApplyTransforms(null, field), field.TargetType);
                fieldResult.WarningCode = "OPTIONAL_VALUE_MISSING";
                AddWarning(result, fieldResult.WarningCode);
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

        internal static object ApplyTransforms(object rawValue, FieldMappingRule field)
        {
            object value = rawValue;
            if (IsMissing(value) &&
                (field.Transforms ?? new List<string>()).Contains("default") &&
                !string.IsNullOrEmpty(field.DefaultValue))
            {
                value = field.DefaultValue;
            }
            if (IsMissing(value)) return null;

            foreach (string transform in field.Transforms ?? Enumerable.Empty<string>())
            {
                switch (transform)
                {
                    case "trim":
                        value = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
                        break;
                    case "normalizeWhitespace":
                        value = value == null ? null : NormalizeText(Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                    case "integer":
                        value = ToInteger(value);
                        break;
                    case "decimal":
                        value = ToDecimal(value);
                        break;
                    case "date":
                        value = ToDate(value);
                        break;
                    case "valueMap":
                        string key = Convert.ToString(value, CultureInfo.InvariantCulture);
                        string mapped;
                        if (field.ExactValueMap != null && field.ExactValueMap.TryGetValue(key, out mapped))
                            value = mapped;
                        break;
                    case "default":
                        break;
                    default:
                        throw new MappingValidationException("不允许的转换器：" + transform);
                }
            }
            if (IsMissing(value) && !string.IsNullOrEmpty(field.DefaultValue))
                value = field.DefaultValue;
            return value;
        }

        internal static object ConvertToTargetType(object value, string targetType)
        {
            if (IsMissing(value)) return null;
            switch ((targetType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "string":
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                case "int":
                    return ToInteger(value);
                case "long":
                    return ToLong(value);
                case "decimal":
                    return ToDecimal(value);
                case "float":
                    return value is float ? value : Convert.ToSingle(ToDecimal(value), CultureInfo.InvariantCulture);
                case "double":
                    return value is double ? value : Convert.ToDouble(ToDecimal(value), CultureInfo.InvariantCulture);
                case "datetime":
                    return ToDate(value);
                case "bool":
                    return value is bool ? value : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                default:
                    throw new FormatException("Unsupported target type.");
            }
        }

        private static int ToInteger(object value)
        {
            if (value is int) return (int)value;
            if (value is long) return checked((int)(long)value);
            if (value is double)
            {
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((int)number);
            }
            if (value is float)
            {
                float number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number) || number != Math.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((int)number);
            }
            if (value is decimal)
            {
                decimal number = (decimal)value;
                if (number != decimal.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((int)number);
            }
            return int.Parse(NormalizeNumericText(value), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static long ToLong(object value)
        {
            if (value is long) return (long)value;
            if (value is int) return (int)value;
            if (value is double)
            {
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((long)number);
            }
            if (value is float)
            {
                float number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number) || number != Math.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((long)number);
            }
            if (value is decimal)
            {
                decimal number = (decimal)value;
                if (number != decimal.Truncate(number))
                    throw new FormatException("整数值不能包含小数部分。");
                return checked((long)number);
            }
            return long.Parse(NormalizeNumericText(value), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static decimal ToDecimal(object value)
        {
            if (value is decimal) return (decimal)value;
            if (value is double) return Convert.ToDecimal((double)value, CultureInfo.InvariantCulture);
            return decimal.Parse(
                NormalizeNumericText(value),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
        }

        private static string NormalizeNumericText(object value)
        {
            return (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
                .Normalize(NormalizationForm.FormKC)
                .Trim()
                .Replace('\u2212', '-')
                .Replace('\u2013', '-')
                .Replace('\u2014', '-');
        }

        private static DateTime ToDate(object value)
        {
            if (value is DateTime) return (DateTime)value;
            if (value is double) return DateTime.FromOADate((double)value);
            if (value is decimal) return DateTime.FromOADate(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            DateTime parsed;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed) ||
                DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
                return parsed;
            throw new FormatException("日期格式无效。");
        }

        private static object GetRawValue(ICell cell)
        {
            CellType type = GetEffectiveCellType(cell);
            switch (type)
            {
                case CellType.String:
                    return cell.StringCellValue;
                case CellType.Numeric:
                    if (DateUtil.IsCellDateFormatted(cell)) return cell.DateCellValue;
                    return cell.NumericCellValue;
                case CellType.Boolean:
                    return cell.BooleanCellValue;
                case CellType.Blank:
                    return null;
                default:
                    return GetDisplayText(cell, new DataFormatter(CultureInfo.InvariantCulture));
            }
        }

        private static CellType GetEffectiveCellType(ICell cell)
        {
            return cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
        }

        private static bool IsFormulaCacheMissing(ICell cell)
        {
            if (cell == null || cell.CellType != CellType.Formula) return false;
            var xssfCell = cell as XSSFCell;
            if (xssfCell != null)
            {
                MethodInfo getCell = typeof(XSSFCell).GetMethod(
                    "GetCTCell",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object ctCell = getCell == null ? null : getCell.Invoke(xssfCell, null);
                MethodInfo isSetValue = ctCell == null ? null : ctCell.GetType().GetMethod("IsSetV");
                if (isSetValue == null || !(bool)isSetValue.Invoke(ctCell, null)) return true;
                PropertyInfo valueProperty = ctCell.GetType().GetProperty("v");
                string cachedValue = valueProperty == null
                    ? null
                    : Convert.ToString(valueProperty.GetValue(ctCell, null), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(cachedValue)) return true;
            }
            return cell.CachedFormulaResultType == CellType.Blank ||
                   cell.CachedFormulaResultType == CellType.Error;
        }

        private static string GetDisplayText(ICell cell, DataFormatter formatter)
        {
            if (cell.CellType != CellType.Formula)
                return formatter.FormatCellValue(cell) ?? string.Empty;
            switch (cell.CachedFormulaResultType)
            {
                case CellType.String:
                    return cell.StringCellValue ?? string.Empty;
                case CellType.Numeric:
                    return DateUtil.IsCellDateFormatted(cell)
                        ? (cell.DateCellValue.HasValue
                            ? cell.DateCellValue.Value.ToString("o", CultureInfo.InvariantCulture)
                            : string.Empty)
                        : cell.NumericCellValue.ToString("G17", CultureInfo.InvariantCulture);
                case CellType.Boolean:
                    return cell.BooleanCellValue ? "TRUE" : "FALSE";
                default:
                    return string.Empty;
            }
        }

        private static string BuildTemplateSignature(MappingRuleDefinition rule, MappingSheetSnapshot sheet)
        {
            var parts = new List<string> { sheet.Name };
            if (rule.RecordMode == MappingRecordMode.RepeatingRows)
            {
                RepeatedRowDefinition rows = rule.RepeatedRows;
                parts.Add(string.Join("|", new[]
                {
                    "repeatingRows",
                    rows.AnchorMode.ToString(),
                    NormalizeText(rows.AnchorText),
                    rows.AnchorMode == MappingTableAnchorMode.FixedCell
                        ? (rows.AnchorCell ?? string.Empty).ToUpperInvariant()
                        : string.Empty,
                    rows.FirstDataRowOffset.ToString(CultureInfo.InvariantCulture),
                    rows.KeyColumnOffset.ToString(CultureInfo.InvariantCulture),
                    rows.FirstColumnOffset.ToString(CultureInfo.InvariantCulture),
                    rows.LastColumnOffset.ToString(CultureInfo.InvariantCulture)
                }));
            }
            else
            {
                parts.AddRange(sheet.MergedRegions.OrderBy(region => region, StringComparer.Ordinal));
            }
            foreach (FieldMappingRule field in rule.Fields.OrderBy(item => item.TargetField, StringComparer.Ordinal))
            {
                MappingLocator locator = field.Locator;
                string anchors = string.Empty;
                if (locator.Type == "rowColumn")
                {
                    int anchorRow;
                    int anchorColumn;
                    string anchorError;
                    MappingCellSnapshot actualHeader = null;
                    if (TryResolveTableAnchor(
                            sheet,
                            rule.RepeatedRows,
                            out anchorRow,
                            out anchorColumn,
                            out anchorError))
                    {
                        int headerColumn = anchorColumn + locator.ColumnOffset;
                        if (headerColumn >= 0 && headerColumn < MaximumColumns)
                        {
                            string coordinate = ToCoordinate(anchorRow, headerColumn);
                            actualHeader = sheet.Cells.FirstOrDefault(cell =>
                                string.Equals(cell.Coordinate, coordinate, StringComparison.Ordinal));
                        }
                    }
                    anchors = string.IsNullOrWhiteSpace(locator.Text)
                        ? "unnamed"
                        : NormalizeText(actualHeader == null ? string.Empty : actualHeader.DisplayText);
                }
                else if (locator.Type == "cell")
                {
                    int anchorRow;
                    int anchorColumn;
                    string anchorCoordinate = TryParseCoordinate(locator.AnchorCell, out anchorRow, out anchorColumn)
                        ? ToCoordinate(anchorRow, anchorColumn)
                        : (locator.AnchorCell ?? string.Empty).ToUpperInvariant();
                    MappingCellSnapshot actualAnchor = sheet.Cells.FirstOrDefault(cell =>
                        string.Equals(cell.Coordinate, anchorCoordinate, StringComparison.Ordinal));
                    anchors = anchorCoordinate + "=" + NormalizeText(
                        actualAnchor == null ? string.Empty : actualAnchor.DisplayText);
                }
                else if (field.IsRequired)
                {
                    anchors = string.Join(",", sheet.Cells
                        .Where(cell => string.Equals(
                            NormalizeText(cell.DisplayText),
                            NormalizeText(locator.Text),
                            StringComparison.OrdinalIgnoreCase))
                        .Select(cell => cell.Coordinate)
                        .OrderBy(value => value, StringComparer.Ordinal));
                }
                else
                {
                    // Optional label/key/header locators are allowed to be absent and
                    // fall back to their default value. Their presence must therefore
                    // not make the published template signature unstable.
                    anchors = "optional";
                }
                parts.Add(string.Join("|", new[]
                {
                    field.TargetField,
                    locator.Type ?? string.Empty,
                    locator.Cell ?? string.Empty,
                    locator.AnchorCell ?? string.Empty,
                    NormalizeText(locator.AnchorText),
                    NormalizeText(locator.Text),
                    locator.RowOffset.ToString(CultureInfo.InvariantCulture),
                    locator.ColumnOffset.ToString(CultureInfo.InvariantCulture),
                    locator.ValueColumn ?? string.Empty,
                    locator.DataRowOffset.ToString(CultureInfo.InvariantCulture),
                    anchors
                }));
            }
            return MappingRuleSerializer.Sha256(string.Join("\n", parts));
        }

        private static bool TryParseCoordinate(string coordinate, out int rowIndex, out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;
            if (string.IsNullOrWhiteSpace(coordinate)) return false;
            string value = coordinate.Trim().ToUpperInvariant();
            int split = 0;
            while (split < value.Length && value[split] >= 'A' && value[split] <= 'Z') split++;
            if (split == 0 || split == value.Length) return false;
            if (!TryParseColumn(value.Substring(0, split), out columnIndex)) return false;
            int oneBasedRow;
            if (!int.TryParse(value.Substring(split), NumberStyles.None, CultureInfo.InvariantCulture, out oneBasedRow) || oneBasedRow <= 0)
                return false;
            rowIndex = oneBasedRow - 1;
            return true;
        }

        private static bool TryParseColumn(string letters, out int columnIndex)
        {
            columnIndex = -1;
            if (string.IsNullOrWhiteSpace(letters)) return false;
            int value = 0;
            foreach (char character in letters.Trim().ToUpperInvariant())
            {
                if (character < 'A' || character > 'Z') return false;
                value = checked(value * 26 + character - 'A' + 1);
                if (value > 16384) return false;
            }
            columnIndex = value - 1;
            return columnIndex >= 0;
        }

        private static string ToCoordinate(int rowIndex, int columnIndex)
        {
            return new CellReference(rowIndex, columnIndex).FormatAsString();
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var builder = new StringBuilder();
            bool previousWhitespace = false;
            foreach (char character in text.Normalize(NormalizationForm.FormKC).Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWhitespace) builder.Append(' ');
                    previousWhitespace = true;
                }
                else
                {
                    builder.Append(character);
                    previousWhitespace = false;
                }
            }
            return builder.ToString();
        }

        internal static bool IsMissing(object value)
        {
            return value == null || (value is string && string.IsNullOrWhiteSpace((string)value));
        }

        private static void AddError(MappingPreviewResult result, string code)
        {
            if (!result.ErrorCodes.Contains(code)) result.ErrorCodes.Add(code);
        }

        private static void AddWarning(MappingPreviewResult result, string code)
        {
            if (!result.WarningCodes.Contains(code)) result.WarningCodes.Add(code);
        }

        private static MappingPreviewResult Complete(MappingPreviewResult result)
        {
            result.IsValid = result.ErrorCodes.Count == 0;
            return result;
        }

        private static string ClassifyValidationError(string message)
        {
            string[] knownCodes =
            {
                "SAMPLE_NOT_FOUND", "EXTENSION_MISMATCH", "ACQUISITION_DIRECTORY_FORBIDDEN",
                "FILE_SIZE_LIMIT", "FILE_SIGNATURE_MISMATCH", "SHEET_COUNT_LIMIT", "ROW_COUNT_LIMIT",
                "COLUMN_COUNT_LIMIT", "NON_EMPTY_CELL_LIMIT", "CELL_TEXT_LIMIT"
            };
            return knownCodes.Contains(message) ? message : "INVALID_RULE";
        }

        private sealed class WorkbookLease : IDisposable
        {
            private readonly string _tempPath;
            private readonly Stream _stream;

            public WorkbookLease(string tempPath, Stream stream, IWorkbook workbook, string fileSha256)
            {
                _tempPath = tempPath;
                _stream = stream;
                Workbook = workbook;
                FileSha256 = fileSha256;
            }

            public IWorkbook Workbook { get; private set; }
            public string FileSha256 { get; private set; }

            public void Dispose()
            {
                try
                {
                    if (Workbook != null)
                    {
                        Workbook.Close();
                        Workbook = null;
                    }
                }
                finally
                {
                    try
                    {
                        _stream.Dispose();
                    }
                    finally
                    {
                        File.Delete(_tempPath);
                    }
                }
            }
        }
    }
}

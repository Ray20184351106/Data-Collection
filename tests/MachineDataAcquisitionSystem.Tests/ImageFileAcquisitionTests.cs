using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SQLite;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class ImageFileAcquisitionTests
    {
        [Fact]
        public void Preview_reads_filename_fields_without_opening_an_excel_workbook()
        {
            string root = CreateTemporaryDirectory();
            string imagePath = Path.Combine(root, "AOI[WO-001]【LOT-02】.jpg");
            try
            {
                WriteJpeg(imagePath, 0x31);
                MappingRuleDefinition rule = CreateRule(root);

                MappingPreviewResult result = new ImageFileMappingService().Preview(imagePath, rule);

                Assert.True(result.IsValid, string.Join(",", result.ErrorCodes));
                Assert.Equal("WO-001", result.Fields["WorkOrderNo"].Value);
                Assert.Equal("LOT-02", result.Fields["LotNo"].Value);
                Assert.Equal("AOI[WO-001]【LOT-02】.jpg", result.Fields["OriginalFileName"].Value);
                Assert.False(string.IsNullOrWhiteSpace(result.TemplateSignature));
            }
            finally
            {
                DeleteFile(imagePath);
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Archive_is_stable_for_retry_and_never_overwrites_a_different_file()
        {
            string root = CreateTemporaryDirectory();
            string sourceRoot = Path.Combine(root, "source");
            string archiveRoot = Path.Combine(root, "archive");
            Directory.CreateDirectory(sourceRoot);
            string sourcePath = Path.Combine(sourceRoot, "CAM[WO-008].png");
            try
            {
                WritePng(sourcePath, 0x41);
                var service = new ImageArchiveService();
                var definition = new ImageArchiveDefinition
                {
                    SharedRootPath = archiveRoot,
                    PathTargetField = "ImagePath",
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = 1 }
                };

                ImageArchiveResult first = service.Archive(sourcePath, 3, definition);
                ImageArchiveResult retry = service.Archive(sourcePath, 3, definition);

                Assert.Equal(first.ArchivedPath, retry.ArchivedPath);
                Assert.Equal(first.ContentSha256, retry.ContentSha256);
                Assert.True(File.Exists(first.ArchivedPath));
                string normalizedRoot = Path.GetFullPath(archiveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.StartsWith(normalizedRoot, Path.GetFullPath(first.ArchivedPath), StringComparison.OrdinalIgnoreCase);

                byte[] conflictingContent = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x7F };
                File.WriteAllBytes(first.ArchivedPath, conflictingContent);

                Assert.Throws<IOException>(() => service.Archive(sourcePath, 3, definition));
                Assert.Equal(conflictingContent, File.ReadAllBytes(first.ArchivedPath));
            }
            finally
            {
                DeleteFile(sourcePath);
                foreach (string file in Directory.Exists(archiveRoot)
                    ? Directory.GetFiles(archiveRoot, "*", SearchOption.AllDirectories)
                    : Array.Empty<string>())
                    DeleteFile(file);
                foreach (string directory in Directory.Exists(archiveRoot)
                    ? Directory.GetDirectories(archiveRoot, "*", SearchOption.AllDirectories)
                    : Array.Empty<string>())
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory);
                }
                if (Directory.Exists(archiveRoot)) Directory.Delete(archiveRoot);
                if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot);
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Image_rule_rejects_excel_extensions_and_a_non_string_archive_field_contract()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                MappingRuleDefinition rule = CreateRule(root);
                rule.NormalizedExtension = ".xlsx";
                Assert.Throws<MappingValidationException>(() => MappingRuleSerializer.ValidateDefinition(rule));

                rule.NormalizedExtension = ".jpg";
                rule.ImageArchive.PathTargetType = "long";
                Assert.Throws<MappingValidationException>(() => MappingRuleSerializer.ValidateDefinition(rule));
            }
            finally
            {
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Image_rule_generator_emits_archive_path_contract()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string script = new MappingScriptGenerator().Generate(CreateRule(root));

                Assert.Contains("model.ImagePath", script);
                Assert.Contains("PopulateImageModel", script);
            }
            finally
            {
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Runtime_populates_filename_fields_and_archive_service_sets_the_path()
        {
            string root = CreateTemporaryDirectory();
            string imagePath = Path.Combine(root, "AOI[WO-011]【LOT-09】.jpg");
            try
            {
                WriteJpeg(imagePath, 0x55);
                MappingRuleDefinition rule = CreateRule(root);
                rule.TargetModelType = nameof(InspectionImageRecord);
                string encoded = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(MappingRuleSerializer.Serialize(rule)));
                var model = new InspectionImageRecord();

                MappingRuntime.PopulateImageModel(model, encoded, imagePath, 1);
                ImageArchiveService.ApplyArchivedPath(model, rule.ImageArchive, @"\\server\share\image.jpg");

                Assert.Equal("WO-011", model.WorkOrderNo);
                Assert.Equal("LOT-09", model.LotNo);
                Assert.Equal("AOI[WO-011]【LOT-09】.jpg", model.OriginalFileName);
                Assert.Equal(@"\\server\share\image.jpg", model.ImagePath);
            }
            finally
            {
                DeleteFile(imagePath);
                Directory.Delete(root);
            }
        }

        [Fact]
        public void Image_rule_can_be_saved_validated_and_published_in_a_real_sqlite_store()
        {
            string root = CreateTemporaryDirectory();
            string databasePath = Path.Combine(root, "rules.db");
            try
            {
                SQLiteConnection.CreateFile(databasePath);
                using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;"))
                using (var command = connection.CreateCommand())
                {
                    connection.Open();
                    command.CommandText = @"
CREATE TABLE DataModels (Id INTEGER PRIMARY KEY, ModelName TEXT NOT NULL, TableName TEXT NOT NULL);
CREATE TABLE ModelFields (
    Id INTEGER PRIMARY KEY, ModelId INTEGER NOT NULL, FieldName TEXT NOT NULL,
    FieldType TEXT NOT NULL, FieldLength INTEGER DEFAULT 0, IsRequired INTEGER DEFAULT 0,
    IsPrimaryKey INTEGER DEFAULT 0, IsIdentity INTEGER DEFAULT 0, Description TEXT);
INSERT INTO DataModels (Id, ModelName, TableName) VALUES (7, 'InspectionImageRecord', 'InspectionImageRecord');
INSERT INTO ModelFields (Id, ModelId, FieldName, FieldType, FieldLength, IsRequired, IsPrimaryKey, IsIdentity, Description) VALUES
    (1, 7, 'WorkOrderNo', 'string', 64, 1, 0, 0, 'work order'),
    (2, 7, 'LotNo', 'string', 64, 1, 0, 0, 'lot'),
    (3, 7, 'OriginalFileName', 'string', 260, 1, 0, 0, 'source name'),
    (4, 7, 'ImagePath', 'string', 1000, 1, 0, 0, 'archive path');";
                    command.ExecuteNonQuery();
                }

                MappingRuleDefinition rule = CreateRule(root);
                rule.ModelSchemaHash = new ModelSchemaService(
                    "Data Source=" + databasePath + ";Version=3;").ComputeHash(7);
                using (var store = new ParseRuleStore(databasePath))
                {
                    store.Initialize();
                    ParseRuleVersion draft = store.SaveDraft(rule);
                    ParseRuleVersion validated = store.Validate(draft.Id, draft.Revision, "image sample passed");
                    ParseRuleVersion published = store.Publish(validated.Id, "3", validated.Revision);

                    Assert.Equal(ParseRuleStatus.Published, published.Status);
                    Assert.Equal(published.Id, store.GetPublished("3", ".jpg").Id);
                }
            }
            finally
            {
                DeleteFile(databasePath);
                Directory.Delete(root);
            }
        }

        private static MappingRuleDefinition CreateRule(string archiveRoot)
        {
            return new MappingRuleDefinition
            {
                RuleName = "AOI image filename",
                ModelId = 7,
                TargetModelType = "InspectionImageRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".jpg",
                RecordMode = MappingRecordMode.ImageFileName,
                ImageArchive = new ImageArchiveDefinition
                {
                    SharedRootPath = archiveRoot,
                    PathTargetField = "ImagePath",
                    PathTargetType = "string",
                    FileName = new FileNameExtractionDefinition { ExpectedSegmentCount = 2 }
                },
                Fields = new List<FieldMappingRule>
                {
                    FilenameField("WorkOrderNo", "fileNameSegment", 0),
                    FilenameField("LotNo", "fileNameSegment", 1),
                    FilenameField("OriginalFileName", "fileNameFull", 0)
                }
            };
        }

        private static FieldMappingRule FilenameField(string target, string locator, int segmentIndex)
        {
            return new FieldMappingRule
            {
                TargetField = target,
                TargetType = "string",
                IsRequired = true,
                Scope = MappingFieldScope.Common,
                Locator = new MappingLocator { Type = locator, SegmentIndex = segmentIndex },
                Transforms = new List<string> { "trim" },
                ConfirmationState = MappingConfirmationState.HumanConfirmed
            };
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "image-acquisition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteJpeg(string path, byte payload)
        {
            File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF, payload, 0xFF, 0xD9 });
        }

        private static void WritePng(string path, byte payload)
        {
            File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, payload });
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private sealed class InspectionImageRecord
        {
            public string WorkOrderNo { get; set; }
            public string LotNo { get; set; }
            public string OriginalFileName { get; set; }
            public string ImagePath { get; set; }
        }
    }
}

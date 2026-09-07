using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public sealed class ImageArchiveResult
    {
        public string ArchivedPath { get; set; }
        public string ContentSha256 { get; set; }
        public bool ReusedExistingFile { get; set; }
    }

    public sealed class ImageArchiveService
    {
        public ImageArchiveResult Archive(
            string sourcePath,
            int machineId,
            ImageArchiveDefinition definition)
        {
            if (machineId <= 0) throw new ArgumentOutOfRangeException(nameof(machineId));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            string extension = MappingRuleSerializer.NormalizeExtension(Path.GetExtension(sourcePath));
            ImageFileValidator.Validate(sourcePath, extension);

            string root = NormalizeRoot(definition.SharedRootPath);
            string hash = MappingRuleSerializer.FileSha256(sourcePath);
            string machineFolder = "Machine-" + machineId.ToString(CultureInfo.InvariantCulture);
            string targetDirectory = Path.Combine(root, machineFolder);
            // 保留原始文件名；SHA-256 仅用于复制校验和同名文件复用判断。
            string targetName = Path.GetFileName(sourcePath);
            string targetPath = Path.GetFullPath(Path.Combine(targetDirectory, targetName));
            EnsureUnderRoot(targetPath, root);
            Directory.CreateDirectory(targetDirectory);

            if (File.Exists(targetPath))
                return ValidateExisting(targetPath, hash);

            string temporaryPath = targetPath + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(sourcePath, temporaryPath, false);
                string copiedHash = MappingRuleSerializer.FileSha256(temporaryPath);
                if (!string.Equals(copiedHash, hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("图片复制后的 SHA-256 校验失败。");
                try
                {
                    File.Move(temporaryPath, targetPath);
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    return ValidateExisting(targetPath, hash);
                }
                return new ImageArchiveResult
                {
                    ArchivedPath = targetPath,
                    ContentSha256 = hash,
                    ReusedExistingFile = false
                };
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public static void ApplyArchivedPath(object model, ImageArchiveDefinition definition, string archivedPath)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(archivedPath))
                throw new ArgumentException("共享图片路径不能为空。", nameof(archivedPath));
            PropertyInfo property = model.GetType().GetProperty(
                definition.PathTargetField,
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                throw new MappingValidationException(
                    "模型缺少可写的 string 图片路径字段：" + definition.PathTargetField);
            property.SetValue(model, archivedPath, null);
        }

        private static string NormalizeRoot(string sharedRootPath)
        {
            if (string.IsNullOrWhiteSpace(sharedRootPath) || !Path.IsPathRooted(sharedRootPath))
                throw new MappingValidationException("图片共享目录必须是绝对路径或 UNC 路径。");
            string fullPath = Path.GetFullPath(sharedRootPath);
            string pathRoot = Path.GetPathRoot(fullPath);
            if (string.Equals(
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return pathRoot;
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void EnsureUnderRoot(string targetPath, string root)
        {
            string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                            root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new MappingValidationException("图片目标路径超出配置的共享目录。");
        }

        private static ImageArchiveResult ValidateExisting(string targetPath, string expectedHash)
        {
            string existingHash = MappingRuleSerializer.FileSha256(targetPath);
            if (!string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("共享目录中存在同名但内容不同的图片，已拒绝覆盖。");
            return new ImageArchiveResult
            {
                ArchivedPath = targetPath,
                ContentSha256 = expectedHash,
                ReusedExistingFile = true
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Acquisition.Updater
{
    [DataContract]
    public sealed class UpdaterSettings
    {
        [DataMember(Name = "sourceDirectory")]
        public string SourceDirectory { get; set; }
    }

    [DataContract]
    public sealed class ReleaseManifest
    {
        [DataMember(Name = "version")]
        public string Version { get; set; }
        [DataMember(Name = "package")]
        public string Package { get; set; }
    }

    public sealed class UpdateResult
    {
        public bool Updated { get; set; }
        public string Message { get; set; }
    }

    public sealed class UpdateService
    {
        public const string ApplicationFileName = "文件数据采集系统.exe";
        private readonly string applicationDirectory;
        private readonly string sourceDirectory;

        public UpdateService(string applicationDirectory, string sourceDirectory)
        {
            this.applicationDirectory = Path.GetFullPath(applicationDirectory);
            this.sourceDirectory = sourceDirectory;
        }

        public static T ReadJson<T>(string path)
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        public UpdateResult ApplyLatest()
        {
            ReleaseManifest release;
            string packagePath;
            string cache = Path.Combine(applicationDirectory, "UpdateCache");
            string localVersionPath = Path.Combine(applicationDirectory, "installed-version.txt");
            var localVersion = File.Exists(localVersionPath)
                ? new Version(File.ReadAllText(localVersionPath).Trim()) : new Version(0, 0, 0, 0);
            try
            {
                release = ReadJson<ReleaseManifest>(Path.Combine(sourceDirectory, "latest.json"));
                if (new Version(release.Version) <= localVersion)
                    return new UpdateResult { Message = "当前已是最新版本。" };
                packagePath = ResolveInside(sourceDirectory, release.Package);
            }
            catch (IOException ex)
            {
                return new UpdateResult { Message = "未读取到共享版本，继续使用本地程序：" + ex.Message };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new UpdateResult { Message = "无法读取共享版本，继续使用本地程序：" + ex.Message };
            }

            Directory.CreateDirectory(cache);
            string localPackage = Path.Combine(cache, "package.zip");
            FileStream remotePackage;
            try { remotePackage = File.OpenRead(packagePath); }
            catch (IOException ex) { return new UpdateResult { Message = "更新包暂不可读，继续使用本地程序：" + ex.Message }; }
            catch (UnauthorizedAccessException ex) { return new UpdateResult { Message = "更新包无读取权限，继续使用本地程序：" + ex.Message }; }
            using (remotePackage)
            using (var local = File.Create(localPackage)) remotePackage.CopyTo(local);

            string staging = Path.Combine(cache, "staging");
            var programFiles = new List<string>();
            using (var package = ZipFile.OpenRead(localPackage))
                foreach (var entry in package.Entries)
                {
                    if (entry.Name.Length == 0) continue;
                    string destination = ResolveInside(applicationDirectory, entry.FullName);
                    string relative = destination.Substring(applicationDirectory.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    if (!IsProgramFile(relative)) continue;
                    string target = ResolveInside(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    programFiles.Add(relative);
                }
            if (!programFiles.Contains(ApplicationFileName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包缺少采集主程序，未替换本地文件。");

            // 版本记录最后替换，并与程序文件一同备份恢复。
            File.WriteAllText(Path.Combine(staging, "installed-version.txt"), release.Version, new UTF8Encoding(false));
            programFiles.Add("installed-version.txt");
            string backupPath = Path.Combine(cache, "previous.zip");
            using (var backupStream = File.Create(backupPath))
            using (var backup = new ZipArchive(backupStream, ZipArchiveMode.Create))
                foreach (string relative in programFiles)
                {
                    string target = ResolveInside(applicationDirectory, relative);
                    if (File.Exists(target)) backup.CreateEntryFromFile(target, relative);
                }

            var replaced = new List<string>();
            try
            {
                foreach (string relative in programFiles)
                {
                    string target = ResolveInside(applicationDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    replaced.Add(relative);
                    File.Copy(ResolveInside(staging, relative), target, true);
                }
            }
            catch (Exception copyError)
            {
                using (var backup = ZipFile.OpenRead(backupPath))
                    foreach (string relative in replaced.AsEnumerable().Reverse())
                    {
                        string target = ResolveInside(applicationDirectory, relative);
                        var entry = backup.GetEntry(relative);
                        try
                        {
                            if (entry == null) File.Delete(target);
                            else if (!MatchesEntry(target, entry)) entry.ExtractToFile(target, true);
                        }
                        catch (Exception restoreError)
                        {
                            throw new IOException("程序更新失败且自动恢复未完成，请保留 UpdateCache/previous.zip 并人工恢复。", new AggregateException(copyError, restoreError));
                        }
                    }
                throw;
            }
            return new UpdateResult { Updated = true, Message = "已更新到 " + release.Version };
        }

        public static bool IsProgramFile(string path)
        {
            string normalized = path.Replace('\\', '/');
            string first = normalized.Split('/')[0];
            if (new[] { "Data", "GeneratedModels", "Logs", "Log", "UpdateCache" }.Contains(first, StringComparer.OrdinalIgnoreCase)) return false;
            if (normalized.Equals("Updater.exe", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Updater.exe.", StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(normalized);
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".exe.config", StringComparison.OrdinalIgnoreCase)
                || (first.Equals("Resources", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveInside(string directory, string relative)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包路径越过目标目录：" + relative);
            return target;
        }

        private static bool MatchesEntry(string path, ZipArchiveEntry entry)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) return false;
            using (var file = File.OpenRead(path))
            using (var original = entry.Open())
            {
                var fileBuffer = new byte[65536];
                var originalBuffer = new byte[65536];
                int count;
                while ((count = original.Read(originalBuffer, 0, originalBuffer.Length)) > 0)
                {
                    int offset = 0;
                    while (offset < count)
                    {
                        int read = file.Read(fileBuffer, offset, count - offset);
                        if (read == 0) return false;
                        offset += read;
                    }
                    for (int index = 0; index < count; index++) if (fileBuffer[index] != originalBuffer[index]) return false;
                }
                return file.ReadByte() == -1;
            }
        }
    }
}

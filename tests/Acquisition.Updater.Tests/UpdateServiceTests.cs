using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Acquisition.Updater;
using Xunit;

namespace Acquisition.Updater.Tests
{
    public sealed class UpdateServiceTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "AcquisitionUpdaterTests", Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string App => Path.Combine(root, "设备程序");
        private string Source => Path.Combine(root, "共享发布");

        [Fact]
        public void Upgrade_replaces_program_and_native_dependencies_and_records_version()
        {
            WriteApp(UpdateService.ApplicationFileName, "old");
            WriteApp("Resources/CHANGELOG.md", "old release notes");
            Publish("1.0.0.2", new Dictionary<string, string> {
                [UpdateService.ApplicationFileName] = "new",
                ["x64/SQLite.Interop.dll"] = "native",
                ["Resources/CHANGELOG.md"] = "new release notes with full history",
                [UpdateService.ApplicationFileName + ".config"] = "bindings"
            });

            Assert.True(new UpdateService(App, Source).ApplyLatest().Updated);
            Assert.Equal("new", ReadApp(UpdateService.ApplicationFileName));
            Assert.Equal("native", ReadApp("x64/SQLite.Interop.dll"));
            Assert.Equal("bindings", ReadApp(UpdateService.ApplicationFileName + ".config"));
            Assert.Equal("1.0.0.2", ReadApp("installed-version.txt"));
            Assert.Equal("new release notes with full history", ReadApp("Resources/CHANGELOG.md"));
            using (var backup = ZipFile.OpenRead(Path.Combine(App, "UpdateCache", "previous.zip")))
            using (var reader = new StreamReader(backup.GetEntry(UpdateService.ApplicationFileName).Open()))
                Assert.Equal("old", reader.ReadToEnd());
            using (var backup = ZipFile.OpenRead(Path.Combine(App, "UpdateCache", "previous.zip")))
            using (var reader = new StreamReader(backup.GetEntry("Resources/CHANGELOG.md").Open()))
                Assert.Equal("old release notes", reader.ReadToEnd());
        }

        [Fact]
        public void Upgrade_preserves_local_settings_database_models_logs_and_updater()
        {
            var localFiles = new[] { "appsettings.json", "config.json", "Data/采集记录.db", "GeneratedModels/Model.cs", "Logs/run.log", "updater.json", "Updater.exe" };
            foreach (var path in localFiles) WriteApp(path, "local");
            WriteApp(UpdateService.ApplicationFileName, "old");
            var package = localFiles.ToDictionary(path => path, path => "server");
            package[UpdateService.ApplicationFileName] = "new";
            Publish("1.0.0.2", package);

            new UpdateService(App, Source).ApplyLatest();

            foreach (var path in localFiles) Assert.Equal("local", ReadApp(path));
        }

        [Theory]
        [InlineData("1.0.0.2")]
        [InlineData("1.0.0.1")]
        public void Same_or_older_release_does_not_replace_local_program(string publishedVersion)
        {
            WriteApp(UpdateService.ApplicationFileName, "local");
            WriteApp("installed-version.txt", "1.0.0.2");
            Publish(publishedVersion, new Dictionary<string, string> { [UpdateService.ApplicationFileName] = "server" });

            Assert.False(new UpdateService(App, Source).ApplyLatest().Updated);
            Assert.Equal("local", ReadApp(UpdateService.ApplicationFileName));
        }

        [Fact]
        public void Unavailable_share_keeps_the_installed_program_usable()
        {
            WriteApp(UpdateService.ApplicationFileName, "local");
            Assert.False(new UpdateService(App, Source).ApplyLatest().Updated);
            Assert.Equal("local", ReadApp(UpdateService.ApplicationFileName));
        }

        [Fact]
        public void Missing_package_keeps_the_installed_program_unchanged()
        {
            WriteApp(UpdateService.ApplicationFileName, "local");
            Write("共享发布/latest.json", "{\"version\":\"1.0.0.2\",\"package\":\"packages/missing.zip\"}");
            Assert.False(new UpdateService(App, Source).ApplyLatest().Updated);
            Assert.Equal("local", ReadApp(UpdateService.ApplicationFileName));
        }

        [Fact]
        public void Package_paths_cannot_write_outside_the_installation_directory()
        {
            WriteApp(UpdateService.ApplicationFileName, "local");
            Publish("1.0.0.2", new Dictionary<string, string> { [UpdateService.ApplicationFileName] = "server", ["../outside.dll"] = "unexpected" });
            Assert.Throws<InvalidDataException>(() => new UpdateService(App, Source).ApplyLatest());
            Assert.Equal("local", ReadApp(UpdateService.ApplicationFileName));
            Assert.False(File.Exists(Path.Combine(root, "outside.dll")));
        }

        [Fact]
        public void Failed_replacement_restores_program_files_and_does_not_advance_version()
        {
            WriteApp(UpdateService.ApplicationFileName, "old");
            WriteApp("Resources/CHANGELOG.md", "old release notes");
            WriteApp("a.dll", "old-a");
            WriteApp("z.dll", "old-z");
            WriteApp("installed-version.txt", "1.0.0.1");
            Publish("1.0.0.2", new Dictionary<string, string> {
                ["Resources/CHANGELOG.md"] = "new release notes",
                ["a.dll"] = "new-a", ["z.dll"] = "new-z", [UpdateService.ApplicationFileName] = "new"
            });
            using (File.Open(Path.Combine(App, "z.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => new UpdateService(App, Source).ApplyLatest());
            Assert.Equal("old-a", ReadApp("a.dll"));
            Assert.Equal("old", ReadApp(UpdateService.ApplicationFileName));
            Assert.Equal("1.0.0.1", ReadApp("installed-version.txt"));
            Assert.Equal("old release notes", ReadApp("Resources/CHANGELOG.md"));
        }

        [Fact]
        public void Normalized_paths_still_preserve_local_data_and_model_dependencies()
        {
            WriteApp(UpdateService.ApplicationFileName, "old");
            WriteApp("Data/cache.dll", "local-data");
            WriteApp("GeneratedModels/model.dll", "local-model");
            Publish("1.0.0.2", new Dictionary<string, string> {
                [UpdateService.ApplicationFileName] = "new",
                ["./Data/cache.dll"] = "server-data",
                ["sub/../GeneratedModels/model.dll"] = "server-model"
            });
            new UpdateService(App, Source).ApplyLatest();
            Assert.Equal("local-data", ReadApp("Data/cache.dll"));
            Assert.Equal("local-model", ReadApp("GeneratedModels/model.dll"));
        }

        private void Publish(string version, Dictionary<string, string> entries)
        {
            string zipPath = Path.Combine(Source, "packages", version + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            files.Add(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                foreach (var entry in entries)
                    using (var writer = new StreamWriter(zip.CreateEntry(entry.Key).Open())) writer.Write(entry.Value);
            Write("共享发布/latest.json", "{\"version\":\"" + version + "\",\"package\":\"packages/" + version + ".zip\"}");
        }

        private void WriteApp(string path, string content) => Write("设备程序/" + path, content);
        private string ReadApp(string path) => File.ReadAllText(Path.Combine(App, path));
        private void Write(string path, string content)
        {
            string target = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, content);
            files.Add(target);
        }

        public void Dispose()
        {
            // root 每个测试独有；逐个删除本测试目录内的文件，不递归删除目录。
            if (!Directory.Exists(root)) return;
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MachineDataAcquisitionSystem.Helpers;
using MachineDataAcquisitionSystem.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class SettingsHelperPersistenceTests
    {
        [Fact]
        public void SaveBasicConfiguration_round_trips_selected_ids_without_saving_other_pending_edits()
        {
            WithIsolatedSettings((settingsPath, cacheField) =>
            {
                SettingsHelper.SaveSettingsOrThrow(new AppSettings { MachineNamePrefix = "已保存名称" });
                AppSettings cached = SettingsHelper.LoadSettings();
                cached.MachineNamePrefix = "未保存修改";
                JObject document = JObject.Parse(File.ReadAllText(settingsPath));
                document["FutureSetting"] = "保留未知字段";
                File.WriteAllText(settingsPath, document.ToString());

                SettingsHelper.SaveBasicConfiguration(false, new[] { 5, 2, 2 });

                JObject saved = JObject.Parse(File.ReadAllText(settingsPath));
                Assert.Equal("已保存名称", saved.Value<string>("MachineNamePrefix"));
                Assert.Equal("保留未知字段", saved.Value<string>("FutureSetting"));
                Assert.Equal(new[] { 2, 5 }, cached.AutoStartMachineIds);
                cacheField.SetValue(null, null);
                Assert.Equal(new[] { 2, 5 }, SettingsHelper.LoadSettings().AutoStartMachineIds);
                Assert.False(SettingsHelper.LoadSettings().AutoStart);
            });
        }

        [Fact]
        public void Old_basic_save_preserves_selection_and_explicit_empty_selection_clears_it()
        {
            WithIsolatedSettings((settingsPath, cacheField) =>
            {
                SettingsHelper.SaveSettingsOrThrow(new AppSettings { AutoStartMachineIds = new List<int> { 2, 5 } });
                SettingsHelper.SaveBasicConfiguration(true);
                Assert.Equal(new[] { 2, 5 }, JObject.Parse(File.ReadAllText(settingsPath))["AutoStartMachineIds"].ToObject<int[]>());

                SettingsHelper.SaveBasicConfiguration(true, Array.Empty<int>());
                cacheField.SetValue(null, null);
                Assert.Empty(SettingsHelper.LoadSettings().AutoStartMachineIds);
            });
        }

        [Fact]
        public void Failed_basic_save_does_not_change_cached_or_persisted_startup_selection()
        {
            WithIsolatedSettings((settingsPath, cacheField) =>
            {
                SettingsHelper.SaveSettingsOrThrow(new AppSettings { AutoStartMachineIds = new List<int> { 2 } });
                string original = File.ReadAllText(settingsPath);
                using (File.Open(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Assert.Throws<IOException>(() => SettingsHelper.SaveBasicConfiguration(true, new[] { 5 }));

                Assert.Equal(original, File.ReadAllText(settingsPath));
                Assert.False(SettingsHelper.LoadSettings().AutoStart);
                Assert.Equal(new[] { 2 }, SettingsHelper.LoadSettings().AutoStartMachineIds);
            });
        }

        [Fact]
        public void Invalid_machine_ids_are_rejected_before_writing_settings()
        {
            WithIsolatedSettings((settingsPath, cacheField) =>
            {
                SettingsHelper.SaveSettingsOrThrow(new AppSettings());
                string original = File.ReadAllText(settingsPath);
                Assert.Throws<ArgumentOutOfRangeException>(() => SettingsHelper.SaveBasicConfiguration(true, new[] { 7 }));
                Assert.Equal(original, File.ReadAllText(settingsPath));
            });
        }

        private static void WithIsolatedSettings(Action<string, FieldInfo> test)
        {
            string path = Path.Combine(Path.GetTempPath(), "machine-startup-" + Guid.NewGuid().ToString("N") + ".json");
            FieldInfo pathField = typeof(SettingsHelper).GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo cacheField = typeof(SettingsHelper).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object originalPath = pathField.GetValue(null);
            object originalSettings = cacheField.GetValue(null);
            try
            {
                pathField.SetValue(null, path);
                cacheField.SetValue(null, null);
                test(path, cacheField);
            }
            finally
            {
                pathField.SetValue(null, originalPath);
                cacheField.SetValue(null, originalSettings);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Machine_auto_start_selection_survives_general_settings_serialization()
        {
            AppSettings settings = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(
                "{\"AutoStartMachineIds\":[2,5]}");
            JObject document = JObject.FromObject(settings);

            Assert.NotNull(document["AutoStartMachineIds"]);
            Assert.Equal(new[] { 2, 5 }, document["AutoStartMachineIds"].ToObject<int[]>());
        }

        [Fact]
        public void SaveSettingsOrThrow_round_trips_ai_and_database_configuration_after_a_fresh_load()
        {
            string settingsPath = Path.Combine(Path.GetTempPath(), "settings-roundtrip-" + Guid.NewGuid().ToString("N") + ".json");
            FieldInfo pathField = typeof(SettingsHelper).GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo cacheField = typeof(SettingsHelper).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object originalPath = pathField.GetValue(null);
            object originalSettings = cacheField.GetValue(null);
            try
            {
                pathField.SetValue(null, settingsPath);
                cacheField.SetValue(null, null);
                var saved = new AppSettings
                {
                    Databases = new List<DatabaseConfig>
                    {
                        new DatabaseConfig { Name = "生产库", DbType = "SQL Server", Server = "10.0.0.8", Password = "secret" }
                    },
                    AiMapping = new AiMappingConfig
                    {
                        Enabled = true,
                        Endpoint = "https://ai.example.test/v1/chat/completions",
                        Model = "mapping-model",
                        ApiKey = "api-secret"
                    }
                };

                SettingsHelper.SaveSettingsOrThrow(saved);
                cacheField.SetValue(null, null);
                AppSettings reloaded = SettingsHelper.LoadSettings();

                Assert.True(string.IsNullOrWhiteSpace(SettingsHelper.LastLoadError), SettingsHelper.LastLoadError);
                Assert.Equal("生产库", reloaded.Databases[0].Name);
                Assert.True(reloaded.AiMapping.Enabled);
                Assert.Equal("mapping-model", reloaded.AiMapping.Model);
            }
            finally
            {
                pathField.SetValue(null, originalPath);
                cacheField.SetValue(null, originalSettings);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }

        [Fact]
        public void SaveDatabaseConfigurations_persists_the_database_editor_changes_immediately()
        {
            string settingsPath = Path.Combine(Path.GetTempPath(), "settings-" + Guid.NewGuid().ToString("N") + ".json");
            FieldInfo pathField = typeof(SettingsHelper).GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo cacheField = typeof(SettingsHelper).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object originalPath = pathField.GetValue(null);
            object originalSettings = cacheField.GetValue(null);
            try
            {
                pathField.SetValue(null, settingsPath);
                cacheField.SetValue(null, new AppSettings());

                SettingsHelper.SaveDatabaseConfigurations(new List<DatabaseConfig>
                {
                    new DatabaseConfig { Name = "生产库", DbType = "SQL Server", Server = "10.0.0.8", Password = "secret" }
                });

                JObject json = JObject.Parse(File.ReadAllText(settingsPath));
                Assert.Equal("生产库", json["Databases"][0]["Name"].Value<string>());
                Assert.StartsWith("dpapi:", json["Databases"][0]["Password"].Value<string>(), StringComparison.Ordinal);
            }
            finally
            {
                pathField.SetValue(null, originalPath);
                cacheField.SetValue(null, originalSettings);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }

        [Fact]
        public void SaveAiMappingConfiguration_updates_only_ai_settings_and_preserves_persisted_databases()
        {
            string settingsPath = Path.Combine(Path.GetTempPath(), "settings-ai-" + Guid.NewGuid().ToString("N") + ".json");
            FieldInfo pathField = typeof(SettingsHelper).GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo cacheField = typeof(SettingsHelper).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object originalPath = pathField.GetValue(null);
            object originalSettings = cacheField.GetValue(null);
            try
            {
                pathField.SetValue(null, settingsPath);
                cacheField.SetValue(null, null);
                SettingsHelper.SaveSettingsOrThrow(new AppSettings
                {
                    Databases = new List<DatabaseConfig>
                    {
                        new DatabaseConfig { Name = "已保存数据库", DbType = "SQLite", Server = "persisted.db" }
                    }
                });
                SettingsHelper.LoadSettings().Databases[0].Name = "界面未保存修改";

                SettingsHelper.SaveAiMappingConfiguration(new AiMappingConfig
                {
                    Enabled = true,
                    Endpoint = "https://ai.example.test/v1/chat/completions",
                    Model = "mapping-model",
                    ApiKey = "api-secret",
                    TimeoutSeconds = 45
                });

                JObject json = JObject.Parse(File.ReadAllText(settingsPath));
                Assert.Equal("已保存数据库", json["Databases"][0]["Name"].Value<string>());
                Assert.Equal("mapping-model", json["AiMapping"]["Model"].Value<string>());
                Assert.StartsWith("dpapi:", json["AiMapping"]["ApiKey"].Value<string>(), StringComparison.Ordinal);
            }
            finally
            {
                pathField.SetValue(null, originalPath);
                cacheField.SetValue(null, originalSettings);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }

        [Fact]
        public void SaveBasicConfiguration_updates_only_auto_start_and_preserves_other_configuration()
        {
            string settingsPath = Path.Combine(Path.GetTempPath(), "settings-basic-" + Guid.NewGuid().ToString("N") + ".json");
            FieldInfo pathField = typeof(SettingsHelper).GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo cacheField = typeof(SettingsHelper).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic);
            object originalPath = pathField.GetValue(null);
            object originalSettings = cacheField.GetValue(null);
            try
            {
                pathField.SetValue(null, settingsPath);
                cacheField.SetValue(null, null);
                SettingsHelper.SaveSettingsOrThrow(new AppSettings
                {
                    AutoStart = false,
                    Databases = new List<DatabaseConfig>
                    {
                        new DatabaseConfig { Name = "已保存数据库", DbType = "SQLite", Server = "persisted.db" }
                    },
                    AiMapping = new AiMappingConfig
                    {
                        Enabled = true,
                        Endpoint = "https://ai.example.test/v1/chat/completions",
                        Model = "mapping-model",
                        ApiKey = "api-secret"
                    }
                });

                SettingsHelper.SaveBasicConfiguration(true);

                JObject json = JObject.Parse(File.ReadAllText(settingsPath));
                Assert.True(json["AutoStart"].Value<bool>());
                Assert.Equal("已保存数据库", json["Databases"][0]["Name"].Value<string>());
                Assert.Equal("mapping-model", json["AiMapping"]["Model"].Value<string>());
                Assert.StartsWith("dpapi:", json["AiMapping"]["ApiKey"].Value<string>(), StringComparison.Ordinal);
                Assert.True(SettingsHelper.LoadSettings().AutoStart);
            }
            finally
            {
                pathField.SetValue(null, originalPath);
                cacheField.SetValue(null, originalSettings);
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
            }
        }
    }
}

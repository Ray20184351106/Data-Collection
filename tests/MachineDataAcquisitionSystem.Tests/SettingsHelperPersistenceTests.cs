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
    }
}

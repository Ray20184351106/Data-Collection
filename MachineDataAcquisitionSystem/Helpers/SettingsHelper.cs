// Core/SettingsHelper.cs
using System;
using System.IO;
using Newtonsoft.Json;
using MachineDataAcquisitionSystem.Models;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace MachineDataAcquisitionSystem.Helpers
{
    public static class SettingsHelper
    {
        private static string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        private static AppSettings _settings;

        /// <summary>
        /// 加载默认配置
        /// </summary>
        public static AppSettings LoadSettings()
        {
            if (_settings != null)
                return _settings;

            if (!File.Exists(SettingsPath))
            {
                _settings = new AppSettings();
                SaveSettings(_settings);
                return _settings;
            }

            try
            {
                string json = File.ReadAllText(SettingsPath);
                _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                if (_settings.AiMapping == null)
                    _settings.AiMapping = new AiMappingConfig();
                bool needsMigration = false;
                if (_settings.Databases != null)
                {
                    foreach (var database in _settings.Databases)
                    {
                        if (!string.IsNullOrEmpty(database.Password))
                        {
                            if (database.Password.StartsWith("dpapi:", StringComparison.Ordinal))
                                database.Password = Unprotect(database.Password.Substring(6));
                            else
                                needsMigration = true;
                        }
                        if (!string.IsNullOrEmpty(database.ConnectionString))
                        {
                            if (database.ConnectionString.StartsWith("dpapi:", StringComparison.Ordinal))
                                database.ConnectionString = Unprotect(database.ConnectionString.Substring(6));
                            else
                                needsMigration = true;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(_settings.AiMapping.ApiKey))
                {
                    if (_settings.AiMapping.ApiKey.StartsWith("dpapi:", StringComparison.Ordinal))
                        _settings.AiMapping.ApiKey = Unprotect(_settings.AiMapping.ApiKey.Substring(6));
                    else
                        needsMigration = true;
                }
                if (needsMigration) SaveSettings(_settings);
                return _settings;
            }
            catch
            {
                _settings = new AppSettings();
                return _settings;
            }
        }

        /// <summary>
        /// 保存默认配置
        /// </summary>
        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                JObject document = JObject.FromObject(settings);
                var databases = document["Databases"] as JArray;
                if (databases != null)
                {
                    foreach (var item in databases.OfType<JObject>())
                    {
                        string password = item.Value<string>("Password");
                        if (!string.IsNullOrEmpty(password) && !password.StartsWith("dpapi:", StringComparison.Ordinal))
                            item["Password"] = "dpapi:" + Protect(password);
                        string connectionString = item.Value<string>("ConnectionString");
                        if (!string.IsNullOrEmpty(connectionString) && !connectionString.StartsWith("dpapi:", StringComparison.Ordinal))
                            item["ConnectionString"] = "dpapi:" + Protect(connectionString);
                    }
                }
                var aiMapping = document["AiMapping"] as JObject;
                if (aiMapping != null)
                {
                    string apiKey = aiMapping.Value<string>("ApiKey");
                    if (!string.IsNullOrEmpty(apiKey) && !apiKey.StartsWith("dpapi:", StringComparison.Ordinal))
                        aiMapping["ApiKey"] = "dpapi:" + Protect(apiKey);
                }
                string json = document.ToString(Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
                _settings = settings;
            }
            catch { }
        }

        private static string Protect(string value)
        {
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string Unprotect(string value)
        {
            try
            {
                byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// 根据默认配置生成机台配置
        /// </summary>
        public static System.Collections.Generic.List<MachineConfig> GenerateDefaultMachineConfigs()
        {
            var settings = LoadSettings();
            var configs = new System.Collections.Generic.List<MachineConfig>();

            for (int i = 1; i <= 6; i++)
            {
                configs.Add(new MachineConfig
                {
                    Id = i,
                    Name = $"{settings.MachineNamePrefix}{i}",
                    MonitorPath = Path.Combine(settings.DefaultBasePath, $"Machine{i}", settings.IncomingFolder),
                    SuccessPath = Path.Combine(settings.DefaultBasePath, $"Machine{i}", settings.SuccessFolder),
                    ErrorPath = Path.Combine(settings.DefaultBasePath, $"Machine{i}", settings.ErrorFolder)
                });
            }

            return configs;
        }
    }
}

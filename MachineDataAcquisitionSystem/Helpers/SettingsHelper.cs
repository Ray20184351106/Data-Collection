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
        public static string LastLoadError { get; private set; }

        /// <summary>
        /// 加载默认配置
        /// </summary>
        public static AppSettings LoadSettings()
        {
            if (_settings != null)
                return _settings;

            LastLoadError = null;

            if (!File.Exists(SettingsPath))
            {
                _settings = new AppSettings();
                SaveSettings(_settings);
                return _settings;
            }

            try
            {
                string json = File.ReadAllText(SettingsPath);
                JObject document = JObject.Parse(json);
                bool needsMigration = NormalizeAiMapping(document);
                _settings = document.ToObject<AppSettings>() ?? new AppSettings();
                if (_settings.AiMapping == null)
                    _settings.AiMapping = new AiMappingConfig();
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
            catch (Exception ex)
            {
                LastLoadError = "无法读取配置文件，原文件未被修改：" + ex.Message;
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
                SaveSettingsOrThrow(settings);
            }
            catch { }
        }

        /// <summary>
        /// Persists changes made by the database editor immediately. Unlike the
        /// general startup save path, errors are returned to the caller so the UI
        /// never reports a successful save when appsettings.json was not written.
        /// </summary>
        public static void SaveDatabaseConfigurations(System.Collections.Generic.IEnumerable<DatabaseConfig> databases)
        {
            if (databases == null) throw new ArgumentNullException(nameof(databases));
            AppSettings settings = LoadSettings();
            settings.Databases = databases.ToList();
            SaveSettingsOrThrow(settings);
        }

        /// <summary>
        /// 只保存 AI 映射配置，避免把配置窗口中其他尚未提交的编辑一并写入磁盘。
        /// </summary>
        public static void SaveAiMappingConfiguration(AiMappingConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            JObject document;
            if (File.Exists(SettingsPath))
            {
                document = JObject.Parse(File.ReadAllText(SettingsPath));
            }
            else
            {
                document = JObject.FromObject(new AppSettings());
            }

            JObject aiMapping = CreateAiMappingJson(config);
            string apiKey = aiMapping.Value<string>("ApiKey");
            if (!string.IsNullOrEmpty(apiKey) && !apiKey.StartsWith("dpapi:", StringComparison.Ordinal))
                aiMapping["ApiKey"] = "dpapi:" + Protect(apiKey);
            document["AiMapping"] = aiMapping;
            File.WriteAllText(SettingsPath, document.ToString(Formatting.Indented));

            if (_settings != null && !ReferenceEquals(_settings.AiMapping, config))
                CopyAiMapping(config, _settings.AiMapping ?? (_settings.AiMapping = new AiMappingConfig()));
        }

        /// <summary>
        /// 只保存基础配置，避免覆盖配置窗口中其他尚未提交的编辑。
        /// </summary>
        public static void SaveBasicConfiguration(
            bool autoStart,
            System.Collections.Generic.IEnumerable<int> autoStartMachineIds = null)
        {
            var selectedMachines = autoStartMachineIds?.Distinct().OrderBy(id => id).ToList();
            if (selectedMachines != null && selectedMachines.Any(id => id < 1 || id > 6))
                throw new ArgumentOutOfRangeException(nameof(autoStartMachineIds), "仅支持机台 1–6。");

            JObject document;
            if (File.Exists(SettingsPath))
            {
                document = JObject.Parse(File.ReadAllText(SettingsPath));
            }
            else
            {
                document = JObject.FromObject(new AppSettings());
            }

            document["AutoStart"] = autoStart;
            // 未传入列表的旧调用保留已有选择；空列表表示取消全部勾选。
            if (selectedMachines != null)
                document["AutoStartMachineIds"] = new JArray(selectedMachines);
            File.WriteAllText(SettingsPath, document.ToString(Formatting.Indented));

            if (_settings != null)
            {
                _settings.AutoStart = autoStart;
                if (selectedMachines != null)
                    _settings.AutoStartMachineIds = selectedMachines;
            }
        }

        public static void SaveSettingsOrThrow(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            JObject document = JObject.FromObject(settings);
            document["AiMapping"] = CreateAiMappingJson(settings.AiMapping);
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

        private static bool NormalizeAiMapping(JObject document)
        {
            JToken token = document["AiMapping"];
            if (token == null || token.Type == JTokenType.Null)
            {
                document["AiMapping"] = CreateAiMappingJson(new AiMappingConfig());
                return true;
            }

            // Previous versions serialized this property through its WinForms type
            // converter, leaving only a display string such as "未启用". Preserve
            // the rest of the configuration and migrate this value to defaults.
            if (token.Type == JTokenType.String)
            {
                document["AiMapping"] = CreateAiMappingJson(new AiMappingConfig());
                return true;
            }
            if (token.Type != JTokenType.Object)
                throw new JsonSerializationException("AiMapping must be a JSON object.");
            return false;
        }

        private static JObject CreateAiMappingJson(AiMappingConfig config)
        {
            config = config ?? new AiMappingConfig();
            return new JObject
            {
                ["Enabled"] = config.Enabled,
                ["Endpoint"] = config.Endpoint ?? string.Empty,
                ["Model"] = config.Model ?? string.Empty,
                ["ApiKey"] = config.ApiKey ?? string.Empty,
                ["TimeoutSeconds"] = config.TimeoutSeconds,
                ["AllowPrivateNetworkHttp"] = config.AllowPrivateNetworkHttp
            };
        }

        private static void CopyAiMapping(AiMappingConfig source, AiMappingConfig target)
        {
            target.Enabled = source.Enabled;
            target.Endpoint = source.Endpoint;
            target.Model = source.Model;
            target.ApiKey = source.ApiKey;
            target.TimeoutSeconds = source.TimeoutSeconds;
            target.AllowPrivateNetworkHttp = source.AllowPrivateNetworkHttp;
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

// Core/SettingsHelper.cs
using System;
using System.IO;
using Newtonsoft.Json;
using MachineDataAcquisitionSystem.Models;

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
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
                _settings = settings;
            }
            catch { }
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
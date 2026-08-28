using System;
using Microsoft.Win32;

namespace MachineDataAcquisitionSystem.Core
{
    internal interface IStartupRegistrationStore
    {
        void SetValue(string name, string value);
        void DeleteValue(string name);
    }

    internal sealed class StartupRegistrationService
    {
        public const string DefaultValueName = "文件数据采集系统";

        private readonly IStartupRegistrationStore _store;
        private readonly string _valueName;

        public StartupRegistrationService()
            : this(new CurrentUserStartupRegistrationStore(), DefaultValueName)
        {
        }

        public StartupRegistrationService(IStartupRegistrationStore store, string valueName)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(valueName))
                throw new ArgumentException("启动项名称不能为空。", nameof(valueName));
            _valueName = valueName;
        }

        public void SetEnabled(bool enabled, string executablePath)
        {
            if (!enabled)
            {
                _store.DeleteValue(_valueName);
                return;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("程序路径不能为空。", nameof(executablePath));
            if (executablePath.IndexOf('"') >= 0)
                throw new ArgumentException("程序路径不能包含双引号。", nameof(executablePath));

            _store.SetValue(_valueName, "\"" + executablePath + "\"");
        }
    }

    internal sealed class CurrentUserStartupRegistrationStore : IStartupRegistrationStore
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public void SetValue(string name, string value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
            {
                if (key == null)
                    throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
                key.SetValue(name, value, RegistryValueKind.String);
            }
        }

        public void DeleteValue(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                key?.DeleteValue(name, false);
            }
        }
    }
}

using System.Diagnostics;
using System.IO;

namespace MachineDataAcquisitionSystem.Core
{
    internal static class ApplicationLaunchService
    {
        public static string GetStartupExecutablePath(string applicationPath)
        {
            string updater = Path.Combine(Path.GetDirectoryName(applicationPath), "Updater.exe");
            return File.Exists(updater) ? updater : applicationPath;
        }
        public static ProcessStartInfo CreateRestartStartInfo(string applicationPath, int previousProcessId)
        {
            string executable = GetStartupExecutablePath(applicationPath);
            return new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(applicationPath),
                Arguments = executable == applicationPath ? string.Empty : "--wait-for-pid " + previousProcessId
            };
        }
    }
}

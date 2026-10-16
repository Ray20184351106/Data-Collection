using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MachineDataAcquisitionSystem.Core
{
    internal sealed class ApplicationReleaseService
    {
        private readonly string noticeVersionPath;

        public Version CurrentVersion { get; }
        public IReadOnlyList<ReleaseNote> Releases { get; }

        public ApplicationReleaseService(string applicationDirectory)
        {
            string installedVersionPath = Path.Combine(applicationDirectory, "installed-version.txt");
            CurrentVersion = File.Exists(installedVersionPath)
                ? new Version(File.ReadAllText(installedVersionPath).Trim())
                : typeof(ApplicationReleaseService).Assembly.GetName().Version;
            noticeVersionPath = Path.Combine(applicationDirectory, "UpdateCache", "last-notified-version.txt");

            var releases = ReadHistory(Path.Combine(applicationDirectory, "Resources", "CHANGELOG.md"));
            if (!releases.Any(release => release.Version.Equals(CurrentVersion)))
                releases.Add(new ReleaseNote
                {
                    Version = CurrentVersion,
                    ReleaseDate = string.Empty,
                    Notes = "该版本暂无更新说明"
                });
            Releases = releases.OrderByDescending(release => release.Version).ToList();
        }

        public bool ShouldShowUpdateNotice()
        {
            return !File.Exists(noticeVersionPath)
                || File.ReadAllText(noticeVersionPath).Trim() != CurrentVersion.ToString();
        }

        public void MarkUpdateNoticeShown()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(noticeVersionPath));
            File.WriteAllText(noticeVersionPath, CurrentVersion.ToString(), new UTF8Encoding(false));
        }

        private static List<ReleaseNote> ReadHistory(string path)
        {
            var releases = new List<ReleaseNote>();
            if (!File.Exists(path)) return releases;

            var heading = new Regex(@"^##\s+(\d+(?:\.\d+){1,3})\s+-\s+(\d{4}-\d{2}-\d{2})\s*$");
            ReleaseNote current = null;
            var notes = new StringBuilder();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (current != null) current.Notes = notes.ToString().Trim();
                    notes.Clear();
                    var match = heading.Match(line);
                    current = match.Success
                        ? new ReleaseNote { Version = new Version(match.Groups[1].Value), ReleaseDate = match.Groups[2].Value }
                        : null;
                    if (current != null) releases.Add(current);
                }
                else if (current != null) notes.AppendLine(line);
            }
            if (current != null) current.Notes = notes.ToString().Trim();
            return releases;
        }
    }

    internal sealed class ReleaseNote
    {
        public Version Version { get; set; }
        public string ReleaseDate { get; set; }
        public string Notes { get; set; }
        public string DisplayTitle => "v" + Version + "  " + ReleaseDate;
    }
}

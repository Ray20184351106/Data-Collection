namespace Acquisition.Agent.Setup;

public sealed class InstallationPlanner(string programFilesRoot, string programDataRoot)
{
    public InstallationPlan Create(string packageRoot, ValidatedDeployment deployment, PayloadManifest payload)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(payload);
        var versionText = deployment.AgentVersion.ToString();
        var versionDirectory = Path.GetFullPath(Path.Combine(programFilesRoot, "AcquisitionAgent", "versions", versionText));
        var programDataDirectory = Path.GetFullPath(Path.Combine(programDataRoot, "AcquisitionAgent"));
        var files = new List<FileCopyPlan>();

        foreach (var entry in payload.Files)
        {
            if (!PayloadIntegrityVerifier.TryNormalizePayloadPath(entry.Path, out var normalized, out var error))
                throw new InvalidDataException(error);
            if (string.Equals(normalized, "AgentSetup.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var relativeSegments = normalized!.Split(Path.DirectorySeparatorChar).Skip(1).ToArray();
            var relativeDestination = Path.Combine(relativeSegments);
            var source = Path.GetFullPath(Path.Combine(packageRoot, normalized));
            var destination = Path.GetFullPath(Path.Combine(versionDirectory, relativeDestination));
            EnsureWithin(destination, versionDirectory, "payload目标路径越过版本目录。");
            files.Add(new FileCopyPlan(source, destination, entry.Length, entry.Sha256));
        }

        var agentExecutable = Path.Combine(versionDirectory, "Acquisition.Agent.exe");
        if (!files.Any(file => string.Equals(file.DestinationPath, agentExecutable, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("payload缺少Acquisition.Agent.exe安装计划。");

        return new InstallationPlan(
            Path.GetFullPath(packageRoot),
            versionDirectory,
            programDataDirectory,
            Path.Combine(programDataDirectory, "agent.db"),
            Path.Combine(versionDirectory, "appsettings.json"),
            Path.Combine(programDataDirectory, "enrollment.json"),
            Path.Combine(programDataDirectory, "install-status.json"),
            Path.Combine(programDataDirectory, "identity.bin"),
            Path.Combine(programDataDirectory, "setup-diagnostic.log"),
            "AcquisitionAgent",
            "文件数据采集 Agent",
            agentExecutable,
            deployment,
            files);
    }

    private static void EnsureWithin(string path, string root, string message)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(message);
    }
}

using System.Text.Json;

namespace Acquisition.Contracts;

public sealed class ManagedMachineConfiguration
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string MonitorPath { get; set; } = "";
    public string SuccessPath { get; set; } = "";
    public string ErrorPath { get; set; } = "";
}

public sealed class MachineConfigurationDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<string> Errors { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<ManagedMachineConfiguration> Machines { get; private init; } = Array.Empty<ManagedMachineConfiguration>();
    public string PayloadJson { get; private init; } = "{\"machines\":[]}";
    public string PayloadSha256 { get; private init; } = "";

    public static MachineConfigurationDocument Create(IEnumerable<ManagedMachineConfiguration>? machines)
    {
        var errors = new List<string>();
        var normalized = (machines ?? Array.Empty<ManagedMachineConfiguration>())
            .Select(machine => Normalize(machine, errors))
            .OrderBy(machine => machine.Id)
            .ToList();

        foreach (var duplicate in normalized.GroupBy(machine => machine.Id).Where(group => group.Count() > 1))
            errors.Add($"机台编号 {duplicate.Key} 重复。");

        var paths = normalized.SelectMany(machine => new[]
        {
            (machine.Id, Label: "监控目录", Path: machine.MonitorPath),
            (machine.Id, Label: "成功目录", Path: machine.SuccessPath),
            (machine.Id, Label: "失败目录", Path: machine.ErrorPath)
        }).Where(item => !string.IsNullOrWhiteSpace(item.Path)).ToList();

        for (var left = 0; left < paths.Count; left++)
        for (var right = left + 1; right < paths.Count; right++)
        {
            if (!AreSameOrNested(paths[left].Path, paths[right].Path)) continue;
            errors.Add($"机台 {paths[left].Id} 的{paths[left].Label}与机台 {paths[right].Id} 的{paths[right].Label}不能相同或互相嵌套。");
        }

        var payload = JsonSerializer.Serialize(new MachineConfigurationEnvelope { Machines = normalized }, JsonOptions);
        return new MachineConfigurationDocument
        {
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Machines = normalized,
            PayloadJson = payload,
            PayloadSha256 = ConfigPackage.ComputeSha256(payload)
        };
    }

    private static ManagedMachineConfiguration Normalize(ManagedMachineConfiguration source, ICollection<string> errors)
    {
        if (source.Id is < 1 or > 6) errors.Add("现有采集程序只支持1到6号机台。");
        var name = (source.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) errors.Add($"机台 {source.Id} 名称不能为空。");
        if (name.Length > 100) errors.Add($"机台 {source.Id} 名称不能超过100个字符。");

        return new ManagedMachineConfiguration
        {
            Id = source.Id,
            Name = name,
            MonitorPath = NormalizePath(source.MonitorPath, source.Id, "监控目录", errors),
            SuccessPath = NormalizePath(source.SuccessPath, source.Id, "成功目录", errors),
            ErrorPath = NormalizePath(source.ErrorPath, source.Id, "失败目录", errors)
        };
    }

    private static string NormalizePath(string? value, int machineId, string label, ICollection<string> errors)
    {
        var path = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"机台 {machineId} 的{label}不能为空。");
            return path;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                errors.Add($"机台 {machineId} 的{label}必须是绝对路径。");
                return path;
            }
            return TrimTrailingSeparators(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"机台 {machineId} 的{label}不是有效路径。");
            return path;
        }
    }

    private static bool AreSameOrNested(string left, string right)
    {
        var normalizedLeft = TrimTrailingSeparators(left);
        var normalizedRight = TrimTrailingSeparators(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase)
               || normalizedLeft.StartsWith(normalizedRight + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || normalizedRight.StartsWith(normalizedLeft + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return path;
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class MachineConfigurationEnvelope
    {
        public IReadOnlyList<ManagedMachineConfiguration> Machines { get; init; } = Array.Empty<ManagedMachineConfiguration>();
    }
}

public class ConfigPreviewRequest
{
    public string Name { get; set; } = "采集配置";
    public string MinimumAgentVersion { get; set; } = "1.0.0";
    public List<string> AgentIds { get; set; } = new();
    public List<ManagedMachineConfiguration> Machines { get; set; } = new();
}

public sealed class ConfigPreviewResponse
{
    public string PreviewSha256 { get; set; } = "";
    public string PayloadJson { get; set; } = "{\"machines\":[]}";
    public string PayloadSha256 { get; set; } = "";
    public List<ConfigTargetPreview> Targets { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class ConfigTargetPreview
{
    public string AgentId { get; set; } = "";
    public bool Exists { get; set; }
    public bool IsOnline { get; set; }
    public int? EffectiveVersion { get; set; }
    public string? EffectiveSha256 { get; set; }
    public bool HasChanges { get; set; }
}

public sealed class PublishConfigVersionRequest : ConfigPreviewRequest
{
    public Guid RequestId { get; set; }
    public string PreviewSha256 { get; set; } = "";
    public string? Reason { get; set; }
}

public sealed class RollbackConfigVersionRequest
{
    public Guid RequestId { get; set; }
    public List<string> AgentIds { get; set; } = new();
    public string? Reason { get; set; }
}

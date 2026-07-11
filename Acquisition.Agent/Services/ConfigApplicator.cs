using System.Text.Json;
using Acquisition.Agent.Storage;
using Acquisition.Contracts;

namespace Acquisition.Agent.Services;

public sealed class ConfigApplicator(AgentOptions options, AgentLocalStore store)
{
    public async Task<ValidationResult> ApplyAsync(ConfigPackage package, CancellationToken cancellationToken)
    {
        var result = package.Validate();
        if (!result.IsValid) return result;
        var errors = new List<string>();
        string machineJson;
        try
        {
            using var document = JsonDocument.Parse(package.PayloadJson);
            if (!document.RootElement.TryGetProperty("machines", out var machines) || machines.ValueKind != JsonValueKind.Array)
                return new ValidationResult(false, new[] { "配置必须包含machines数组。" });
            foreach (var machine in machines.EnumerateArray())
            {
                foreach (var name in new[] { "monitorPath", "successPath", "errorPath" })
                {
                    if (!machine.TryGetProperty(name, out var path) || string.IsNullOrWhiteSpace(path.GetString()) || !Path.IsPathFullyQualified(path.GetString()!)) errors.Add($"{name}必须是绝对路径。");
                }
            }
            machineJson = machines.GetRawText();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new ValidationResult(false, new[] { "配置结构无效。" });
        }
        if (errors.Count > 0) return new ValidationResult(false, errors);

        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(options.LegacyConfigPath))
        {
            fullPath = Path.GetFullPath(options.LegacyConfigPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temporary = fullPath + ".pending";
            await File.WriteAllTextAsync(temporary, machineJson, cancellationToken);
            if (File.Exists(fullPath)) File.Copy(fullPath, fullPath + ".previous", overwrite: true);
            File.Move(temporary, fullPath, overwrite: true);
        }
        var reload = new CommandEnvelope
        {
            CommandId = package.AssignmentId, AgentId = package.AgentId, Type = AgentCommandType.ReloadApprovedConfig,
            CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(15)
        };
        await store.EnqueueLegacyCommandAsync(reload, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var reloadResult = await store.GetLegacyCommandResultAsync(reload.CommandId, cancellationToken);
            if (reloadResult.Completed)
            {
                if (!reloadResult.Succeeded)
                {
                    RestorePrevious(fullPath);
                    return new ValidationResult(false, new[] { reloadResult.Message ?? "本地采集程序拒绝了配置。" });
                }
                await store.SaveAppliedConfigAsync(package, cancellationToken);
                return new ValidationResult(true, Array.Empty<string>());
            }
            await Task.Delay(250, cancellationToken);
        }
        RestorePrevious(fullPath);
        return new ValidationResult(false, new[] { "本地采集程序未确认新配置，已恢复上一版本。" });
    }

    private static void RestorePrevious(string? fullPath)
    {
        if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath + ".previous"))
            File.Copy(fullPath + ".previous", fullPath, overwrite: true);
    }
}

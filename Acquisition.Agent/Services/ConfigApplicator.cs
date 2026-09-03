using System.Text.Json;
using Acquisition.Agent.Storage;
using Acquisition.Contracts;

namespace Acquisition.Agent.Services;

public sealed class ConfigApplicator(AgentOptions options, AgentLocalStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ValidationResult> ApplyAsync(ConfigPackage package, CancellationToken cancellationToken)
    {
        var result = package.Validate();
        if (!result.IsValid) return result;
        if (Version.TryParse(package.MinimumAgentVersion, out var minimum)
            && typeof(ConfigApplicator).Assembly.GetName().Version is { } current
            && current < minimum)
            return new ValidationResult(false, new[] { $"当前Agent版本 {current} 低于配置要求的 {minimum}。" });

        MachineConfigurationDocument document;
        try
        {
            using var json = JsonDocument.Parse(package.PayloadJson);
            if (!json.RootElement.TryGetProperty("machines", out var machines) || machines.ValueKind != JsonValueKind.Array)
                return new ValidationResult(false, new[] { "配置必须包含machines数组。" });
            var parsed = JsonSerializer.Deserialize<List<ManagedMachineConfiguration>>(machines.GetRawText(), JsonOptions) ?? new();
            document = MachineConfigurationDocument.Create(parsed);
        }
        catch (JsonException)
        {
            return new ValidationResult(false, new[] { "配置结构无效。" });
        }
        if (!document.IsValid) return new ValidationResult(false, document.Errors);

        LegacyConfigFileTransaction? fileTransaction = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(options.LegacyConfigPath))
            {
                var machineJson = JsonSerializer.Serialize(document.Machines, JsonOptions);
                fileTransaction = await LegacyConfigFileTransaction.StageAsync(options.LegacyConfigPath, machineJson, cancellationToken);
            }

            var applied = await ReloadAndWaitAsync(package.AssignmentId, cancellationToken);
            if (applied.Succeeded)
            {
                if (fileTransaction is not null) await fileTransaction.CommitAsync();
                await store.SaveAppliedConfigAsync(package, cancellationToken);
                return new ValidationResult(true, Array.Empty<string>());
            }

            if (fileTransaction is not null) await fileTransaction.RollbackAsync(cancellationToken);
            var restored = await ReloadAndWaitAsync(Guid.NewGuid(), cancellationToken);
            var message = restored.Succeeded
                ? applied.Message ?? "本地采集程序拒绝新配置，上一状态已恢复并重新加载。"
                : $"新配置应用失败；文件已恢复，但本地采集程序未确认恢复结果：{restored.Message}";
            return new ValidationResult(false, new[] { message });
        }
        finally
        {
            if (fileTransaction is not null) await fileTransaction.DisposeAsync();
        }
    }

    private async Task<(bool Succeeded, string? Message)> ReloadAndWaitAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var reload = new CommandEnvelope
        {
            CommandId = commandId,
            AgentId = options.AgentId,
            Type = AgentCommandType.ReloadApprovedConfig,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(15)
        };
        await store.EnqueueLegacyCommandAsync(reload, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var reloadResult = await store.GetLegacyCommandResultAsync(reload.CommandId, cancellationToken);
            if (reloadResult.Completed) return (reloadResult.Succeeded, reloadResult.Message);
            await Task.Delay(250, cancellationToken);
        }
        return (false, "本地采集程序未在10秒内确认配置重载。");
    }
}

using Acquisition.Agent.Storage;
using Acquisition.Contracts;

namespace Acquisition.Agent.Services;

public sealed class LegacyCommandExecutor(AgentLocalStore store)
{
    public async Task<(bool Succeeded, string Message)> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Type == AgentCommandType.RefreshStatus) return (true, "状态已刷新。");
        await store.EnqueueLegacyCommandAsync(command, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await store.GetLegacyCommandResultAsync(command.CommandId, cancellationToken);
            if (result.Completed) return (result.Succeeded, result.Message ?? (result.Succeeded ? "执行成功。" : "执行失败。"));
            await Task.Delay(250, cancellationToken);
        }
        return (false, "本地采集程序未在10秒内响应。请确认WinForms采集程序正在运行。");
    }
}

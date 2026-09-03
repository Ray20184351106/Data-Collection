using System.Diagnostics;

namespace Acquisition.Agent.Setup;

public interface IProcessRunner
{
    Task<CommandExecutionResult> RunAsync(ProcessCommandSpec command, CancellationToken cancellationToken);
}

public sealed class WindowsProcessRunner : IProcessRunner
{
    public async Task<CommandExecutionResult> RunAsync(ProcessCommandSpec command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动系统工具：{Path.GetFileName(command.FileName)}");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new CommandExecutionResult(process.ExitCode, await stdout, await stderr);
    }
}

public sealed class ScServiceManager(ServiceCommandFactory commands, IProcessRunner runner) : IServiceManager
{
    public async Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(commands.CreateQueryCommand(serviceName), cancellationToken);
        if (result.Succeeded) return true;
        if (result.ExitCode == 1060 || result.StandardOutput.Contains("1060", StringComparison.Ordinal) ||
            result.StandardError.Contains("1060", StringComparison.Ordinal)) return false;
        throw new InvalidOperationException($"无法确认Windows服务状态（退出码{result.ExitCode}）：{Trim(result.StandardError)}");
    }

    public async Task InstallAndStartAsync(InstallationPlan plan, CancellationToken cancellationToken)
    {
        foreach (var command in commands.CreateInstallCommands(plan.ServiceName, plan.ServiceDisplayName, plan.AgentExecutablePath))
        {
            var result = await runner.RunAsync(command, cancellationToken);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Windows服务操作失败（{Path.GetFileName(command.FileName)}，退出码{result.ExitCode}）：{Trim(result.StandardError)}");
        }
    }

    public async Task RemoveAsync(string serviceName, CancellationToken cancellationToken)
    {
        var removal = commands.CreateRemovalCommands(serviceName);
        await runner.RunAsync(removal[0], cancellationToken); // 服务可能尚未启动，停止失败不阻止删除明确服务。
        var result = await runner.RunAsync(removal[1], cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"无法删除回退服务（退出码{result.ExitCode}）：{Trim(result.StandardError)}");
    }

    private static string Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? "未返回错误文本。" : value.Trim().Length <= 1000 ? value.Trim() : value.Trim()[..1000];
}

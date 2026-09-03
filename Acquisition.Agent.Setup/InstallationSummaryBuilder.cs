namespace Acquisition.Agent.Setup;

public static class InstallationSummaryBuilder
{
    public static IReadOnlyList<string> Build(ValidatedDeployment deployment) =>
    [
        $"Agent编号：{deployment.AgentId}",
        $"Center地址：{deployment.CenterBaseUri}",
        $"厂区/楼层/产线：{JoinLocation(deployment)}",
        $"采集程序：{deployment.AcquisitionExecutablePath}",
        $"采集记录库：{deployment.LegacyDatabasePath}",
        $"采集配置：{deployment.LegacyConfigPath}",
        $"Agent版本：{deployment.AgentVersion}",
        "Agent服务：AcquisitionAgent（自动延迟启动）"
    ];

    private static string JoinLocation(ValidatedDeployment deployment)
    {
        var parts = new[] { deployment.Site, deployment.Building, deployment.Line }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var value = string.Join(" / ", parts);
        return value.Length == 0 ? "未填写" : value;
    }
}

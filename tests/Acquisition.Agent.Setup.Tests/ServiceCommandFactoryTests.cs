using Acquisition.Agent.Setup;

namespace Acquisition.Agent.Setup.Tests;

public sealed class ServiceCommandFactoryTests
{
    [Fact]
    public void Service_commands_use_sc_directly_and_keep_each_argument_separate()
    {
        var factory = new ServiceCommandFactory(@"C:\Windows");
        var executable = @"C:\Program Files\AcquisitionAgent\versions\1.2.3\Acquisition.Agent.exe";

        var commands = factory.CreateInstallCommands("AcquisitionAgent", "文件数据采集 Agent", executable);

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.Equal(@"C:\Windows\System32\sc.exe", command.FileName));
        Assert.All(commands, command => Assert.DoesNotContain("cmd.exe", command.FileName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command => command.Arguments.SequenceEqual(
            ["create", "AcquisitionAgent", "binPath=", $"\"{executable}\"", "start=", "delayed-auto", "DisplayName=", "文件数据采集 Agent"]));
        Assert.DoesNotContain(commands.SelectMany(command => command.Arguments), argument => argument.Contains('&') || argument.Contains('|'));
    }

    [Theory]
    [InlineData("AcquisitionAgent & whoami")]
    [InlineData("Acquisition Agent")]
    [InlineData("../service")]
    public void Unsafe_service_name_is_rejected(string serviceName)
    {
        var factory = new ServiceCommandFactory(@"C:\Windows");

        Assert.Throws<ArgumentException>(() => factory.CreateInstallCommands(
            serviceName,
            "文件数据采集 Agent",
            @"C:\Program Files\AcquisitionAgent\Acquisition.Agent.exe"));
    }
}

using Acquisition.Agent.Setup;

namespace Acquisition.Agent.Setup.Tests;

public sealed class AccessControlCommandFactoryTests
{
    [Fact]
    public void Enrollment_secret_acl_uses_icacls_directly_with_fixed_system_and_administrator_sids()
    {
        var factory = new AccessControlCommandFactory(@"C:\Windows");
        var path = @"C:\ProgramData\AcquisitionAgent\enrollment.json";

        var command = factory.CreateProtectEnrollmentCommand(path);

        Assert.Equal(@"C:\Windows\System32\icacls.exe", command.FileName);
        Assert.Equal(path, command.Arguments[0]);
        Assert.Contains("*S-1-5-18:F", command.Arguments);
        Assert.Contains("*S-1-5-32-544:F", command.Arguments);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("relative\\deployment.json")]
    [InlineData("C:\\ProgramData\\bad\"name.json")]
    [InlineData("\\\\server\\share\\deployment.json")]
    public void Unsafe_enrollment_path_is_rejected(string path)
    {
        var factory = new AccessControlCommandFactory(@"C:\Windows");

        Assert.Throws<ArgumentException>(() => factory.CreateProtectEnrollmentCommand(path));
    }
}

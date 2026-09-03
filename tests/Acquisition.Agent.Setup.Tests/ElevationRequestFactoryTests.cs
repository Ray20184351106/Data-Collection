using Acquisition.Agent.Setup;

namespace Acquisition.Agent.Setup.Tests;

public sealed class ElevationRequestFactoryTests
{
    [Fact]
    public void Elevation_request_uses_runas_and_never_places_enrollment_token_on_command_line()
    {
        var token = "secret-enrollment-token-that-must-not-be-an-argument";

        var request = ElevationRequestFactory.Create(
            @"E:\LINE01-PC01 AgentSetup\AgentSetup.exe",
            @"E:\LINE01-PC01 AgentSetup");

        Assert.Equal("runas", request.Verb);
        Assert.True(request.UseShellExecute);
        Assert.Equal(["--elevated"], request.Arguments);
        Assert.DoesNotContain(token, request.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains(token, StringComparison.Ordinal));
    }
}

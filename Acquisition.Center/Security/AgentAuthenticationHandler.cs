using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Acquisition.Center.Security;

public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "InternalAgent";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var agentId = Request.Headers["X-Agent-Id"].FirstOrDefault();
        var suppliedKey = Request.Headers["X-Registration-Key"].FirstOrDefault();
        var configuredKey = configuration["InternalLan:RegistrationKey"];
        if (string.IsNullOrWhiteSpace(agentId) || !InternalLanCredentialValidator.IsValid(configuredKey, suppliedKey))
            return Task.FromResult(AuthenticateResult.Fail("AgentId或注册码无效。"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, agentId), new Claim(ClaimTypes.Name, agentId), new Claim("kind", "agent")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Acquisition.Center.Configuration;
using Acquisition.Center.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Acquisition.Center.Security;

public sealed class AgentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CenterRuntimeOptions runtime,
    AgentCredentialService credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "InternalAgent";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var agentId = Request.Headers["X-Agent-Id"].FirstOrDefault();
        var suppliedKey = Request.Headers["X-Registration-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(suppliedKey))
            return AuthenticateResult.Fail("AgentId或注册码无效。");
        var hasDeployment = await credentials.HasDeploymentAsync(agentId, Context.RequestAborted);
        var valid = hasDeployment
            ? await credentials.IsValidAsync(agentId, suppliedKey, Context.RequestAborted)
            : runtime.AllowLegacySharedRegistrationKey
              && InternalLanCredentialValidator.IsValid(runtime.CompatibilityRegistrationKey, suppliedKey);
        if (!valid) return AuthenticateResult.Fail("AgentId或注册码无效。");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, agentId), new Claim(ClaimTypes.Name, agentId), new Claim("kind", "agent")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

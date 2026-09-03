using System.Security.Claims;
using Acquisition.Center.Bootstrap;

namespace Acquisition.Center.Security;

public sealed class DeploymentOperatorAuthenticationService(CenterBootstrapStore bootstrapStore)
{
    public const string OperatorName = "deployment-operator";
    public const string AuthenticationType = "DeploymentOperator";

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string? accessCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessCode) || accessCode.Length > 256) return null;
        var settings = await bootstrapStore.LoadAsync(cancellationToken);
        if (settings is null || !CenterBootstrapStore.VerifyDeploymentAdminAccessCode(settings, accessCode)) return null;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, OperatorName),
            new Claim(ClaimTypes.Name, OperatorName),
            new Claim("kind", "operator")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }
}

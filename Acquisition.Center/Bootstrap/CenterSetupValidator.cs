using System.Net;
using Microsoft.Data.SqlClient;

namespace Acquisition.Center.Bootstrap;

public sealed record CenterSetupValidationIssue(string Code, string Field, string Message);

public sealed record CenterSetupValidationResult(bool IsValid, IReadOnlyList<CenterSetupValidationIssue> Issues);

public static class CenterSetupValidator
{
    public static CenterSetupValidationResult Validate(CenterBootstrapDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<CenterSetupValidationIssue>();

        if (!Enum.IsDefined(draft.RunMode))
            issues.Add(new("INVALID_RUN_MODE", nameof(draft.RunMode), "运行模式无效。"));

        if (string.IsNullOrWhiteSpace(draft.BindAddress) || !IPAddress.TryParse(draft.BindAddress, out _))
            issues.Add(new("INVALID_BIND_ADDRESS", nameof(draft.BindAddress), "监听地址必须是有效的IP地址。"));

        if (draft.Port is < 1 or > 65535)
            issues.Add(new("INVALID_PORT", nameof(draft.Port), "监听端口必须在1到65535之间。"));

        ValidateAdvertisedBaseUrl(draft, issues);
        ValidateDatabase(draft, issues);

        return new CenterSetupValidationResult(issues.Count == 0, issues);
    }

    private static void ValidateAdvertisedBaseUrl(CenterBootstrapDraft draft, ICollection<CenterSetupValidationIssue> issues)
    {
        if (!Uri.TryCreate(draft.AdvertisedBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            issues.Add(new("INVALID_ADVERTISED_BASE_URL", nameof(draft.AdvertisedBaseUrl), "Agent访问地址必须是无查询参数的HTTP或HTTPS绝对地址。"));
            return;
        }

        if (uri.Host is "0.0.0.0" or "::" or "*" or "+")
            issues.Add(new("UNUSABLE_ADVERTISED_HOST", nameof(draft.AdvertisedBaseUrl), "Agent访问地址不能使用通配监听地址。"));

        if (draft.RequireHttps && uri.Scheme != Uri.UriSchemeHttps)
            issues.Add(new("HTTPS_URL_REQUIRED", nameof(draft.AdvertisedBaseUrl), "启用HTTPS时Agent访问地址必须使用https。"));

        if (draft.RunMode == CenterRunMode.Production && IsLoopbackHost(uri.Host))
            issues.Add(new("PRODUCTION_ADVERTISED_URL_NOT_REACHABLE", nameof(draft.AdvertisedBaseUrl), "正式环境的Agent访问地址不能指向本机回环地址。"));
    }

    private static void ValidateDatabase(CenterBootstrapDraft draft, ICollection<CenterSetupValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(draft.DatabaseConnectionString))
        {
            issues.Add(new("DATABASE_CONNECTION_REQUIRED", nameof(draft.DatabaseConnectionString), "数据库连接不能为空。"));
            return;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(draft.DatabaseConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
                issues.Add(new("INVALID_DATABASE_CONNECTION", nameof(draft.DatabaseConnectionString), "数据库连接必须包含服务器和数据库名。"));
            if (draft.RunMode == CenterRunMode.Production && IsLocalDb(builder.DataSource))
                issues.Add(new("PRODUCTION_LOCALDB_NOT_ALLOWED", nameof(draft.DatabaseConnectionString), "正式环境不能使用LocalDB。"));
        }
        catch (ArgumentException)
        {
            issues.Add(new("INVALID_DATABASE_CONNECTION", nameof(draft.DatabaseConnectionString), "数据库连接格式无效。"));
        }
    }

    private static bool IsLocalDb(string dataSource) =>
        dataSource.Contains("(localdb)", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
}

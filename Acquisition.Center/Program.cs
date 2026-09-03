using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Acquisition.Center.Bootstrap;
using Acquisition.Center.Configuration;
using Acquisition.Center.Data;
using Acquisition.Center.Hubs;
using Acquisition.Center.Security;
using Acquisition.Center.Services;
using Acquisition.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "文件数据采集中心服务");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var bootstrapStore = new CenterBootstrapStore();
var bootstrap = await bootstrapStore.LoadAsync();
if (bootstrap is null)
{
    // 未初始化的Center仅运行本机向导，不在内网暴露设置接口。
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
    ConfigureRateLimiting(builder.Services);

    var setupApp = builder.Build();
    ConfigureCommonPipeline(setupApp, requireHttps: false);
    setupApp.MapGet("/health", () => Results.Ok(new { status = "bootstrap-required", timestampUtc = DateTimeOffset.UtcNow })).AllowAnonymous();
    setupApp.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new { token = tokens.RequestToken });
    }).AllowAnonymous();

    var setup = setupApp.MapGroup("/api/bootstrap/v1");
    setup.MapGet("/status", async (HttpContext context, CancellationToken ct) =>
    {
        if (!IsLoopback(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var settings = await bootstrapStore.LoadAsync(ct);
        return Results.Ok(new { configured = settings is not null, restartRequired = settings is not null });
    }).AllowAnonymous();
    setup.MapPost("/validate", (CenterBootstrapDraft input, HttpContext context) =>
    {
        if (!IsLoopback(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        return Results.Ok(CenterSetupValidator.Validate(input));
    }).AllowAnonymous().RequireCsrf();
    setup.MapPost("/complete", async (CenterBootstrapDraft input, HttpContext context, CancellationToken ct) =>
    {
        if (!IsLoopback(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var created = await bootstrapStore.CreateAsync(input, ct);
        // 这是唯一包含部署管理员访问码的响应。
        return Results.Ok(new { deploymentAdminAccessCode = created.DeploymentAdminAccessCode, restartRequired = true });
    }).AllowAnonymous().RequireCsrf();
    setupApp.MapFallbackToFile("setup.html");
    setupApp.Run();
    return;
}

var effective = new EffectiveConfigurationService(builder.Configuration).Build(bootstrap);
var connectionString = builder.Configuration.GetConnectionString("CenterDatabase") ?? bootstrap.DatabaseConnectionString;
var runMode = Enum.Parse<CenterRunMode>(effective.RunMode, ignoreCase: true);
var runtimeValidation = CenterSetupValidator.Validate(new CenterBootstrapDraft
{
    RunMode = runMode,
    BindAddress = effective.BindAddress,
    Port = effective.Port,
    AdvertisedBaseUrl = effective.AdvertisedBaseUrl,
    DatabaseConnectionString = connectionString,
    InternalLanEnabled = effective.InternalLanEnabled,
    RequireHttps = effective.RequireHttps
});
if (!runtimeValidation.IsValid)
    throw new InvalidOperationException($"中心最终生效配置无效：{string.Join("; ", runtimeValidation.Issues.Select(x => x.Message))}");
if (runMode == CenterRunMode.Production && connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("生产环境必须配置正式SQL Server，不能使用LocalDB。");

var urlScheme = effective.RequireHttps ? "https" : "http";
builder.WebHost.UseUrls($"{urlScheme}://{FormatListenHost(effective.BindAddress)}:{effective.Port}");
var compatibilityKey = builder.Configuration["InternalLan:RegistrationKey"] ?? bootstrap.CenterSharedCompatibilityKey;
var allowLegacySharedRegistrationKey = builder.Configuration.GetValue("InternalLan:AllowLegacySharedRegistrationKey", builder.Environment.IsDevelopment());
var developmentLoopbackOperatorAllowed = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Security:AllowDevelopmentAnonymousOperator", true);

builder.Services.AddDbContext<CenterDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton(bootstrapStore);
builder.Services.AddSingleton(bootstrap);
builder.Services.AddSingleton(effective);
builder.Services.AddSingleton(new CenterRuntimeOptions
{
    InternalLanEnabled = effective.InternalLanEnabled,
    RequireHttps = effective.RequireHttps,
    AllowLegacySharedRegistrationKey = allowLegacySharedRegistrationKey,
    CompatibilityRegistrationKey = compatibilityKey
});
builder.Services.AddSingleton(new DeploymentPackageOptions
{
    AdvertisedBaseUrl = effective.AdvertisedBaseUrl,
    SetupExecutablePath = builder.Configuration["Deployment:SetupExecutablePath"]
        ?? Path.Combine(AppContext.BaseDirectory, "assets", "AgentSetup.exe"),
    AgentPayloadPath = builder.Configuration["Deployment:AgentPayloadPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "assets", "AgentPayload"),
    AgentVersion = builder.Configuration["Deployment:AgentVersion"] ?? "1.0.0"
});
builder.Services.AddScoped<FleetService>();
builder.Services.AddScoped<ConfigManagementService>();
builder.Services.AddScoped<AgentDeploymentService>();
builder.Services.AddScoped<AgentEnrollmentService>();
builder.Services.AddScoped<AgentCredentialService>();
builder.Services.AddScoped<AgentDiagnosticsService>();
builder.Services.AddSingleton<DeploymentOperatorAuthenticationService>();
builder.Services.AddSignalR();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "AcquisitionCenter.Operator";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = effective.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(AgentAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Agent", policy => policy.AddAuthenticationSchemes(AgentAuthenticationHandler.SchemeName).RequireClaim("kind", "agent"));
    options.AddPolicy("Viewer", policy => policy.RequireAssertion(context =>
        IsOperator(context.User) || developmentLoopbackOperatorAllowed && IsLoopbackResource(context.Resource)));
    options.AddPolicy("Operator", policy => policy.RequireAssertion(context =>
        IsOperator(context.User) || developmentLoopbackOperatorAllowed && IsLoopbackResource(context.Resource)));
});
ConfigureRateLimiting(builder.Services);

var app = builder.Build();
ConfigureCommonPipeline(app, effective.RequireHttps);
await InitializeDatabaseAsync(app.Services);

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTimeOffset.UtcNow }));
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();
app.MapGet("/api/auth/operator/status", (HttpContext context) =>
{
    var isDevelopmentOperator = developmentLoopbackOperatorAllowed && IsLoopback(context);
    var authenticated = IsOperator(context.User) || isDevelopmentOperator;
    return Results.Ok(new
    {
        authenticated,
        displayName = authenticated ? context.User.Identity?.Name ?? "development-loopback" : null
    });
}).AllowAnonymous();
app.MapPost("/api/auth/operator/login", async (OperatorLoginRequest input, HttpContext context,
    DeploymentOperatorAuthenticationService authentication, CancellationToken ct) =>
{
    var principal = await authentication.AuthenticateAsync(input.AccessCode, ct);
    if (principal is null) return Results.Unauthorized();
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = false,
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
    });
    return Results.Ok(new { authenticated = true, displayName = DeploymentOperatorAuthenticationService.OperatorName });
}).AllowAnonymous().RequireCsrf().RequireRateLimiting("operator-login");
app.MapPost("/api/auth/operator/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization("Operator").RequireCsrf();

app.MapPost("/api/agent/v1/enroll", async (AgentEnrollmentRequest input, AgentEnrollmentService service, CancellationToken ct) =>
    Results.Ok(await service.EnrollAsync(input, ct))).AllowAnonymous();

var agent = app.MapGroup("/api/agent/v1").RequireAuthorization("Agent");
agent.MapPost("/heartbeats", async (AgentHeartbeat input, ClaimsPrincipal user, FleetService service, IHubContext<FleetHub> hub, CancellationToken ct) =>
{
    if (!MatchesAgent(user, input.AgentId)) return Results.Forbid();
    var validation = ValidateRequest(input.AgentId, input.RequestId, input.TimestampUtc, input.ProtocolVersion);
    if (validation is not null) return validation;
    var accepted = await service.ReceiveHeartbeatAsync(input, ct);
    if (accepted) await hub.Clients.All.SendAsync("fleetChanged", new { input.AgentId, input.TimestampUtc }, ct);
    return Results.Ok(new { accepted });
});
agent.MapPost("/collection-records/batch", async (CollectionRecordBatch input, ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
{
    if (!MatchesAgent(user, input.AgentId)) return Results.Forbid();
    var validation = ValidateRequest(input.AgentId, input.RequestId, input.TimestampUtc, input.ProtocolVersion);
    if (validation is not null) return validation;
    if (input.Records.Count > 500) return Results.BadRequest(Error("BATCH_TOO_LARGE", "单批最多500条记录。"));
    var accepted = await service.ReceiveRecordsAsync(input, ct);
    return Results.Ok(new { accepted });
});
agent.MapGet("/commands", async (ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
    Results.Ok(await service.GetPendingCommandsAsync(user.Identity!.Name!, DateTimeOffset.UtcNow, ct)));
agent.MapPost("/commands/{id:guid}/ack", async (Guid id, CommandAcknowledgement input, ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
{
    if (id != input.CommandId || !MatchesAgent(user, input.AgentId)) return Results.Forbid();
    return await service.AcknowledgeCommandAsync(input, ct) ? Results.NoContent() : Results.NotFound(Error("NOT_FOUND", "命令不存在。"));
});
agent.MapGet("/config-assignments", async (ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
    Results.Ok(await service.GetPendingConfigsAsync(user.Identity!.Name!, ct)));
agent.MapPost("/config-assignments/{id:guid}/ack", async (Guid id, ConfigApplyResult input, ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
{
    if (id != input.AssignmentId || !MatchesAgent(user, input.AgentId)) return Results.Forbid();
    return await service.AcknowledgeConfigAsync(input, ct) ? Results.NoContent() : Results.NotFound(Error("NOT_FOUND", "配置任务不存在。"));
});

var fleet = app.MapGroup("/api/v1/fleet").RequireAuthorization("Viewer");
fleet.MapGet("/summary", async (CenterDbContext db, CancellationToken ct) =>
{
    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
    return Results.Ok(new
    {
        agents = await db.Agents.CountAsync(ct), onlineAgents = await db.Agents.CountAsync(x => x.LastHeartbeatUtc >= cutoff, ct),
        runningDevices = await db.DeviceStatuses.CountAsync(x => x.State == RuntimeState.Running, ct),
        queueDepth = await db.DeviceStatuses.SumAsync(x => (int?)x.QueueDepth, ct) ?? 0,
        todaySuccess = await db.DeviceStatuses.SumAsync(x => (int?)x.TodaySuccess, ct) ?? 0,
        todayFailure = await db.DeviceStatuses.SumAsync(x => (int?)x.TodayFailure, ct) ?? 0,
        activeAlerts = await db.Alerts.CountAsync(x => x.AcknowledgedAtUtc == null, ct)
    });
});
fleet.MapGet("/agents", async (int page, int pageSize, string? search, CenterDbContext db, CancellationToken ct) =>
{
    page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
    var query = db.Agents.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Id.Contains(search) || x.ComputerName.Contains(search));
    var total = await query.CountAsync(ct);
    var data = await query.OrderByDescending(x => x.LastHeartbeatUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    return Results.Ok(new { data, pagination = new { page, pageSize, total } });
});
fleet.MapGet("/agents/{agentId}/devices", async (string agentId, CenterDbContext db, CancellationToken ct) =>
    Results.Ok(await db.DeviceStatuses.AsNoTracking().Where(x => x.AgentId == agentId).OrderBy(x => x.Name).ToListAsync(ct)));
fleet.MapGet("/records", async (int page, int pageSize, string? agentId, bool? succeeded, CenterDbContext db, CancellationToken ct) =>
{
    page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
    var query = db.CollectionRecords.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(agentId)) query = query.Where(x => x.AgentId == agentId);
    if (succeeded.HasValue) query = query.Where(x => x.Succeeded == succeeded.Value);
    var total = await query.CountAsync(ct);
    var data = await query.OrderByDescending(x => x.ProcessedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    return Results.Ok(new { data, pagination = new { page, pageSize, total } });
});
fleet.MapGet("/alerts", async (bool activeOnly, CenterDbContext db, CancellationToken ct) => Results.Ok(await db.Alerts.AsNoTracking()
    .Where(x => !activeOnly || x.AcknowledgedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct)));

app.MapGet("/api/v1/system/effective-configuration", () => Results.Ok(effective)).RequireAuthorization("Operator");
app.MapPost("/api/v1/alerts/{id:guid}/acknowledge", async (Guid id, ClaimsPrincipal user, CenterDbContext db, CancellationToken ct) =>
{
    var alert = await db.Alerts.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (alert is null) return Results.NotFound(Error("NOT_FOUND", "告警不存在。"));
    alert.AcknowledgedAtUtc = DateTimeOffset.UtcNow; alert.AcknowledgedBy = Actor(user);
    db.AuditLogs.Add(new AuditLogEntity { Actor = Actor(user), Action = "alert.acknowledge", Target = id.ToString(), CreatedAtUtc = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct); return Results.NoContent();
}).RequireAuthorization("Operator").RequireCsrf();
app.MapPost("/api/v1/agents/{agentId}/commands", async (string agentId, CreateCommandRequest input, ClaimsPrincipal user, FleetService service, CancellationToken ct) =>
{
    if (!Enum.IsDefined(input.Type)) return Results.BadRequest(Error("INVALID_COMMAND", "命令不在白名单内。"));
    var expires = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(input.ValidForSeconds, 10, 3600));
    return Results.Ok(await service.CreateCommandAsync(agentId, input.Type, input.DeviceId, input.ParametersJson, expires, Actor(user), ct));
}).RequireAuthorization("Operator").RequireCsrf();

var deployments = app.MapGroup("/api/v1/deployments").RequireAuthorization("Operator");
deployments.MapGet("", async (AgentDeploymentService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)));
deployments.MapPost("/package", async (CreateAgentDeploymentRequest input, ClaimsPrincipal user, AgentDeploymentService service, CancellationToken ct) =>
{
    var package = await service.CreatePackageAsync(input, Actor(user), ct);
    return Results.File(package.Content, "application/zip", package.FileName);
}).RequireCsrf();

var configVersions = app.MapGroup("/api/v1/config-versions").RequireAuthorization("Operator");
configVersions.MapPost("/preview", async (ConfigPreviewRequest input, ConfigManagementService service, CancellationToken ct) =>
    Results.Ok(await service.PreviewAsync(input, ct))).RequireCsrf();
configVersions.MapPost("", async (PublishConfigVersionRequest input, ClaimsPrincipal user, ConfigManagementService service, CancellationToken ct) =>
    Results.Ok(ToConfigVersionResponse(await service.PublishAsync(input, Actor(user), ct)))).RequireCsrf();
configVersions.MapGet("", async (CenterDbContext db, CancellationToken ct) => Results.Ok(await db.ConfigVersions.AsNoTracking()
    .OrderByDescending(x => x.Version).Take(100).Select(x => new { x.Id, x.Version, x.Name, state = x.State.ToString(), x.CreatedAtUtc, x.CreatedBy, x.Reason, x.RollbackSourceVersionId }).ToListAsync(ct)));
configVersions.MapGet("/{versionId:guid}/assignments", async (Guid versionId, CenterDbContext db, CancellationToken ct) =>
{
    if (!await db.ConfigVersions.AsNoTracking().AnyAsync(x => x.Id == versionId, ct)) return Results.NotFound(Error("NOT_FOUND", "配置版本不存在。"));
    var assignments = await db.ConfigAssignments.AsNoTracking().Where(x => x.ConfigVersionId == versionId).OrderBy(x => x.AgentId)
        .Select(x => new { x.Id, x.AgentId, state = x.State == null ? null : x.State.ToString(), x.CreatedAtUtc, x.AcknowledgedAtUtc, x.Message, x.EffectiveVersion, x.EffectiveSha256, x.EffectiveObservedAtUtc }).ToListAsync(ct);
    return Results.Ok(assignments);
});
configVersions.MapPost("/{versionId:guid}/rollback", async (Guid versionId, RollbackConfigVersionRequest input, ClaimsPrincipal user, ConfigManagementService service, CancellationToken ct) =>
    Results.Ok(ToConfigVersionResponse(await service.RollbackAsync(versionId, input, Actor(user), ct)))).RequireCsrf();

app.MapPost("/api/v1/configs/sync", async (SyncConfigRequest input, ClaimsPrincipal user, ConfigManagementService service, CancellationToken ct) =>
    Results.Ok(ToConfigVersionResponse(await service.SyncInternalAsync(input.Name, input.PayloadJson, input.MinimumAgentVersion, input.AgentIds, Actor(user), ct))))
    .RequireAuthorization("Operator").RequireCsrf();

var diagnostics = app.MapGroup("/api/v1/agents/{agentId}/diagnostics").RequireAuthorization("Operator");
diagnostics.MapGet("", async (string agentId, AgentDiagnosticsService service, CancellationToken ct) =>
{
    var view = await service.GetAsync(agentId, ct);
    return view is null ? Results.NotFound(Error("NOT_FOUND", "Agent诊断不存在。")) : Results.Ok(view);
});
diagnostics.MapGet("/export", async (string agentId, AgentDiagnosticsService service, CancellationToken ct) =>
{
    var export = await service.ExportAsync(agentId, ct);
    return export is null ? Results.NotFound(Error("NOT_FOUND", "Agent诊断不存在。")) : Results.File(export.Content, "application/zip", export.FileName);
});

var hub = app.MapHub<FleetHub>("/hubs/fleet");
if (developmentLoopbackOperatorAllowed) hub.AllowAnonymous(); else hub.RequireAuthorization("Viewer");
app.MapFallbackToFile("index.html");
app.Run();

static void ConfigureRateLimiting(IServiceCollection services)
{
    services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy("operator-login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    });
}

static void ConfigureCommonPipeline(WebApplication app, bool requireHttps)
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = exception is ArgumentException ? 400 : exception is InvalidOperationException ? 409 : 500;
        context.Response.ContentType = "application/json";
        var message = context.Response.StatusCode == 500 ? "服务器处理请求失败。" : exception?.Message ?? "请求失败。";
        await context.Response.WriteAsJsonAsync(Error(context.Response.StatusCode == 409 ? "CONFLICT" : "REQUEST_FAILED", message));
    }));
    if (requireHttps) { app.UseHsts(); app.UseHttpsRedirection(); }
    app.Use(async (context, next) =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; connect-src 'self' ws: wss:; style-src 'self' 'unsafe-inline'; script-src 'self'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        await next();
    });
    app.UseRateLimiter();
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
}

static bool IsLoopback(HttpContext context) => context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
static bool IsLoopbackResource(object? resource) => resource is HttpContext context && IsLoopback(context);
static bool IsOperator(ClaimsPrincipal principal) => principal.Identity?.IsAuthenticated == true && principal.HasClaim("kind", "operator");
static string FormatListenHost(string address) => address.Contains(':', StringComparison.Ordinal) ? $"[{address}]" : address;
static bool MatchesAgent(ClaimsPrincipal user, string agentId) => string.Equals(user.Identity?.Name, agentId, StringComparison.OrdinalIgnoreCase);
static string Actor(ClaimsPrincipal user) => user.Identity?.Name ?? "internal-admin";
static IResult? ValidateRequest(string agentId, Guid requestId, DateTimeOffset timestampUtc, string protocolVersion)
{
    if (string.IsNullOrWhiteSpace(agentId) || requestId == Guid.Empty) return Results.BadRequest(Error("VALIDATION_ERROR", "AgentId和RequestId不能为空。"));
    if (!string.Equals(protocolVersion, Protocol.Version, StringComparison.Ordinal)) return Results.BadRequest(Error("PROTOCOL_MISMATCH", "协议版本不受支持。"));
    if (Math.Abs((DateTimeOffset.UtcNow - timestampUtc).TotalMinutes) > 10) return Results.BadRequest(Error("CLOCK_SKEW", "请求时间与服务器时间偏差过大。"));
    return null;
}
static ApiError Error(string code, string message) => new() { Error = new ApiError.ApiErrorBody { Code = code, Message = message } };
static object ToConfigVersionResponse(ConfigVersionEntity value) => new
{
    value.Id, value.Version, value.Name, state = value.State.ToString(), value.CreatedAtUtc, value.CreatedBy,
    value.Reason, value.RollbackSourceVersionId
};
static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CenterDbContext>();
    await DatabaseBootstrapper.InitializeAsync(db);
}

public sealed record CreateCommandRequest(AgentCommandType Type, string? DeviceId, string ParametersJson, int ValidForSeconds = 300);
public sealed record SyncConfigRequest(string Name, string PayloadJson, List<string> AgentIds, string MinimumAgentVersion = "1.0.0");
public sealed record OperatorLoginRequest(string? AccessCode);
public partial class Program;

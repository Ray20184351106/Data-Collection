using System.Security.Claims;
using System.Threading.RateLimiting;
using Acquisition.Center.Data;
using Acquisition.Center.Hubs;
using Acquisition.Center.Security;
using Acquisition.Center.Services;
using Acquisition.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "文件数据采集中心服务");
var connectionString = builder.Configuration.GetConnectionString("CenterDatabase")
    ?? throw new InvalidOperationException("缺少CenterDatabase连接字符串。");
if (!builder.Environment.IsDevelopment() && connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("生产环境必须配置正式SQL Server，不能使用LocalDB。");
var internalLanEnabled = builder.Configuration.GetValue<bool>("InternalLan:Enabled");
var requireHttps = builder.Configuration.GetValue<bool>("Security:RequireHttps");
builder.Services.AddDbContext<CenterDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<FleetService>();
builder.Services.AddScoped<ConfigManagementService>();
builder.Services.AddSignalR();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(AgentAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Agent", policy => policy.AddAuthenticationSchemes(AgentAuthenticationHandler.SchemeName).RequireClaim("kind", "agent"));
    options.AddPolicy("Viewer", policy => policy.RequireAssertion(_ => internalLanEnabled));
    options.AddPolicy("Operator", policy => policy.RequireAssertion(_ => internalLanEnabled));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
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

await InitializeDatabaseAsync(app.Services);

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTimeOffset.UtcNow }));
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();
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

var configs = app.MapGroup("/api/v1/configs");
configs.MapGet("/", async (CenterDbContext db, CancellationToken ct) => Results.Ok(await db.ConfigVersions.AsNoTracking()
    .OrderByDescending(x => x.Version).Take(100).Select(x => new { x.Id, x.Version, x.Name, state = x.State.ToString(), x.CreatedAtUtc, x.CreatedBy }).ToListAsync(ct)))
    .RequireAuthorization("Viewer");
configs.MapPost("/sync", async (SyncConfigRequest input, ClaimsPrincipal user, ConfigManagementService service, CancellationToken ct) =>
    Results.Ok(await service.SyncInternalAsync(input.Name, input.PayloadJson, input.MinimumAgentVersion, input.AgentIds, Actor(user), ct)))
    .RequireAuthorization("Operator").RequireCsrf();

app.MapHub<FleetHub>("/hubs/fleet");
app.MapFallbackToFile("index.html");
app.Run();

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
static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CenterDbContext>();
    await DatabaseBootstrapper.InitializeAsync(db);
}

public sealed record CreateCommandRequest(AgentCommandType Type, string? DeviceId, string ParametersJson, int ValidForSeconds = 300);
public sealed record SyncConfigRequest(string Name, string PayloadJson, List<string> AgentIds, string MinimumAgentVersion = "1.0.0");
public partial class Program;

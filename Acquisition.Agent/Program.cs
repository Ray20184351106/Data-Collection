using Acquisition.Agent;
using Acquisition.Agent.Services;
using Acquisition.Agent.Storage;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "文件数据采集Agent");
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
if (string.IsNullOrWhiteSpace(options.AgentId) || options.AgentId.StartsWith("replace-", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("请为Agent配置唯一AgentId。");
if (string.IsNullOrWhiteSpace(options.RegistrationKey)) throw new InvalidOperationException("Agent:RegistrationKey不能为空。");
if (!Uri.TryCreate(options.CenterBaseUrl, UriKind.Absolute, out _)) throw new InvalidOperationException("Agent:CenterBaseUrl无效。");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new AgentLocalStore(options.LocalDatabasePath));
builder.Services.AddSingleton<LegacyCommandExecutor>();
builder.Services.AddSingleton<ConfigApplicator>();
builder.Services.AddSingleton<LegacyRecordImporter>();
builder.Services.AddHttpClient<CenterClient>(client =>
{
    client.BaseAddress = new Uri(options.CenterBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHostedService<AgentWorker>();
await builder.Build().RunAsync();

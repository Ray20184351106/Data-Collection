using System.Text.Json;

namespace Acquisition.Agent.Setup;

public sealed class ManifestFileLoader
{
    private const long MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public DeploymentManifest LoadDeployment(string path) => Load<DeploymentManifest>(path, "deployment.json");

    public PayloadManifest LoadPayload(string path) => Load<PayloadManifest>(path, "payload.manifest.json");

    private static T Load<T>(string path, string displayName)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"安装包缺少{displayName}。");
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumManifestBytes)
            throw new InvalidDataException($"{displayName}大小无效。");

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{displayName}内容为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{displayName}不是有效JSON。", ex);
        }
    }
}

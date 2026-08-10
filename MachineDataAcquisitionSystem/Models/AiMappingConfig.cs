using System;
using System.ComponentModel;
using MachineDataAcquisitionSystem.Core.Mapping;
using Newtonsoft.Json;

namespace MachineDataAcquisitionSystem.Models
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [JsonObject(MemberSerialization.OptOut)]
    public sealed class AiMappingConfig
    {
        [Category("AI 自动映射")]
        [DisplayName("启用")]
        public bool Enabled { get; set; }

        [Category("AI 自动映射")]
        [DisplayName("完整 Endpoint")]
        [Description("例如 https://example.com/v1/chat/completions")]
        public string Endpoint { get; set; } = string.Empty;

        [Category("AI 自动映射")]
        [DisplayName("模型")]
        public string Model { get; set; } = string.Empty;

        [Category("AI 自动映射")]
        [DisplayName("API Key")]
        [PasswordPropertyText(true)]
        public string ApiKey { get; set; } = string.Empty;

        [Category("AI 自动映射")]
        [DisplayName("超时（秒）")]
        public int TimeoutSeconds { get; set; } = 30;

        [Category("AI 自动映射")]
        [DisplayName("允许私网 HTTP")]
        [Description("仅在内网兼容接口必须使用 HTTP 时开启；公共 HTTP 始终被拒绝。")]
        public bool AllowPrivateNetworkHttp { get; set; }

        public AiMappingClientOptions ToClientOptions()
        {
            Uri endpoint = null;
            if (!string.IsNullOrWhiteSpace(Endpoint))
                Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out endpoint);
            return new AiMappingClientOptions
            {
                Enabled = Enabled,
                Endpoint = endpoint,
                Model = Model,
                ApiKey = ApiKey,
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 30 : TimeoutSeconds),
                AllowPrivateNetworkHttp = AllowPrivateNetworkHttp
            };
        }

        public override string ToString()
        {
            return Enabled ? "已启用" : "未启用";
        }
    }
}

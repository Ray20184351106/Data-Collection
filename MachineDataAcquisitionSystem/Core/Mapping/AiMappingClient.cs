using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public interface IAiMappingClient
    {
        Task<AiMappingClientResult> SuggestMappingsAsync(AiMappingRequest request, CancellationToken cancellationToken);
    }

    public interface IAiMappingTransport
    {
        Task<AiMappingTransportResponse> SendAsync(
            Uri endpoint,
            string requestJson,
            CancellationToken cancellationToken);
    }

    public sealed class AiMappingTransportResponse
    {
        public int StatusCode { get; set; }
        public string Content { get; set; }
    }

    public sealed class AiMappingClientOptions
    {
        public bool Enabled { get; set; }
        public Uri Endpoint { get; set; }
        public string Model { get; set; }
        public string ApiKey { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool AllowPrivateNetworkHttp { get; set; }
    }

    public sealed class AiMappingClientResult
    {
        public AiMappingClientResult()
        {
            Suggestions = new List<AiMappingSuggestion>();
        }

        public bool IsSuccess { get; set; }
        public string ErrorCode { get; set; }
        public AiMappingResponse Response { get; set; }
        public List<AiMappingSuggestion> Suggestions { get; set; }

        public static AiMappingClientResult Success(AiMappingResponse response)
        {
            return new AiMappingClientResult
            {
                IsSuccess = true,
                Response = response,
                Suggestions = response == null || response.Suggestions == null
                    ? new List<AiMappingSuggestion>()
                    : new List<AiMappingSuggestion>(response.Suggestions)
            };
        }

        public static AiMappingClientResult Failure(string errorCode)
        {
            return new AiMappingClientResult { IsSuccess = false, ErrorCode = errorCode };
        }
    }

    public sealed class AiMappingClientException : InvalidOperationException
    {
        public AiMappingClientException(string message) : base(message) { }
        public AiMappingClientException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class AiMappingEndpointPolicy
    {
        public static bool IsAllowed(Uri endpoint, bool allowPrivateNetworkHttp)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri) return false;
            if (endpoint.Scheme == Uri.UriSchemeHttps) return true;
            if (endpoint.Scheme != Uri.UriSchemeHttp || !allowPrivateNetworkHttp) return false;
            return IsLoopbackOrPrivateHost(endpoint.Host);
        }

        private static bool IsLoopbackOrPrivateHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            if (!IPAddress.TryParse(host, out address)) return false;
            if (IPAddress.IsLoopback(address)) return true;
            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            }
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
                   (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);
        }
    }

    public sealed class OpenAiCompatibleMappingClient : IAiMappingClient, IDisposable
    {
        private const int MaximumResponseBytes = 1024 * 1024;
        private readonly AiMappingClientOptions _options;
        private readonly HttpClient _httpClient;
        private readonly IAiMappingTransport _transport;
        private readonly bool _ownsHttpClient;

        public OpenAiCompatibleMappingClient(AiMappingClientOptions options, HttpClient httpClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(_options);
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient == null;
        }

        public OpenAiCompatibleMappingClient(AiMappingClientOptions options, IAiMappingTransport transport)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(_options);
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public OpenAiCompatibleMappingClient(AiMappingClientOptions options, HttpMessageHandler handler)
            : this(options, new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))), true)
        {
        }

        private OpenAiCompatibleMappingClient(
            AiMappingClientOptions options,
            HttpClient httpClient,
            bool ownsHttpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(_options);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<AiMappingClientResult> SuggestMappingsAsync(
            AiMappingRequest request,
            CancellationToken cancellationToken)
        {
            if (!_options.Enabled)
                throw new AiMappingClientException("AI 自动映射未启用。");
            if (request == null) throw new ArgumentNullException(nameof(request));

            string requestJson = BuildRequestJson(request);
            if (_transport != null)
                return await SendWithTransportAsync(requestJson, cancellationToken).ConfigureAwait(false);

            using (var message = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(_options.Timeout);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(
                        message,
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return AiMappingClientResult.Failure("AI_TIMEOUT");
                }
                catch (HttpRequestException)
                {
                    return AiMappingClientResult.Failure("AI_UNAVAILABLE");
                }

                using (response)
                {
                    if (response.StatusCode == (HttpStatusCode)429)
                        return AiMappingClientResult.Failure("AI_RATE_LIMITED");
                    if (!response.IsSuccessStatusCode)
                        return AiMappingClientResult.Failure("AI_HTTP_ERROR_" + (int)response.StatusCode);

                    try
                    {
                        string body = await ReadBoundedAsync(response.Content, linked.Token).ConfigureAwait(false);
                        return AiMappingClientResult.Success(ParseResponse(body));
                    }
                    catch (AiMappingClientException)
                    {
                        return AiMappingClientResult.Failure("INVALID_AI_RESPONSE");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return AiMappingClientResult.Failure("AI_TIMEOUT");
                    }
                    catch (HttpRequestException)
                    {
                        return AiMappingClientResult.Failure("AI_UNAVAILABLE");
                    }
                }
            }
        }

        private async Task<AiMappingClientResult> SendWithTransportAsync(
            string requestJson,
            CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(_options.Timeout);
                AiMappingTransportResponse response;
                try
                {
                    response = await _transport.SendAsync(
                        _options.Endpoint,
                        requestJson,
                        linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return AiMappingClientResult.Failure("AI_TIMEOUT");
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    return AiMappingClientResult.Failure("AI_UNAVAILABLE");
                }

                if (response == null) return AiMappingClientResult.Failure("INVALID_AI_RESPONSE");
                if (response.StatusCode == 429) return AiMappingClientResult.Failure("AI_RATE_LIMITED");
                if (response.StatusCode < 200 || response.StatusCode >= 300)
                    return AiMappingClientResult.Failure("AI_HTTP_ERROR_" + response.StatusCode);
                if (Encoding.UTF8.GetByteCount(response.Content ?? string.Empty) > MaximumResponseBytes)
                    return AiMappingClientResult.Failure("INVALID_AI_RESPONSE");
                try
                {
                    return AiMappingClientResult.Success(ParseResponse(response.Content));
                }
                catch (AiMappingClientException)
                {
                    return AiMappingClientResult.Failure("INVALID_AI_RESPONSE");
                }
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient && _httpClient != null) _httpClient.Dispose();
        }

        public static void ValidateOptions(AiMappingClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!options.Enabled) return;
            Uri endpoint = options.Endpoint;
            if (endpoint == null || !endpoint.IsAbsoluteUri ||
                (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
                throw new AiMappingClientException("AI Endpoint 必须是完整的 HTTP(S) 地址。");
            if (!string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
                throw new AiMappingClientException("AI Endpoint 不能包含用户信息或片段。");
            if (endpoint.Scheme == Uri.UriSchemeHttp &&
                !AiMappingEndpointPolicy.IsAllowed(endpoint, options.AllowPrivateNetworkHttp))
                throw new AiMappingClientException("HTTP 仅可在显式开启后用于回环或私网地址。");
            if (string.IsNullOrWhiteSpace(options.Model))
                throw new AiMappingClientException("AI Model 不能为空。");
            if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(120))
                throw new AiMappingClientException("AI 超时必须在 1 到 120 秒之间。");
        }

        private string BuildRequestJson(AiMappingRequest request)
        {
            var responseContract = new
            {
                schemaVersion = "1",
                modelSchemaHash = request.ModelSchemaHash,
                suggestions = new[]
                {
                    new
                    {
                        targetField = "fieldName",
                        locator = new
                        {
                            type = "cell",
                            cell = "B2",
                            anchorCell = "A1",
                            anchorText = "known label"
                        },
                        transforms = new[] { "trim" },
                        confidence = 0.0,
                        reason = "short reason"
                    }
                },
                unmappedTargets = new[] { "fieldName" },
                assumptions = new[] { "short assumption" }
            };
            var envelope = new
            {
                model = _options.Model,
                temperature = 0,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You fill a declarative Excel mapping draft. Return JSON only. " +
                                  "Never output code, SQL, paths, expressions, or extra fields. " +
                                  "Use only the supplied target fields, locator types and transforms."
                    },
                    new
                    {
                        role = "user",
                        content = JsonConvert.SerializeObject(new
                        {
                            request,
                            responseContract
                        }, Formatting.None)
                    }
                }
            };
            return JsonConvert.SerializeObject(envelope, Formatting.None);
        }

        private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
        {
            long? contentLength = content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaximumResponseBytes)
                throw new AiMappingClientException("AI 响应超过大小上限。");

            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    if (memory.Length + count > MaximumResponseBytes)
                        throw new AiMappingClientException("AI 响应超过大小上限。");
                    memory.Write(buffer, 0, count);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        private static AiMappingResponse ParseResponse(string body)
        {
            try
            {
                JObject root = JObject.Parse(body);
                JToken content = root.SelectToken("choices[0].message.content");
                string json = content == null ? body : (string)content;
                if (string.IsNullOrWhiteSpace(json))
                    throw new JsonException("响应内容为空。");
                var response = JsonConvert.DeserializeObject<AiMappingResponse>(
                    json,
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        Culture = CultureInfo.InvariantCulture
                    });
                if (response == null) throw new JsonException("映射建议为空。");
                return response;
            }
            catch (JsonException ex)
            {
                throw new AiMappingClientException("AI 返回的映射 JSON 无效。", ex);
            }
        }
    }

    public sealed class AiMappingRequestFactory
    {
        private const int MaximumCellsSent = 500;

        public AiMappingRequest Create(MappingWorkbookSnapshot snapshot, MappingRuleDefinition rule)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrWhiteSpace(rule.ModelSchemaHash))
                throw new MappingValidationException("模型结构哈希不能为空。");
            if (rule.Fields == null || rule.Fields.Count == 0 ||
                rule.Fields.Any(field => field == null || !MappingRuleSerializer.IsIdentifier(field.TargetField)) ||
                rule.Fields.GroupBy(field => field.TargetField, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new MappingValidationException("AI 映射目标字段无效或重复。");
            var request = new AiMappingRequest
            {
                SchemaVersion = "1",
                ModelSchemaHash = rule.ModelSchemaHash,
                AllowedLocators = MappingRuleSerializer.AllowedLocatorTypes.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                AllowedTransforms = MappingRuleSerializer.AllowedTransforms
                    .Where(value => value != "valueMap" && value != "default")
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList(),
                Targets = rule.Fields.Select(field => new AiTargetField
                {
                    FieldName = field.TargetField,
                    FieldType = field.TargetType,
                    Description = field.TargetDescription,
                    IsRequired = field.IsRequired
                }).ToList()
            };

            var safeLabelTexts = new HashSet<string>(rule.Fields
                .SelectMany(field => new[] { field.TargetField, field.TargetDescription })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeLabelText), StringComparer.OrdinalIgnoreCase);
            int remaining = MaximumCellsSent;
            foreach (MappingSheetSnapshot sheet in snapshot.Sheets)
            {
                var summary = new AiSheetSummary
                {
                    Name = sheet.Name,
                    MergedRegions = sheet.MergedRegions.Take(256).ToList()
                };
                var coordinates = sheet.Cells.ToDictionary(cell => cell.Coordinate, StringComparer.Ordinal);
                foreach (MappingCellSnapshot cell in sheet.Cells)
                {
                    if (remaining-- <= 0) break;
                    bool label = LooksLikeLabel(cell, coordinates, safeLabelTexts);
                    summary.Cells.Add(new AiCellSummary
                    {
                        Coordinate = cell.Coordinate,
                        LabelText = label ? Limit(cell.DisplayText, 128) : null,
                        ValuePlaceholder = label ? null : Placeholder(cell)
                    });
                }
                request.Sheets.Add(summary);
                if (remaining <= 0) break;
            }
            return request;
        }

        private static bool LooksLikeLabel(
            MappingCellSnapshot cell,
            IDictionary<string, MappingCellSnapshot> coordinates,
            ISet<string> safeLabelTexts)
        {
            if (cell == null || cell.IsFormula || !string.Equals(cell.ValueType, CellTypeName.String, StringComparison.Ordinal))
                return false;
            string text = cell.DisplayText ?? string.Empty;
            if (text.Length == 0 || text.Length > 128) return false;
            if (safeLabelTexts == null || !safeLabelTexts.Contains(NormalizeLabelText(text)))
                return false;

            int row;
            int column;
            if (!TryParseCoordinate(cell.Coordinate, out row, out column)) return false;
            string right = ToCoordinate(row, column + 1);
            string below = ToCoordinate(row + 1, column);
            return coordinates.ContainsKey(right) || coordinates.ContainsKey(below);
        }

        private static string NormalizeLabelText(string value)
        {
            string normalized = string.Concat((value ?? string.Empty)
                .Normalize(NormalizationForm.FormKC)
                .Where(character => !char.IsWhiteSpace(character)));
            return normalized.TrimEnd(':', '：');
        }

        private static string Placeholder(MappingCellSnapshot cell)
        {
            if (cell.FormulaCacheMissing) return "<formula-cache-missing>";
            string type = (cell.ValueType ?? string.Empty).ToLowerInvariant();
            if (type.Contains("numeric")) return "<number>";
            if (type.Contains("boolean")) return "<boolean>";
            if (type.Contains("date")) return "<date>";
            return cell.IsFormula ? "<formula-cached-value>" : "<string>";
        }

        private static string Limit(string value, int maximum)
        {
            return value == null || value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static bool TryParseCoordinate(string coordinate, out int row, out int column)
        {
            row = 0;
            column = 0;
            if (string.IsNullOrWhiteSpace(coordinate)) return false;
            int split = 0;
            while (split < coordinate.Length && char.IsLetter(coordinate[split])) split++;
            int oneBasedRow;
            if (split == 0 || split == coordinate.Length || !int.TryParse(coordinate.Substring(split), out oneBasedRow))
                return false;
            int oneBasedColumn = 0;
            foreach (char character in coordinate.Substring(0, split).ToUpperInvariant())
                oneBasedColumn = oneBasedColumn * 26 + character - 'A' + 1;
            row = oneBasedRow - 1;
            column = oneBasedColumn - 1;
            return row >= 0 && column >= 0;
        }

        private static string ToCoordinate(int row, int column)
        {
            int value = column + 1;
            string letters = string.Empty;
            while (value > 0)
            {
                value--;
                letters = (char)('A' + value % 26) + letters;
                value /= 26;
            }
            return letters + (row + 1);
        }

        private static class CellTypeName
        {
            public const string String = "String";
        }
    }
}

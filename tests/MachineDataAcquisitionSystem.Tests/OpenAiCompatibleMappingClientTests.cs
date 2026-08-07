using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class OpenAiCompatibleMappingClientTests
    {
        [Fact]
        public async Task SuggestMappingsAsync_returns_no_suggestions_when_the_request_times_out()
        {
            var options = NewOptions();
            options.Timeout = TimeSpan.FromMilliseconds(100);
            var client = new OpenAiCompatibleMappingClient(options, new HangingHandler());

            AiMappingClientResult result = await client.SuggestMappingsAsync(
                NewRequest(),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("AI_TIMEOUT", result.ErrorCode);
            Assert.Empty(result.Suggestions);
        }

        [Fact]
        public async Task SuggestMappingsAsync_returns_no_suggestions_for_invalid_json()
        {
            var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json")
            });
            var client = new OpenAiCompatibleMappingClient(NewOptions(), handler);

            AiMappingClientResult result = await client.SuggestMappingsAsync(
                NewRequest(),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_AI_RESPONSE", result.ErrorCode);
            Assert.Empty(result.Suggestions);
        }

        [Fact]
        public async Task SuggestMappingsAsync_rejects_unknown_nested_response_fields()
        {
            const string responseJson = @"{
  ""schemaVersion"": ""1"",
  ""modelSchemaHash"": ""schema-v1"",
  ""suggestions"": [{
    ""targetField"": ""SerialNumber"",
    ""locator"": {
      ""type"": ""cell"",
      ""cell"": ""B2"",
      ""anchorCell"": ""A1"",
      ""anchorText"": ""Serial number""
    },
    ""transforms"": [],
    ""confidence"": 0.9,
    ""reason"": ""matched label"",
    ""sql"": ""DROP TABLE DataModels""
  }],
  ""unmappedTargets"": [],
  ""assumptions"": []
}";
            var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
            var client = new OpenAiCompatibleMappingClient(NewOptions(), handler);

            AiMappingClientResult result = await client.SuggestMappingsAsync(
                NewRequest(),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_AI_RESPONSE", result.ErrorCode);
            Assert.Empty(result.Suggestions);
        }

        [Fact]
        public void Endpoint_policy_allows_private_http_only_after_explicit_opt_in()
        {
            var privateEndpoint = new Uri("http://192.168.10.20/v1/chat/completions");

            Assert.False(AiMappingEndpointPolicy.IsAllowed(privateEndpoint, allowPrivateNetworkHttp: false));
            Assert.True(AiMappingEndpointPolicy.IsAllowed(privateEndpoint, allowPrivateNetworkHttp: true));
        }

        [Fact]
        public void Endpoint_policy_never_allows_public_http_even_with_private_http_opt_in()
        {
            var publicEndpoint = new Uri("http://203.0.113.10/v1/chat/completions");

            Assert.False(AiMappingEndpointPolicy.IsAllowed(publicEndpoint, allowPrivateNetworkHttp: true));
        }

        [Fact]
        public void Request_factory_only_exposes_labels_already_present_in_model_metadata()
        {
            var snapshot = new MappingWorkbookSnapshot
            {
                FileExtension = ".xlsx",
                FileSha256 = "sample",
                Sheets = new List<MappingSheetSnapshot>
                {
                    new MappingSheetSnapshot
                    {
                        Name = "Data",
                        Cells = new List<MappingCellSnapshot>
                        {
                            new MappingCellSnapshot { Coordinate = "A1", DisplayText = "序列号", ValueType = "String" },
                            new MappingCellSnapshot { Coordinate = "B1", DisplayText = "SN-SECRET-001", ValueType = "String" },
                            new MappingCellSnapshot { Coordinate = "A2", DisplayText = "客户名称", ValueType = "String" },
                            new MappingCellSnapshot { Coordinate = "B2", DisplayText = "ACME SECRET", ValueType = "String" }
                        }
                    }
                }
            };
            var rule = new MappingRuleDefinition
            {
                RuleName = "ai-request",
                ModelId = 7,
                TargetModelType = "InspectionRecord",
                ModelSchemaHash = "schema-v1",
                NormalizedExtension = ".xlsx",
                SheetName = "Data",
                Fields = new List<FieldMappingRule>
                {
                    new FieldMappingRule
                    {
                        TargetField = "SerialNumber",
                        TargetType = "string",
                        TargetDescription = "序列号",
                        IsRequired = true,
                        Locator = new MappingLocator
                        {
                            Type = "cell",
                            Cell = "A1",
                            AnchorCell = "A1",
                            AnchorText = "序列号"
                        }
                    }
                }
            };

            AiMappingRequest request = new AiMappingRequestFactory().Create(snapshot, rule);

            Assert.Equal("序列号", request.Sheets[0].Cells.Single(cell => cell.Coordinate == "A1").LabelText);
            Assert.All(
                request.Sheets[0].Cells.Where(cell => cell.Coordinate != "A1"),
                cell => Assert.Null(cell.LabelText));
            Assert.DoesNotContain("valueMap", request.AllowedTransforms);
            Assert.DoesNotContain("default", request.AllowedTransforms);
        }

        private static AiMappingClientOptions NewOptions()
        {
            return new AiMappingClientOptions
            {
                Enabled = true,
                Endpoint = new Uri("https://api.example.test/v1/chat/completions"),
                Model = "mapping-model",
                ApiKey = "test-key",
                Timeout = TimeSpan.FromSeconds(30),
                AllowPrivateNetworkHttp = false
            };
        }

        private static AiMappingRequest NewRequest()
        {
            return new AiMappingRequest
            {
                SchemaVersion = "1",
                ModelSchemaHash = "schema-v1"
            };
        }

        private sealed class StaticResponseHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public StaticResponseHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }

        private sealed class HangingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<HttpResponseMessage>();
                cancellationToken.Register(() => completion.TrySetCanceled());
                return completion.Task;
            }
        }
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Mapping;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class AiMappingConnectionTesterTests
    {
        [Fact]
        public async Task TestAsync_uses_the_entered_model_and_can_test_a_disabled_configuration()
        {
            var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""choices"":[]}")
            });
            var options = new AiMappingClientOptions
            {
                Enabled = false,
                Endpoint = new Uri("https://api.example.test/v1/chat/completions"),
                Model = "mapping-model",
                ApiKey = "test-key",
                Timeout = TimeSpan.FromSeconds(5)
            };

            AiConnectionTestResult result;
            using (var tester = new AiMappingConnectionTester(handler))
                result = await tester.TestAsync(options, CancellationToken.None);

            Assert.True(result.IsSuccess, result.ErrorCode);
            Assert.Equal("Bearer", handler.AuthorizationScheme);
            Assert.Equal("test-key", handler.AuthorizationParameter);
            Assert.Contains("mapping-model", handler.RequestBody);
            Assert.DoesNotContain("test-key", handler.RequestBody);
        }

        [Fact]
        public async Task TestAsync_reports_the_http_status_when_the_endpoint_rejects_the_request()
        {
            var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var options = new AiMappingClientOptions
            {
                Enabled = true,
                Endpoint = new Uri("https://api.example.test/v1/chat/completions"),
                Model = "mapping-model",
                ApiKey = "invalid-key",
                Timeout = TimeSpan.FromSeconds(5)
            };

            AiConnectionTestResult result;
            using (var tester = new AiMappingConnectionTester(handler))
                result = await tester.TestAsync(options, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("AI_HTTP_ERROR_401", result.ErrorCode);
        }

        [Fact]
        public async Task TestAsync_rejects_a_success_status_from_a_non_compatible_endpoint()
        {
            var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ordinary web page")
            });
            var options = new AiMappingClientOptions
            {
                Enabled = true,
                Endpoint = new Uri("https://api.example.test/v1/chat/completions"),
                Model = "mapping-model",
                Timeout = TimeSpan.FromSeconds(5)
            };

            AiConnectionTestResult result;
            using (var tester = new AiMappingConnectionTester(handler))
                result = await tester.TestAsync(options, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("INVALID_AI_RESPONSE", result.ErrorCode);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;

            public CapturingHandler(HttpResponseMessage response)
            {
                _response = response;
            }

            public string AuthorizationScheme { get; private set; }
            public string AuthorizationParameter { get; private set; }
            public string RequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                AuthorizationScheme = request.Headers.Authorization == null
                    ? null
                    : request.Headers.Authorization.Scheme;
                AuthorizationParameter = request.Headers.Authorization == null
                    ? null
                    : request.Headers.Authorization.Parameter;
                RequestBody = await request.Content.ReadAsStringAsync();
                return _response;
            }
        }
    }
}

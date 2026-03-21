using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using shmoxy.api.ipc;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.ipc;

public class ProxyIpcClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetStatusAsync_ReturnsProxyStatus()
    {
        var json = """{"isListening":true,"port":8080,"uptime":"00:01:30","activeConnections":5}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var status = await client.GetStatusAsync();

        Assert.True(status.IsListening);
        Assert.Equal(8080, status.Port);
        Assert.Equal(5, status.ActiveConnections);
    }

    [Fact]
    public async Task ShutdownAsync_ReturnsSuccess()
    {
        var json = """{"success":true,"message":"Shutdown initiated"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var response = await client.ShutdownAsync();

        Assert.True(response.Success);
        Assert.Equal("Shutdown initiated", response.Message);
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsConfig()
    {
        var json = """{"port":8080,"certPath":null,"keyPath":null,"logLevel":1,"maxConcurrentConnections":8,"certStoragePath":"/tmp"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var config = await client.GetConfigAsync();

        Assert.Equal(8080, config.Port);
        Assert.Equal(ProxyConfig.LogLevelEnum.Info, config.LogLevel);
    }

    [Fact]
    public async Task UpdateConfigAsync_SendsConfig()
    {
        var requestConfig = new ProxyConfig { Port = 9090, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        var responseJson = """{"port":9090,"certPath":null,"keyPath":null,"logLevel":0,"maxConcurrentConnections":8,"certStoragePath":"/tmp"}""";
        
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            capturedRequests.Add(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);

        var result = await client.UpdateConfigAsync(requestConfig);

        Assert.Single(capturedRequests);
        var capturedRequest = capturedRequests[0];
        Assert.Equal(HttpMethod.Put, capturedRequest.Method);
        Assert.Contains("/ipc/config", capturedRequest.RequestUri?.ToString());
        Assert.Equal(9090, result.Port);
    }

    [Fact]
    public async Task GetHooksAsync_ReturnsHooks()
    {
        var json = """[{"id":"inspection","name":"Request/Response Inspection","type":"builtin","enabled":false}]""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var hooks = await client.GetHooksAsync();

        Assert.Single(hooks);
        Assert.Equal("inspection", hooks[0].Id);
        Assert.Equal("builtin", hooks[0].Type);
        Assert.False(hooks[0].Enabled);
    }

    [Fact]
    public async Task EnableHookAsync_SendsRequest()
    {
        var json = """{"success":true,"message":"Hook enabled"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var response = await client.EnableHookAsync("inspection");

        Assert.True(response.Success);
    }

    [Fact]
    public async Task DisableHookAsync_SendsRequest()
    {
        var json = """{"success":true,"message":"Hook disabled"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var response = await client.DisableHookAsync("inspection");

        Assert.True(response.Success);
    }

    [Fact]
    public async Task EnableInspectionAsync_ReturnsSuccess()
    {
        var json = """{"success":true,"message":"Inspection enabled"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var response = await client.EnableInspectionAsync();

        Assert.True(response.Success);
    }

    [Fact]
    public async Task DisableInspectionAsync_ReturnsSuccess()
    {
        var json = """{"success":true,"message":"Inspection disabled"}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var response = await client.DisableInspectionAsync();

        Assert.True(response.Success);
    }

    [Fact]
    public async Task GetRootCertPemAsync_ReturnsPem()
    {
        var pem = "-----BEGIN CERTIFICATE-----\nMIIC...\n-----END CERTIFICATE-----";
        var client = CreateClientWithResponse(pem, HttpStatusCode.OK, "text/plain");

        var result = await client.GetRootCertPemAsync();

        Assert.Contains("-----BEGIN CERTIFICATE-----", result);
    }

    [Fact]
    public async Task GetRootCertDerAsync_ReturnsDer()
    {
        var derBytes = Encoding.UTF8.GetBytes("fake-der-data");
        var client = CreateClientWithResponse(derBytes, HttpStatusCode.OK, "application/x-x509-ca-cert");

        var result = await client.GetRootCertDerAsync();

        Assert.Equal(derBytes, result);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsTrue_WhenProxyHealthy()
    {
        var json = """{"isListening":true,"port":8080,"uptime":"00:00:01","activeConnections":0}""";
        var client = CreateClientWithResponse(json, HttpStatusCode.OK);

        var healthy = await client.IsHealthyAsync();

        Assert.True(healthy);
    }

    [Fact]
    public async Task IsHealthyAsync_ReturnsFalse_WhenProxyUnreachable()
    {
        var client = CreateClientThatThrows(new HttpRequestException("Connection refused"));

        var healthy = await client.IsHealthyAsync();

        Assert.False(healthy);
    }

    [Fact]
    public async Task RetryAsync_RetriesOn5xx()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            if (callCount < 3)
            {
                throw new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"isListening":true,"port":8080,"uptime":"00:00:01","activeConnections":0}""")
            };
        });

        var client = CreateClient(handler);

        var status = await client.GetStatusAsync();

        Assert.True(status.IsListening);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task RetryAsync_DoesNotRetryOn4xx()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            throw new HttpRequestException("Not found", null, HttpStatusCode.NotFound);
        });

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStatusAsync());
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetInspectionStreamAsync_ParsesSseEvents()
    {
        var sseData = """
            data: {"timestamp":"2024-01-01T00:00:00Z","eventType":"request","method":"GET","url":"http://example.com","statusCode":null,"headers":{},"body":null}
            
            data: {"timestamp":"2024-01-01T00:00:01Z","eventType":"response","method":"GET","url":"http://example.com","statusCode":200,"headers":{},"body":null}
            
            """;
        var client = CreateClientWithResponse(sseData, HttpStatusCode.OK, "text/event-stream");

        var events = new List<InspectionEvent>();
        await foreach (var evt in client.GetInspectionStreamAsync())
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("request", events[0].EventType);
        Assert.Equal("response", events[1].EventType);
        Assert.Equal(200, events[1].StatusCode);
    }

    private static ProxyIpcClient CreateClientWithResponse(
        string response,
        HttpStatusCode statusCode,
        string contentType = "application/json",
        Action<HttpRequestMessage>? onRequest = null)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            onRequest?.Invoke(request);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, contentType)
            };
        });

        return CreateClient(handler);
    }

    private static ProxyIpcClient CreateClientWithResponse(
        byte[] responseBytes,
        HttpStatusCode statusCode,
        string contentType = "application/octet-stream")
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(responseBytes)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) }
                }
            };
        });

        return CreateClient(handler);
    }

    private static ProxyIpcClient CreateClientThatThrows(Exception ex)
    {
        var handler = new FakeHttpMessageHandler(request => throw ex);
        return CreateClient(handler);
    }

    private static ProxyIpcClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var logger = new LoggerFactory().CreateLogger<ProxyIpcClient>();
        return new ProxyIpcClient(httpClient, logger);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _sendAsync;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_sendAsync(request));
        }
    }
}

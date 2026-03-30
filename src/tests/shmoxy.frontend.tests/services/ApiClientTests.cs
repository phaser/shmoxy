using System.Net;
using System.Text;
using System.Text.Json;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class ApiClientTests
{
    [Fact]
    public async Task SaveProxyConfigAsync_On400_ThrowsWithDetailedMessage()
    {
        var errorResponse = new { Message = "Port must be between 1 and 65535" };
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse), Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => apiClient.SaveProxyConfigAsync(new FrontendProxyConfig { Port = 99999 }));

        Assert.Contains("Port must be between 1 and 65535", ex.Message);
    }

    [Fact]
    public async Task SaveProxyConfigAsync_On200_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);

        await apiClient.SaveProxyConfigAsync(new FrontendProxyConfig());
    }

    private class MockHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}

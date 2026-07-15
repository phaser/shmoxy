using System.Net;
using System.Text;
using System.Text.Json;
using shmoxy.frontend.models;
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

    [Fact]
    public async Task SaveTraceAsync_PostsToSavedTraces_AndReturnsSummary()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"trace-1\",\"method\":\"GET\",\"url\":\"https://example.com\"}", Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);

        var summary = await apiClient.SaveTraceAsync(new SavedTraceData { Method = "GET", Url = "https://example.com" });

        Assert.Equal("trace-1", summary.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/saved-traces", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateSavedTraceNoteAsync_SendsPatch()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);

        await apiClient.UpdateSavedTraceNoteAsync("trace-1", "a note");

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Equal("/api/saved-traces/trace-1", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeleteSavedTraceAsync_On404_ThrowsWithMessage()
    {
        var errorResponse = new { Message = "Saved trace 'x' not found" };
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse), Encoding.UTF8, "application/json")
            });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => apiClient.DeleteSavedTraceAsync("x"));

        Assert.Contains("not found", ex.Message);
    }

    private class MockHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}

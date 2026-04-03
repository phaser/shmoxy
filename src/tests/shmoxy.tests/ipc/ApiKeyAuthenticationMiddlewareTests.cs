using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Net;
using shmoxy.ipc;

namespace shmoxy.tests.ipc;

public class ApiKeyAuthenticationMiddlewareTests
{
    private const string ValidApiKey = "test-api-key-123";

    private static ApiKeyService CreateApiKeyService(string? apiKey = ValidApiKey)
    {
        return new ApiKeyService { ApiKey = apiKey };
    }

    private static DefaultHttpContext CreateHttpContext(
        IPAddress? localIp = null,
        IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();

        var connectionFeature = new HttpConnectionFeature
        {
            LocalIpAddress = localIp,
            RemoteIpAddress = remoteIp
        };
        context.Features.Set<IHttpConnectionFeature>(connectionFeature);

        return context;
    }

    private static DefaultHttpContext CreateUnixSocketContext()
    {
        // Unix socket connections have null IP addresses
        return CreateHttpContext(localIp: null, remoteIp: null);
    }

    private static DefaultHttpContext CreateTcpContext()
    {
        return CreateHttpContext(
            localIp: IPAddress.Loopback,
            remoteIp: IPAddress.Loopback);
    }

    [Fact]
    public async Task UnixSocket_BypassesAuthentication()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateUnixSocketContext();
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TcpConnection_WithoutApiKey_Returns401()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateTcpContext();
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task TcpConnection_WithWrongApiKey_Returns401()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateTcpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task TcpConnection_WithCorrectApiKey_PassesThrough()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateTcpContext();
        context.Request.Headers["X-API-Key"] = ValidApiKey;
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TcpConnection_WithEmptyApiKey_Returns401()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateTcpContext();
        context.Request.Headers["X-API-Key"] = "";
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task TcpConnection_WithNullServiceApiKey_Returns401()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService(apiKey: null));

        var context = CreateTcpContext();
        context.Request.Headers["X-API-Key"] = "any-key";
        await middleware.InvokeAsync(context);

        // null != "any-key", so this should return 401
        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task NoConnectionFeature_RequiresAuthentication()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        // DefaultHttpContext without setting connection feature
        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        // Without connection feature, authentication is required and no key = 401
        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnixSocket_DoesNotCheckApiKey()
    {
        var nextCalled = false;
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateApiKeyService());

        var context = CreateUnixSocketContext();
        // No API key header set — should still pass through for Unix socket
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Response_ContainsJsonError_On401()
    {
        var middleware = new ApiKeyAuthenticationMiddleware(
            _ => Task.CompletedTask,
            CreateApiKeyService());

        var context = CreateTcpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Contains("Unauthorized", body);
        Assert.Equal("application/json", context.Response.Headers.ContentType.ToString());
    }
}

/// <summary>
/// Implementation of IHttpConnectionFeature for testing.
/// </summary>
internal class HttpConnectionFeature : IHttpConnectionFeature
{
    public string ConnectionId { get; set; } = "";
    public IPAddress? LocalIpAddress { get; set; }
    public int LocalPort { get; set; }
    public IPAddress? RemoteIpAddress { get; set; }
    public int RemotePort { get; set; }
}

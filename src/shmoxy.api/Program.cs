using Microsoft.Extensions.Options;
using shmoxy.api.models.configuration;
using shmoxy.api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiConfig>(builder.Configuration.GetSection("ApiConfig"));

var app = builder.Build();

var config = app.Services.GetRequiredService<IOptions<ApiConfig>>().Value;
if (!string.IsNullOrEmpty(config.ProxyIpcSocketPath))
{
    builder.Services.AddUnixSocketIpcClient(config.ProxyIpcSocketPath);
}

app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

app.Run();

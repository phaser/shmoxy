using shmoxy.api.models.configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiConfig>(builder.Configuration.GetSection("ApiConfig"));

var app = builder.Build();

app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

app.Run();

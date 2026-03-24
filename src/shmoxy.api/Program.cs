using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using shmoxy.api.models.configuration;
using shmoxy.api;
using shmoxy.api.server;
using shmoxy.api.data;
using shmoxy.frontend.extensions;

var app = Program.CreateApp(args);
app.Run();

public partial class Program
{
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<ApiConfig>(builder.Configuration.GetSection("ApiConfig"));

        var config = builder.Configuration.GetSection("ApiConfig").Get<ApiConfig>() ?? new ApiConfig();

        if (!string.IsNullOrEmpty(config.ProxyIpcSocketPath))
        {
            builder.Services.AddUnixSocketIpcClient(config.ProxyIpcSocketPath);
        }
        else
        {
            builder.Services.AddProxyIpcClient();
        }

        builder.Services.AddSingleton<IProxyProcessManager, ProxyProcessManager>();

        if (config.AutoStartProxy)
        {
            builder.Services.AddHostedService<ProxyHostedService>();
        }

        // Only register DbContext if not already registered (allows test overrides)
        if (!builder.Services.Any(s => s.ServiceType == typeof(DbContextOptions<ProxiesDbContext>)))
        {
            var connectionString = config.ConnectionString ?? GetDefaultConnectionString();
            builder.Services.AddSqliteDbContext(connectionString);
            builder.Services.AddRemoteProxyRegistry();
        }

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        // Add Blazor frontend (from shmoxy.frontend)
        builder.Services.AddBlazorFrontend();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();
            dbContext.Database.EnsureCreated();
        }

        app.UseBlazorFrontendMiddleware();
        app.MapControllers();
        app.MapBlazorFrontend();

        app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

        return app;
    }

    private static string GetDefaultConnectionString()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var shmoxyDir = Path.Combine(appDataPath, "shmoxy-api");
        Directory.CreateDirectory(shmoxyDir);
        var dbPath = Path.Combine(shmoxyDir, "proxies.db");
        return $"Data Source={dbPath}";
    }
}

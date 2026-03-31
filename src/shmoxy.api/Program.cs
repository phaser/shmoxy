using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

        builder.Services.AddSingleton<IConfigPersistence, JsonConfigPersistence>();
        builder.Services.AddSingleton<IProxyProcessManager, ProxyProcessManager>();
        builder.Services.AddHostedService<ProxyHostedService>();

        // Only register DbContext if not already registered (allows test overrides)
        if (!builder.Services.Any(s => s.ServiceType == typeof(DbContextOptions<ProxiesDbContext>)))
        {
            var connectionString = config.ConnectionString ?? GetDefaultConnectionString();
            builder.Services.AddSqliteDbContext(connectionString);
            builder.Services.AddRemoteProxyRegistry();
            builder.Services.AddSessionRepository();
        }

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add Blazor frontend (from shmoxy.frontend)
        builder.Services.AddBlazorFrontend();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();
            EnsureSchemaCreated(dbContext);
        }

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseBlazorFrontendMiddleware();
        app.MapControllers();
        app.MapBlazorFrontend();

        app.MapGet("/api/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

        return app;
    }

    /// <summary>
    /// Ensures the database schema is fully created, including any tables added after
    /// the database was initially created. EnsureCreated() alone is a no-op when the
    /// database file already exists, so we also run the creation script with
    /// CREATE TABLE IF NOT EXISTS for existing databases.
    /// </summary>
    internal static void EnsureSchemaCreated(ProxiesDbContext dbContext)
    {
        var created = dbContext.Database.EnsureCreated();
        if (!created)
        {
            // Database already existed — create any tables/indexes added since initial creation.
            // GenerateCreateScript() produces the full DDL from the current EF Core model.
            // Adding IF NOT EXISTS makes it safe to run against an existing database:
            // SQLite skips objects that already exist.
            var script = dbContext.Database.GenerateCreateScript();
            var safeScript = Regex.Replace(
                script,
                @"CREATE\s+(UNIQUE\s+)?TABLE",
                "CREATE TABLE IF NOT EXISTS",
                RegexOptions.IgnoreCase);
            safeScript = Regex.Replace(
                safeScript,
                @"CREATE\s+(UNIQUE\s+)?INDEX",
                "CREATE $1INDEX IF NOT EXISTS",
                RegexOptions.IgnoreCase);
            dbContext.Database.ExecuteSqlRaw(safeScript);
        }
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

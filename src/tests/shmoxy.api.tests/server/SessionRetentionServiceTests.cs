using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using shmoxy.api.data;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class SessionRetentionServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public SessionRetentionServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ProxiesDbContext>(options =>
            options.UseInMemoryDatabase(dbName), ServiceLifetime.Singleton);
        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var db = _serviceProvider.GetRequiredService<ProxiesDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task CleanupExpiredSessions_DoesNothing_WhenRetentionDisabled()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        db.InspectionSessions.Add(new InspectionSession { Name = "old", CreatedAt = DateTime.UtcNow.AddDays(-60) });
        await db.SaveChangesAsync();

        var service = new SessionRetentionService(_scopeFactory, NullLogger<SessionRetentionService>.Instance);
        await service.CleanupExpiredSessionsAsync();

        Assert.Equal(1, await db.InspectionSessions.CountAsync());
    }

    [Fact]
    public async Task CleanupExpiredSessions_DeletesOldSessions_WhenMaxAgeSet()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        db.InspectionSessions.Add(new InspectionSession { Name = "old", CreatedAt = DateTime.UtcNow.AddDays(-60) });
        db.InspectionSessions.Add(new InspectionSession { Name = "recent", CreatedAt = DateTime.UtcNow.AddDays(-5) });
        await db.SaveChangesAsync();

        await SessionRetentionService.SaveRetentionPolicyAsync(db, new RetentionPolicyDto
        {
            Enabled = true,
            MaxAgeDays = 30
        });

        var service = new SessionRetentionService(_scopeFactory, NullLogger<SessionRetentionService>.Instance);
        await service.CleanupExpiredSessionsAsync();

        var remaining = await db.InspectionSessions.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("recent", remaining[0].Name);
    }

    [Fact]
    public async Task CleanupExpiredSessions_DeletesExcess_WhenMaxCountExceeded()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        for (int i = 0; i < 5; i++)
        {
            db.InspectionSessions.Add(new InspectionSession
            {
                Name = $"session-{i}",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            });
        }
        await db.SaveChangesAsync();

        await SessionRetentionService.SaveRetentionPolicyAsync(db, new RetentionPolicyDto
        {
            Enabled = true,
            MaxCount = 3
        });

        var service = new SessionRetentionService(_scopeFactory, NullLogger<SessionRetentionService>.Instance);
        await service.CleanupExpiredSessionsAsync();

        var remaining = await db.InspectionSessions.OrderBy(s => s.CreatedAt).ToListAsync();
        Assert.Equal(3, remaining.Count);
        // session-0 is newest (CreatedAt = now), session-4 is oldest (CreatedAt = now - 4 days)
        // Oldest 2 deleted (session-4, session-3), remaining ordered by CreatedAt ascending:
        Assert.Equal("session-2", remaining[0].Name);
    }

    [Fact]
    public async Task SaveAndLoad_RetentionPolicy_RoundTrips()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        var policy = new RetentionPolicyDto { Enabled = true, MaxAgeDays = 30, MaxCount = 100 };
        await SessionRetentionService.SaveRetentionPolicyAsync(db, policy);

        var loaded = await SessionRetentionService.LoadRetentionPolicyAsync(db);

        Assert.True(loaded.Enabled);
        Assert.Equal(30, loaded.MaxAgeDays);
        Assert.Equal(100, loaded.MaxCount);
    }

    [Fact]
    public async Task LoadRetentionPolicy_ReturnsDefaults_WhenNoSettings()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        var loaded = await SessionRetentionService.LoadRetentionPolicyAsync(db);

        Assert.False(loaded.Enabled);
        Assert.Null(loaded.MaxAgeDays);
        Assert.Null(loaded.MaxCount);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}

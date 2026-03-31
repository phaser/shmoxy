using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using shmoxy.api.data;
using shmoxy.api.models.dto;

namespace shmoxy.api.server;

public class SessionRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionRetentionService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    public SessionRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredSessionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during session retention cleanup");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    internal async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();

        var policy = await LoadRetentionPolicyAsync(db, cancellationToken);
        if (!policy.Enabled)
            return;

        var deleted = 0;

        // Age-based cleanup
        if (policy.MaxAgeDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-policy.MaxAgeDays.Value);
            var expired = await db.InspectionSessions
                .Where(s => s.CreatedAt < cutoff)
                .ToListAsync(cancellationToken);

            if (expired.Count > 0)
            {
                db.InspectionSessions.RemoveRange(expired);
                deleted += expired.Count;
                _logger.LogInformation("Retention: removing {Count} sessions older than {MaxAgeDays} days", expired.Count, policy.MaxAgeDays.Value);
            }
        }

        // Count-based cleanup
        if (policy.MaxCount.HasValue)
        {
            var totalCount = await db.InspectionSessions.CountAsync(cancellationToken);
            if (totalCount > policy.MaxCount.Value)
            {
                var excess = totalCount - policy.MaxCount.Value;
                var oldest = await db.InspectionSessions
                    .OrderBy(s => s.CreatedAt)
                    .Take(excess)
                    .ToListAsync(cancellationToken);

                db.InspectionSessions.RemoveRange(oldest);
                deleted += oldest.Count;
                _logger.LogInformation("Retention: removing {Count} oldest sessions (max count {MaxCount} exceeded)", oldest.Count, policy.MaxCount.Value);
            }
        }

        if (deleted > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<RetentionPolicyDto> LoadRetentionPolicyAsync(
        ProxiesDbContext db, CancellationToken cancellationToken = default)
    {
        var settings = await db.AppSettings
            .Where(s => s.Key.StartsWith("retention."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        return new RetentionPolicyDto
        {
            Enabled = settings.TryGetValue("retention.enabled", out var enabled) &&
                      bool.TryParse(enabled, out var e) && e,
            MaxAgeDays = settings.TryGetValue("retention.maxAgeDays", out var age) &&
                         int.TryParse(age, out var a) ? a : null,
            MaxCount = settings.TryGetValue("retention.maxCount", out var count) &&
                       int.TryParse(count, out var c) ? c : null
        };
    }

    internal static async Task SaveRetentionPolicyAsync(
        ProxiesDbContext db, RetentionPolicyDto policy, CancellationToken cancellationToken = default)
    {
        var pairs = new Dictionary<string, string>
        {
            ["retention.enabled"] = policy.Enabled.ToString(),
            ["retention.maxAgeDays"] = policy.MaxAgeDays?.ToString() ?? "",
            ["retention.maxCount"] = policy.MaxCount?.ToString() ?? ""
        };

        foreach (var (key, value) in pairs)
        {
            var existing = await db.AppSettings.FindAsync(new object[] { key }, cancellationToken);
            if (existing != null)
            {
                existing.Value = value;
            }
            else
            {
                db.AppSettings.Add(new models.AppSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

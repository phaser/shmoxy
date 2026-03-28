using Microsoft.EntityFrameworkCore;
using shmoxy.api.models;

namespace shmoxy.api.data;

public class ProxiesDbContext : DbContext
{
    public ProxiesDbContext(DbContextOptions<ProxiesDbContext> options)
        : base(options)
    {
    }

    public DbSet<RemoteProxy> RemoteProxies => Set<RemoteProxy>();
    public DbSet<InspectionSession> InspectionSessions => Set<InspectionSession>();
    public DbSet<InspectionSessionRow> InspectionSessionRows => Set<InspectionSessionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RemoteProxy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.AdminUrl).IsRequired().HasMaxLength(2048);
            entity.Property(e => e.ApiKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Status).IsRequired();
        });

        modelBuilder.Entity<InspectionSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.HasMany(e => e.Rows)
                .WithOne(r => r.Session)
                .HasForeignKey(r => r.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InspectionSessionRow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId);
            entity.Property(e => e.Method).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Url).IsRequired();
        });
    }
}

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
    }
}

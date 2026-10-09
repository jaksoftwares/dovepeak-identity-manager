using Dovepeak.Identity.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Persistence;

/// <summary>
/// The Dovepeak platform database: configuration and records owned by Dovepeak rather than the identity engine.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<AuditCheckpoint> AuditCheckpoints => Set<AuditCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Realm).HasMaxLength(64);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.Property(e => e.SourceEventId).HasMaxLength(128);
            entity.Property(e => e.Type).HasMaxLength(64);
            entity.Property(e => e.UserId).HasMaxLength(64);
            entity.Property(e => e.ClientId).HasMaxLength(255);
            entity.Property(e => e.SessionId).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.Error).HasMaxLength(128);
            entity.Property(e => e.Details).HasColumnType("jsonb");

            entity.HasIndex(e => new { e.Realm, e.Source, e.SourceEventId }).IsUnique();
            entity.HasIndex(e => new { e.Realm, e.OccurredAt });
            entity.HasIndex(e => new { e.Realm, e.UserId, e.OccurredAt });
        });

        modelBuilder.Entity<AuditCheckpoint>(entity =>
        {
            entity.HasKey(c => new { c.Realm, c.Source });
            entity.Property(c => c.Realm).HasMaxLength(64);
            entity.Property(c => c.Source).HasMaxLength(32);
        });
    }
}

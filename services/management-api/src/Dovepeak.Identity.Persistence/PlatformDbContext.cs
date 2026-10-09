using System.Linq.Expressions;
using Dovepeak.Identity.Persistence.Audit;
using Dovepeak.Identity.Persistence.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Persistence;

/// <summary>
/// The Dovepeak platform database: configuration and records owned by Dovepeak rather than the identity engine.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, TenantScope tenantScope)
    : DbContext(options), IDataProtectionKeyContext
{
    public TenantScope TenantScope { get; } = tenantScope;

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<AuditCheckpoint> AuditCheckpoints => Set<AuditCheckpoint>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectEnvironment> Environments => Set<ProjectEnvironment>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();

    public DbSet<EnvironmentScope> EnvironmentScopes => Set<EnvironmentScope>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<ManagementAuditEvent> ManagementAuditEvents => Set<ManagementAuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>ASP.NET Core Data Protection key ring (encrypts webhook signing secrets).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Evaluated per query by the global filters below.
    private Guid? CurrentOrganizationId => TenantScope.OrganizationId;

    private bool IsSystemScope => TenantScope.IsSystem;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureAudit(modelBuilder);
        ConfigureTenancy(modelBuilder);
        ConfigurePlatform(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    private static void ConfigureAudit(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<ManagementAuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActorType).HasMaxLength(16);
            entity.Property(e => e.ActorId).HasMaxLength(64);
            entity.Property(e => e.Action).HasMaxLength(64);
            entity.Property(e => e.ResourceType).HasMaxLength(64);
            entity.Property(e => e.ResourceId).HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.Details).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.OrganizationId, e.OccurredAt });
        });
    }

    private static void ConfigureTenancy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Slug).HasMaxLength(63);
            entity.Property(o => o.Name).HasMaxLength(200);
            entity.Property(o => o.CreatedBy).HasMaxLength(64);
            entity.HasIndex(o => o.Slug).IsUnique();
        });

        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.HasKey(m => new { m.OrganizationId, m.UserId });
            entity.Property(m => m.UserId).HasMaxLength(64);
            entity.Property(m => m.Email).HasMaxLength(320);
            entity.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(m => m.UserId);
            entity.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganizationInvitation>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Email).HasMaxLength(320);
            entity.Property(i => i.Role).HasConversion<string>().HasMaxLength(16);
            entity.Property(i => i.CreatedBy).HasMaxLength(64);
            entity.HasIndex(i => new { i.Email, i.AcceptedAt });
            entity.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Slug).HasMaxLength(63);
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.HasIndex(p => new { p.OrganizationId, p.Slug }).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectEnvironment>(entity =>
        {
            entity.ToTable("environments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.State).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.RealmName).HasMaxLength(63);
            entity.Property(e => e.Cluster).HasMaxLength(63);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasIndex(e => new { e.ProjectId, e.Kind }).IsUnique();
            entity.HasIndex(e => e.RealmName).IsUnique();
            entity.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).HasMaxLength(200);
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(a => a.ClientId).HasMaxLength(64);
            entity.Property(a => a.EngineClientId).HasMaxLength(64);
            entity.Property(a => a.Scopes).HasDefaultValueSql("'{}'::text[]");
            entity.HasIndex(a => a.ClientId).IsUnique();
            entity.HasIndex(a => new { a.EnvironmentId, a.Name }).IsUnique();
            entity.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(a => a.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EnvironmentScope>(entity =>
        {
            entity.ToTable("environment_scopes");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(64);
            entity.Property(s => s.Description).HasMaxLength(500);
            entity.Property(s => s.EngineScopeId).HasMaxLength(64);
            entity.HasIndex(s => new { s.EnvironmentId, s.Name }).IsUnique();
            entity.HasIndex(s => s.OrganizationId);
            entity.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(s => s.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).HasMaxLength(64);
            entity.Property(r => r.Description).HasMaxLength(500);
            entity.HasIndex(r => new { r.ApplicationId, r.Name }).IsUnique();
            entity.HasOne<Application>().WithMany().HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Name).HasMaxLength(200);
            entity.Property(k => k.Prefix).HasMaxLength(32);
            entity.Property(k => k.CreatedBy).HasMaxLength(64);
            entity.HasIndex(k => k.Digest).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(k => k.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePlatform(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasMaxLength(64);
            entity.Property(m => m.Payload).HasColumnType("jsonb");
            entity.Property(m => m.LastError).HasMaxLength(2000);
            entity.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<WebhookEndpoint>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Url).HasMaxLength(2048);
            entity.HasOne<Organization>().WithMany().HasForeignKey(w => w.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.EventType).HasMaxLength(64);
            entity.Property(d => d.Payload).HasColumnType("jsonb");
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(d => d.LastError).HasMaxLength(1000);
            entity.HasIndex(d => new { d.Status, d.NextAttemptAt });
            entity.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(d => d.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(r => new { r.Scope, r.Key });
            entity.Property(r => r.Scope).HasMaxLength(100);
            entity.Property(r => r.Key).HasMaxLength(255);
            entity.Property(r => r.RequestHash).HasMaxLength(64);
            entity.Property(r => r.ResponseBody).HasColumnType("jsonb");
        });
    }

    /// <summary>Adds <c>IsSystemScope || entity.OrganizationId == CurrentOrganizationId</c> to every tenant-owned entity.</summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var context = Expression.Constant(this);
            var organizationId = Expression.Convert(
                Expression.Property(parameter, nameof(ITenantOwned.OrganizationId)), typeof(Guid?));

            var body = Expression.OrElse(
                Expression.Property(context, nameof(IsSystemScope)),
                Expression.Equal(organizationId, Expression.Property(context, nameof(CurrentOrganizationId))));

            entityType.SetQueryFilter(Expression.Lambda(body, parameter));
        }

        modelBuilder.Entity<Organization>().HasQueryFilter(o => IsSystemScope || o.Id == CurrentOrganizationId);
    }
}

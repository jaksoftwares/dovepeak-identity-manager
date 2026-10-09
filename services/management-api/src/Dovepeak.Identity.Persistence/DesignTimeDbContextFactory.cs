using Dovepeak.Identity.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dovepeak.Identity.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to create migrations and migration bundles. The connection string is only used when
/// applying migrations; bundles accept <c>--connection</c> at run time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DOVEPEAK_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5442;Database=dovepeak;Username=dovepeak;Password=dovepeak_local_only";

        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, connectionString);
        var scope = new TenantScope();
        scope.EnterSystem();
        return new PlatformDbContext(options.Options, scope);
    }
}

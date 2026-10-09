using Dovepeak.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dovepeak.Identity.Platform.Common;

internal static class DbExtensions
{
    /// <summary>Saves changes, translating unique-constraint violations into a 409 conflict.</summary>
    public static async Task SaveOrConflictAsync(this PlatformDbContext db, CancellationToken ct, string code = "already_exists",
        string message = "A resource with the same identifier already exists.")
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw PlatformException.Conflict(code, message);
        }
    }
}

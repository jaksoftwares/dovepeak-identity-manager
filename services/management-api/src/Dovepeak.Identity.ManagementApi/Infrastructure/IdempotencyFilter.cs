using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dovepeak.Identity.ManagementApi.Infrastructure;

/// <summary>Marks responses that contain a one-time secret; replays omit those fields.</summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class SecretBearingResponseAttribute : Attribute;

/// <summary>
/// <c>Idempotency-Key</c> support for POST requests (milestone M3.9). The key is reserved before the request runs,
/// so concurrent duplicates cannot both execute. A successful response is stored and replayed for the same key and
/// request body; a different body with the same key is rejected. One-time secrets are never stored: replays of
/// secret-bearing responses omit them and set <c>Dovepeak-Secret-Omitted: true</c>.
/// </summary>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly string[] SecretFields = ["clientSecret", "key", "signingSecret"];

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return await next(context);
        }

        if (key.Length > 255)
        {
            throw PlatformException.Invalid(HeaderName, "Idempotency keys are at most 255 characters.");
        }

        var services = http.RequestServices;
        var scope = services.GetRequiredService<ICallerAccessor>().Caller.ScopeKey;
        var requestHash = await HashRequestAsync(http, context.Arguments);
        var jsonOptions = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        await using (var reservationScope = services.CreateAsyncScope())
        {
            var db = SystemDb(reservationScope.ServiceProvider);
            var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Scope == scope && r.Key == key, http.RequestAborted);
            if (existing is not null)
            {
                return Replay(existing, requestHash, http);
            }

            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Scope = scope,
                Key = key,
                RequestHash = requestHash,
                StatusCode = 0,
                ResponseBody = "null",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            try
            {
                await db.SaveChangesAsync(http.RequestAborted);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw PlatformException.Conflict("request_in_progress", "A request with this idempotency key is already in progress.");
            }
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await ReleaseAsync(services, scope, key);
            throw;
        }

        if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } statusResult && result is IValueHttpResult valueResult)
        {
            var body = JsonSerializer.SerializeToNode(valueResult.Value, jsonOptions) ?? JsonValue.Create((string?)null);
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<SecretBearingResponseAttribute>() is not null)
            {
                RemoveSecrets(body);
            }

            await using var storeScope = services.CreateAsyncScope();
            var db = SystemDb(storeScope.ServiceProvider);
            await db.IdempotencyRecords.Where(r => r.Scope == scope && r.Key == key).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.StatusCode, statusResult.StatusCode!.Value)
                .SetProperty(r => r.ResponseBody, body!.ToJsonString()), CancellationToken.None);
        }
        else
        {
            await ReleaseAsync(services, scope, key);
        }

        return result;
    }

    private static IResult Replay(IdempotencyRecord existing, string requestHash, HttpContext http)
    {
        if (existing.RequestHash != requestHash)
        {
            throw new PlatformException(PlatformErrorKind.Validation, "idempotency_key_reused",
                "This idempotency key was already used with a different request.");
        }

        if (existing.StatusCode == 0)
        {
            throw PlatformException.Conflict("request_in_progress", "A request with this idempotency key is already in progress.");
        }

        http.Response.Headers["Idempotent-Replayed"] = "true";
        if (http.GetEndpoint()?.Metadata.GetMetadata<SecretBearingResponseAttribute>() is not null)
        {
            http.Response.Headers["Dovepeak-Secret-Omitted"] = "true";
        }

        return Results.Content(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
    }

    private static async Task ReleaseAsync(IServiceProvider services, string scope, string key)
    {
        await using var releaseScope = services.CreateAsyncScope();
        var db = SystemDb(releaseScope.ServiceProvider);
        await db.IdempotencyRecords.Where(r => r.Scope == scope && r.Key == key && r.StatusCode == 0).ExecuteDeleteAsync(CancellationToken.None);
    }

    private static PlatformDbContext SystemDb(IServiceProvider services)
    {
        services.GetRequiredService<TenantScope>().EnterSystem();
        return services.GetRequiredService<PlatformDbContext>();
    }

    private static async Task<string> HashRequestAsync(HttpContext http, IList<object?> arguments)
    {
        var builder = new StringBuilder().Append(http.Request.Method).Append(' ').Append(http.Request.Path);
        foreach (var argument in arguments.Where(a => a is not null and not CancellationToken and not HttpContext))
        {
            builder.Append('|').Append(JsonSerializer.Serialize(argument));
        }

        await Task.CompletedTask;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void RemoveSecrets(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var field in SecretFields)
            {
                obj.Remove(field);
            }

            foreach (var (_, child) in obj.ToList())
            {
                RemoveSecrets(child);
            }
        }
    }
}

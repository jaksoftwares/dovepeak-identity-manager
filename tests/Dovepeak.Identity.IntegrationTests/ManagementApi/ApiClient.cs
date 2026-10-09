using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Thin JSON client for the Management API, as a developer or API key would use it.</summary>
public sealed class ApiClient(HttpClient http, string? userId, string? email) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string? UserId { get; } = userId;

    public string? Email { get; } = email;

    public HttpClient Http { get; } = http;

    public async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResponse(response.StatusCode, text, response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)));
    }

    public Task<ApiResponse> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public Task<ApiResponse> PostAsync(string path, object? body = null, string? idempotencyKey = null) => SendAsync(HttpMethod.Post, path, body ?? new { }, idempotencyKey);

    public Task<ApiResponse> PatchAsync(string path, object body) => SendAsync(HttpMethod.Patch, path, body);

    public Task<ApiResponse> PutAsync(string path) => SendAsync(HttpMethod.Put, path);

    public Task<ApiResponse> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    public void Dispose() => Http.Dispose();
}

public sealed record ApiResponse(HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public JsonNode Json => JsonNode.Parse(Body) ?? throw new InvalidOperationException("Empty body.");

    public string? Code => Body.Length > 0 && Body.TrimStart().StartsWith('{') ? Json["code"]?.GetValue<string>() : null;

    public ApiResponse Expect(HttpStatusCode status)
    {
        if (Status != status)
        {
            throw new Xunit.Sdk.XunitException($"Expected HTTP {(int)status} but got {(int)Status}: {Body}");
        }

        return this;
    }

    public T As<T>() => JsonSerializer.Deserialize<T>(Body, ApiClient.Json)!;
}

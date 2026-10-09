using System.Net;
using System.Text;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Dovepeak.Identity.ManagementApi.Tests.Keycloak;

public sealed class KeycloakAccessTokenProviderTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly ScriptedHandler _handler = new();
    private readonly KeycloakAccessTokenProvider _provider;

    public KeycloakAccessTokenProviderTests()
    {
        var options = Options.Create(new KeycloakOptions
        {
            BaseUrl = new Uri("http://keycloak.test/"),
            ClientId = "dovepeak-management",
            ClientSecret = "not-a-secret",
        });
        _provider = new KeycloakAccessTokenProvider(new SingleClientFactory(_handler), options, _time);
    }

    [Fact]
    public async Task GetToken_CachesUntilNearExpiry()
    {
        _handler.Tokens.Enqueue(("t1", 60));
        _handler.Tokens.Enqueue(("t2", 60));

        Assert.Equal("t1", await _provider.GetTokenAsync(CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(40));
        Assert.Equal("t1", await _provider.GetTokenAsync(CancellationToken.None));

        // 15-second safety margin: at 46s a 60s token is treated as expired.
        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal("t2", await _provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, _handler.Calls);
    }

    [Fact]
    public async Task Invalidate_ForcesNewToken()
    {
        _handler.Tokens.Enqueue(("t1", 300));
        _handler.Tokens.Enqueue(("t2", 300));

        await _provider.GetTokenAsync(CancellationToken.None);
        _provider.Invalidate();

        Assert.Equal("t2", await _provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TokenFetchedDuringInvalidation_IsDiscarded()
    {
        // Regression: a token requested before a realm was created (and so lacking rights over it) must not be
        // cached if the realm's creator invalidated the cache while that request was in flight.
        _handler.Tokens.Enqueue(("stale", 300));
        _handler.Tokens.Enqueue(("fresh", 300));
        _handler.OnFirstCall = _provider.Invalidate;

        Assert.Equal("fresh", await _provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal("fresh", await _provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, _handler.Calls);
    }

    [Fact]
    public async Task FailedTokenRequest_ThrowsWithoutResponseBody()
    {
        _handler.FailWith = HttpStatusCode.Unauthorized;

        var error = await Assert.ThrowsAsync<KeycloakAdminException>(() => _provider.GetTokenAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("not-a-secret", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public Queue<(string Token, int ExpiresIn)> Tokens { get; } = new();

        public Action? OnFirstCall { get; set; }

        public HttpStatusCode? FailWith { get; set; }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                OnFirstCall?.Invoke();
            }

            if (FailWith is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"error":"unauthorized_client"}""") });
            }

            var (token, expiresIn) = Tokens.Dequeue();
            var json = $$"""{"access_token":"{{token}}","expires_in":{{expiresIn}}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

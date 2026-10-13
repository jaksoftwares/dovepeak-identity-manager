using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dovepeak.Identity.MailRelay;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpServer.Authentication;
using SmtpServer.Storage;

namespace Dovepeak.Identity.MailRelay.Tests;

public sealed class RelayTests : IAsyncLifetime
{
    private const string Password = "relay-password-for-tests";
    private const string Realm = "dp-0123456789abcdef0123456789abcdef";

    private readonly FakeUpstream _upstream = new();
    private readonly FakeQuota _quota = new();
    private readonly int _port = FreePort();
    private ServiceProvider _services = null!;
    private SmtpListener _listener = null!;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(Options.Create(new RelayOptions { Port = _port, Password = Password }));
        collection.AddSingleton<IEmailQuota>(_quota);
        collection.AddSingleton<IUpstreamSender>(_upstream);
        collection.AddSingleton<IUserAuthenticator, RealmAuthenticator>();
        collection.AddSingleton<IMessageStore, RelayMessageStore>();
        collection.AddSingleton<SmtpListener>();
        _services = collection.BuildServiceProvider();
        _listener = _services.GetRequiredService<SmtpListener>();
        await _listener.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task AuthenticatedRealm_IsForwardedUpstream()
    {
        await SendAsync(Realm, Password, "user@example.test");

        var sent = Assert.Single(_upstream.Sent);
        Assert.Equal(["user@example.test"], sent.Recipients);
        Assert.Equal("Verify your email", sent.Subject);
        Assert.Equal((Realm, "user@example.test"), Assert.Single(_quota.Calls));
    }

    [Fact]
    public async Task RecipientOverQuota_IsDroppedButAccepted()
    {
        _quota.Decide = recipient => recipient == "victim@example.test" ? QuotaDecision.RecipientLimit : QuotaDecision.Allowed;

        // Accepted (no SMTP error): rejecting would reveal that the account exists and is being targeted.
        await SendAsync(Realm, Password, "victim@example.test", "other@example.test");

        var sent = Assert.Single(_upstream.Sent);
        Assert.Equal(["other@example.test"], sent.Recipients);
    }

    [Fact]
    public async Task EveryRecipientOverQuota_SendsNothing()
    {
        _quota.Decide = _ => QuotaDecision.TenantLimit;

        await SendAsync(Realm, Password, "user@example.test");

        Assert.Empty(_upstream.Sent);
    }

    [Fact]
    public async Task UpstreamFailure_IsATransientError()
    {
        _upstream.Fail = true;

        var error = await Assert.ThrowsAsync<SmtpCommandException>(() => SendAsync(Realm, Password, "user@example.test"));
        Assert.Equal(451, (int)error.StatusCode);

        // Quotas count email that went out: an outage must not use up the user's recovery emails.
        Assert.Equal((Realm, "user@example.test"), Assert.Single(_quota.Releases));
    }

    [Theory]
    [InlineData(Realm, "wrong-password-value")]
    [InlineData("master", Password)]
    [InlineData("Not A Realm", Password)]
    public async Task InvalidCredentials_AreRejected(string user, string password)
    {
        await Assert.ThrowsAsync<AuthenticationException>(() => SendAsync(user, password, "user@example.test"));
        Assert.Empty(_upstream.Sent);
    }

    [Fact]
    public async Task UnauthenticatedSender_CannotRelay()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(user: null, password: null, "user@example.test"));
        Assert.Empty(_upstream.Sent);
    }

    [Fact]
    public void RecipientKey_IsStable_CaseInsensitive_AndPerRealm()
    {
        var key = ValkeyEmailQuota.RecipientKey(Realm, "User@Example.test ");
        Assert.Equal(key, ValkeyEmailQuota.RecipientKey(Realm, "user@example.test"));
        Assert.NotEqual(key, ValkeyEmailQuota.RecipientKey("dp-ffffffffffffffffffffffffffffffff", "user@example.test"));
        Assert.DoesNotContain("example", key, StringComparison.Ordinal);
    }

    private async Task SendAsync(string? user, string? password, params string[] recipients)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("no-reply@dovepeak.test"));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = "Verify your email";
        message.Body = new TextPart("plain") { Text = "Click the link." };

        using var client = new SmtpClient();
        await client.ConnectAsync("127.0.0.1", _port, SecureSocketOptions.None);
        if (user is not null)
        {
            await client.AuthenticateAsync(user, password!);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private sealed class FakeQuota : IEmailQuota
    {
        public ConcurrentQueue<(string Realm, string Recipient)> Calls { get; } = new();

        public Func<string, QuotaDecision> Decide { get; set; } = _ => QuotaDecision.Allowed;

        public ConcurrentQueue<(string Realm, string Recipient)> Releases { get; } = new();

        public Task ReleaseAsync(string realm, string recipient, CancellationToken cancellationToken)
        {
            Releases.Enqueue((realm, recipient));
            return Task.CompletedTask;
        }

        public Task<QuotaDecision> ConsumeAsync(string realm, string recipient, CancellationToken cancellationToken)
        {
            Calls.Enqueue((realm, recipient));
            return Task.FromResult(Decide(recipient));
        }
    }

    private sealed class FakeUpstream : IUpstreamSender
    {
        public ConcurrentQueue<(string Subject, string[] Recipients)> Sent { get; } = new();

        public bool Fail { get; set; }

        public Task SendAsync(MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("provider down");
            }

            Sent.Enqueue((message.Subject ?? string.Empty, recipients.Select(r => r.Address).ToArray()));
            return Task.CompletedTask;
        }
    }
}

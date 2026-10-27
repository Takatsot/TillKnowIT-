using System.Collections.Concurrent;
using Identity.Contracts;
using Messaging.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;
using Wallet.Application;

namespace Wallet.Infrastructure;

public static partial class WalletModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IWalletStore, WalletStore>();
        services.AddScoped<PurchasePoster>();
        services.AddScoped<PurchaseAlerts>();
        services.AddScoped<IWalletNotifier, WalletNotifier>();
        services.AddSingleton<IWalletWebhookVerifier, ConfiguredWalletWebhookVerifier>();
        services.AddSingleton(ReadKeys);
        // Until a licensed partner is connected, anything that needs one says so plainly. Development swaps in a pretend partner.
        services.TryAddSingleton<IStoredValuePartner, UnconnectedStoredValuePartner>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Development only: a pretend payment partner that keeps its own books in memory.</summary>
    public static IServiceCollection AddDevStoredValuePartner(this IServiceCollection services)
    {
        services.RemoveAll<IStoredValuePartner>();
        return services.AddSingleton<DevStoredValuePartner>().AddSingleton<IStoredValuePartner>(p => p.GetRequiredService<DevStoredValuePartner>());
    }

    /// <summary>Daily reconciliation, scheduled payouts and pending erasures. Call this from the worker only.</summary>
    public static IServiceCollection AddWalletJobs(this IServiceCollection services) => services.AddHostedService<WalletJobsService>();

    private static WalletKeys ReadKeys(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var qr = config["Wallet:QrKey"];
        var allowance = config["Wallet:AllowanceKey"];
        // Two different keys, each long enough to be worth having: a leak of one must not expose the other.
        if (string.IsNullOrWhiteSpace(qr) || qr.Length < 32 || string.IsNullOrWhiteSpace(allowance) || allowance.Length < 32 || qr == allowance)
            throw new InvalidOperationException("Wallet:QrKey and Wallet:AllowanceKey must both be set, at least 32 characters each, and different from each other.");
        return new WalletKeys(qr, allowance);
    }
}

internal sealed class ConfiguredWalletWebhookVerifier(IConfiguration config) : IWalletWebhookVerifier
{
    public bool Verify(string partner, string? signatureHeader, string body, DateTimeOffset now)
    {
        var secret = config[$"Wallet:WebhookSecrets:{partner}"];
        return !string.IsNullOrWhiteSpace(secret) && WebhookSignature.Verify(secret, signatureHeader, body, now);
    }
}

/// <summary>Messages to parents by push (and WhatsApp when asked for) and to the tuckshop owner. A failure is only logged: money never waits on a message.</summary>
internal sealed class WalletNotifier(IPersonDirectory people, IShopDirectory shops, IMessageQueue queue, ILogger<WalletNotifier> log) : IWalletNotifier
{
    public async Task ToParent(Guid parentPersonId, string template, Dictionary<string, string> values, string dedupeKey, bool whatsapp, CancellationToken ct)
    {
        var person = await people.Get(parentPersonId, ct);
        if (person is null) return;
        await Send(new QueueMessageRequest(template, person.Language, "push", parentPersonId.ToString(), values, null, dedupeKey + ":push"), ct);
        if (whatsapp && !string.IsNullOrWhiteSpace(person.Phone))
            await Send(new QueueMessageRequest(template, person.Language, "whatsapp", person.Phone, values, null, dedupeKey + ":wa"), ct);
    }

    public async Task ToShopOwner(Guid shopId, string template, Dictionary<string, string> values, string dedupeKey, CancellationToken ct)
    {
        var shop = await shops.Get(shopId, ct);
        var (channel, destination) = shop switch
        {
            { OwnerPhone: { Length: > 0 } phone } => ("whatsapp", phone),
            { OwnerEmail: { Length: > 0 } email } => ("email", email),
            _ => ("", ""),
        };
        if (channel.Length == 0) return;
        values["shop"] = shop!.Name;
        await Send(new QueueMessageRequest(template, shop.OwnerLanguage, channel, destination, values, shopId, dedupeKey), ct);
    }

    private async Task Send(QueueMessageRequest request, CancellationToken ct)
    {
        var result = await queue.Queue(request, ct);
        if (!result.IsSuccess) log.LogWarning("Wallet message {Template} not queued: {Error}", request.TemplateCode, result.Error!.Message);
    }
}

internal sealed class UnconnectedStoredValuePartner : IStoredValuePartner
{
    private const string Message = "No payment partner is connected yet.";
    public Task<Result<TopUpCheckout>> CreateTopUpCheckout(TopUpCheckoutRequest request, CancellationToken ct) => Task.FromResult(Result<TopUpCheckout>.Fail("payment.partner_not_connected", Message));
    public Task<Result<string>> RefundToParent(Guid walletId, long amountCents, string reference, CancellationToken ct) => Task.FromResult(Result<string>.Fail("payment.partner_not_connected", Message));
    public Task<Result<string>> PayoutToShop(Guid shopId, long amountCents, string reference, CancellationToken ct) => Task.FromResult(Result<string>.Fail("payment.partner_not_connected", Message));
    public Task<long> HeldTotal(CancellationToken ct) => Task.FromResult(0L);
}

/// <summary>
/// Development only: stands in for the licensed partner. Complete(partnerRef) plays the parent paying on the hosted page. It tracks what it
/// holds from its own top-ups, refunds and payouts, so it will not agree with a ledger that also has cash deposits: it is for trying the flow.
/// </summary>
public sealed class DevStoredValuePartner : IStoredValuePartner
{
    private readonly ConcurrentDictionary<string, long> _pending = new();
    private readonly ConcurrentDictionary<string, string> _references = new();
    private long _held;

    public long Held => Interlocked.Read(ref _held);

    public Task<Result<TopUpCheckout>> CreateTopUpCheckout(TopUpCheckoutRequest request, CancellationToken ct)
    {
        var partnerRef = "dev_" + Guid.NewGuid().ToString("N")[..12];
        _pending[partnerRef] = request.AmountCents;
        return Task.FromResult(Result<TopUpCheckout>.Ok(new TopUpCheckout(partnerRef, $"https://dev-partner.invalid/pay/{partnerRef}")));
    }

    /// <summary>The parent pays on the hosted page; the amount is now held. The caller then sends the signed webhook.</summary>
    public long Complete(string partnerRef)
    {
        if (!_pending.TryRemove(partnerRef, out var cents)) throw new KeyNotFoundException(partnerRef);
        Interlocked.Add(ref _held, cents);
        return cents;
    }

    public Task<Result<string>> RefundToParent(Guid walletId, long amountCents, string reference, CancellationToken ct) => Move(amountCents, reference, "dr_");
    public Task<Result<string>> PayoutToShop(Guid shopId, long amountCents, string reference, CancellationToken ct) => Move(amountCents, reference, "dp_");

    private Task<Result<string>> Move(long cents, string reference, string prefix)
    {
        // The reference is an idempotency key: the same reference twice moves the money once.
        if (_references.TryGetValue(reference, out var done)) return Task.FromResult(Result<string>.Ok(done));
        Interlocked.Add(ref _held, -cents);
        return Task.FromResult(Result<string>.Ok(_references.GetOrAdd(reference, prefix + Guid.NewGuid().ToString("N")[..12])));
    }

    public Task<long> HeldTotal(CancellationToken ct) => Task.FromResult(Held);
}

/// <summary>Every ten minutes: reconcile the day once, then pay out what is due, then finish any erasures that were waiting on a refund.</summary>
public sealed class WalletJobsService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WalletJobsService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                var store = scope.ServiceProvider.GetRequiredService<IWalletStore>();
                var today = BusinessDay.Of(clock.GetUtcNow());

                // Reconcile first: a difference holds the payouts that follow.
                if (!await store.HasReconciliation(today, stoppingToken)) await dispatcher.Send(new RunWalletReconciliation(today, null), stoppingToken);

                var payouts = await dispatcher.Send(new RunPayouts(today, null), stoppingToken);
                if (payouts is { IsSuccess: true, Value.Blocked: true }) log.LogWarning("{Reason}", payouts.Value.BlockedReason);
                else if (payouts is { IsSuccess: true, Value: { } p } && p.Payouts.Count > 0) log.LogInformation("Paid out {Count} tuckshops", p.Payouts.Count);

                await dispatcher.Send(new ProcessPendingErasures(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Wallet jobs failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

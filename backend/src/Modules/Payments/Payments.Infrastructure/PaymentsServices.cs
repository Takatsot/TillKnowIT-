using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Application;
using TillKnowIT.Security;
using Payments.Domain;
using Payments.Infrastructure.Persistence;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Infrastructure;

public static partial class PaymentsModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddScoped<IDisputeStore, DisputeStore>();
        services.AddScoped<PaymentSettlement>();
        services.AddSingleton<IWebhookVerifier, ConfiguredWebhookVerifier>();
        // Until a real partner is connected, paying in the app says so plainly. Development swaps in a pretend one.
        services.TryAddSingleton<IPaymentPartner, UnconnectedPaymentPartner>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Releases held money 72 hours after delivery. Call this from the worker only.</summary>
    public static IServiceCollection AddPaymentRelease(this IServiceCollection services) => services.AddHostedService<PaymentReleaseService>();

    /// <summary>Development only: a pretend payment partner that holds, releases and refunds in memory.</summary>
    public static IServiceCollection AddDevPaymentPartner(this IServiceCollection services)
    {
        services.RemoveAll<IPaymentPartner>();
        return services.AddSingleton<DevPaymentPartner>().AddSingleton<IPaymentPartner>(p => p.GetRequiredService<DevPaymentPartner>());
    }
}

internal sealed class PaymentStore(PaymentsDbContext db) : IPaymentStore
{
    public async Task Add(Payment payment, CancellationToken ct) => await db.Set<Payment>().AddAsync(payment, ct);

    public Task<Payment?> Get(Guid paymentId, CancellationToken ct) => Include().FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public Task<Payment?> ForOrder(Guid orderId, CancellationToken ct) =>
        Include().Where(p => p.OrderId == orderId && p.Status != PaymentStatus.Failed).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<Payment?> ByPartnerRef(string partnerRef, CancellationToken ct) => Include().FirstOrDefaultAsync(p => p.PartnerRef == partnerRef, ct);

    public Task<List<Payment>> DueForRelease(DateTimeOffset now, int take, CancellationToken ct) =>
        Include().Where(p => p.Status == PaymentStatus.Held && !p.Frozen && p.ReleaseDueAt != null && p.ReleaseDueAt <= now).OrderBy(p => p.ReleaseDueAt).Take(take).ToListAsync(ct);

    public Task<List<Payment>> ChangedBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        db.Set<Payment>().AsNoTracking().Where(p => (p.UpdatedAt ?? p.CreatedAt) >= from && (p.UpdatedAt ?? p.CreatedAt) < to).ToListAsync(ct);

    public Task<bool> HasReceipt(string partner, string eventId, CancellationToken ct) =>
        db.Set<WebhookReceipt>().AnyAsync(r => r.Partner == partner && r.EventId == eventId, ct);

    public async Task Add(WebhookReceipt receipt, CancellationToken ct) => await db.Set<WebhookReceipt>().AddAsync(receipt, ct);
    public async Task Add(ReconciliationRun run, CancellationToken ct) => await db.Set<ReconciliationRun>().AddAsync(run, ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    private IQueryable<Payment> Include() => db.Set<Payment>().Include(p => p.Audit);
}

internal sealed class DisputeStore(PaymentsDbContext db) : IDisputeStore
{
    public async Task Add(Dispute dispute, CancellationToken ct) => await db.Set<Dispute>().AddAsync(dispute, ct);
    public Task<Dispute?> Get(Guid disputeId, CancellationToken ct) => db.Set<Dispute>().FirstOrDefaultAsync(d => d.Id == disputeId, ct);
    public Task<Dispute?> OpenForOrder(Guid orderId, CancellationToken ct) => db.Set<Dispute>().FirstOrDefaultAsync(d => d.OrderId == orderId && d.Status == DisputeStatus.Open, ct);
    public Task<List<Dispute>> Open(CancellationToken ct) => db.Set<Dispute>().AsNoTracking().Where(d => d.Status == DisputeStatus.Open).OrderBy(d => d.CreatedAt).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>Reads each partner's shared secret from configuration (Payments:WebhookSecrets:{partner}). No secret, no callbacks accepted.</summary>
internal sealed class ConfiguredWebhookVerifier(IConfiguration config) : IWebhookVerifier
{
    public bool Verify(string partner, string? signatureHeader, string body, DateTimeOffset now)
    {
        var secret = config[$"Payments:WebhookSecrets:{partner}"];
        return !string.IsNullOrWhiteSpace(secret) && WebhookSignature.Verify(secret, signatureHeader, body, now);
    }
}

internal sealed class UnconnectedPaymentPartner : IPaymentPartner
{
    private const string Message = "No payment partner is connected yet. Choose pay on delivery.";
    public Task<Result<CheckoutSession>> CreateCheckout(CheckoutRequest request, CancellationToken ct) => Task.FromResult(Result<CheckoutSession>.Fail("payment.partner_not_connected", Message));
    public Task<Result> Release(string partnerRef, long amountCents, CancellationToken ct) => Task.FromResult(Result.Fail("payment.partner_not_connected", Message));
    public Task<Result> Refund(string partnerRef, long amountCents, CancellationToken ct) => Task.FromResult(Result.Fail("payment.partner_not_connected", Message));
    public Task<List<PartnerTransaction>> Statement(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(new List<PartnerTransaction>());
}

/// <summary>
/// Development only: stands in for the partner so the whole flow can be tried. Money lives in memory and a checkout "link" is just
/// a reference; Complete(partnerRef) plays the part of the customer paying. Never registered outside Development.
/// </summary>
public sealed class DevPaymentPartner : IPaymentPartner
{
    private sealed record Row(string PartnerRef, long AmountCents, string State, long Released, long Refunded);
    private readonly ConcurrentDictionary<string, Row> _rows = new();

    public Task<Result<CheckoutSession>> CreateCheckout(CheckoutRequest request, CancellationToken ct)
    {
        var partnerRef = "dev_" + Guid.NewGuid().ToString("N")[..12];
        _rows[partnerRef] = new Row(partnerRef, request.AmountCents, "pending", 0, 0);
        return Task.FromResult(Result<CheckoutSession>.Ok(new CheckoutSession(partnerRef, $"https://dev-partner.invalid/pay/{partnerRef}")));
    }

    /// <summary>The customer pays on the hosted page.</summary>
    public void Complete(string partnerRef) => _rows.AddOrUpdate(partnerRef, _ => throw new KeyNotFoundException(partnerRef), (_, r) => r with { State = "held" });

    public Task<Result> Release(string partnerRef, long amountCents, CancellationToken ct) => Move(partnerRef, amountCents, release: true);
    public Task<Result> Refund(string partnerRef, long amountCents, CancellationToken ct) => Move(partnerRef, amountCents, release: false);

    private Task<Result> Move(string partnerRef, long cents, bool release)
    {
        if (!_rows.TryGetValue(partnerRef, out var r)) return Task.FromResult(Result.Fail("payment.partner_unknown", "The partner has no such payment."));
        if (r.Released + r.Refunded + cents > r.AmountCents) return Task.FromResult(Result.Fail("payment.partner_insufficient", "The partner holds less than that."));
        var next = release ? r with { Released = r.Released + cents } : r with { Refunded = r.Refunded + cents };
        _rows[partnerRef] = next with { State = next.Released > 0 ? "released" : next.Refunded > 0 ? "refunded" : next.State };
        return Task.FromResult(Result.Ok());
    }

    public Task<List<PartnerTransaction>> Statement(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(_rows.Values.Select(r => new PartnerTransaction(r.PartnerRef, r.AmountCents, r.State, r.Released, r.Refunded)).ToList());
}

/// <summary>Every minute, pays out held money whose 72 hours are up and that nobody disputed.</summary>
public sealed class PaymentReleaseService(IServiceScopeFactory scopes, ILogger<PaymentReleaseService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ReleaseDuePayments(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) log.LogInformation("Released {Count} payments to suppliers", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Releasing due payments failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

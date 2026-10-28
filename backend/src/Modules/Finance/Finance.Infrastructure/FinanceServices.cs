using System.Text.Json;
using Finance.Application;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TillKnowIT.Dispatching;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;
using Finance.Contracts;

namespace Finance.Infrastructure;

public static partial class FinanceModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IFinanceStore, FinanceStore>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Development only: a pretend finance partner. Real partners are registered here once an agreement and a sandbox exist.</summary>
    public static IServiceCollection AddDevFinancePartner(this IServiceCollection services)
    {
        services.AddSingleton<IFinancePartner>(sp => new DevFinancePartner(sp.GetRequiredService<IConfiguration>()["Finance:DevWebhookSecret"] ?? "dev-finance-secret", sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}

/// <summary>
/// A pretend lender for development: approves up to half of the sales it was shown, declines when there is too little history, and charges 5% for 30 days.
/// It is not a model of any real partner's rules.
/// </summary>
public sealed class DevFinancePartner(string secret, TimeProvider clock) : IFinancePartner
{
    public string Code => "dev";

    public Task<Result<PartnerDecision>> Apply(PartnerApplication app, CancellationToken ct)
    {
        var total = app.SalesHistory.Sum(m => m.SalesCents);
        var limit = Math.Min(app.RequestedCents, total / 2);
        return Task.FromResult(Result<PartnerDecision>.Ok(limit < 10_000
            ? new PartnerDecision(false, $"dev-app-{app.ApplicationId:N}", 0, 0, "Not enough sales history yet.")
            : new PartnerDecision(true, $"dev-app-{app.ApplicationId:N}", limit, 30, null)));
    }

    public Task<Result<PartnerDisbursement>> Disburse(string applicationReference, Guid orderId, string supplierName, long principalCents, CancellationToken ct) =>
        Task.FromResult(Result<PartnerDisbursement>.Ok(new($"dev-draw-{orderId:N}", (principalCents * 105 + 50) / 100, DateOnly.FromDateTime(clock.GetUtcNow().AddDays(30).UtcDateTime))));

    public bool VerifyCallback(string? signature, string body, DateTimeOffset now) => WebhookSignature.Verify(secret, signature, body, now);

    public Result<PartnerRepayment> ParseRepayment(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return Result<PartnerRepayment>.Ok(new(root.GetProperty("reference").GetString()!, root.GetProperty("drawReference").GetString()!, root.GetProperty("amountCents").GetInt64(), root.GetProperty("paidAt").GetDateTimeOffset()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result<PartnerRepayment>.Fail("webhook.body_invalid", "The callback could not be read.");
        }
    }
}

internal sealed class FinanceStore(FinanceDbContext db) : IFinanceStore
{
    public void Add(CreditConsent consent) => db.Set<CreditConsent>().Add(consent);
    public Task<CreditConsent?> Consent(Guid shopId, Guid id, CancellationToken ct) => db.Set<CreditConsent>().FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == id, ct);
    public Task<CreditConsent?> ActiveConsent(Guid shopId, string partnerCode, CancellationToken ct) => db.Set<CreditConsent>().FirstOrDefaultAsync(c => c.ShopId == shopId && c.PartnerCode == partnerCode && c.WithdrawnAt == null, ct);
    public Task<List<CreditConsent>> Consents(Guid shopId, CancellationToken ct) => db.Set<CreditConsent>().AsNoTracking().Where(c => c.ShopId == shopId).ToListAsync(ct);
    public void Add(DataShareRecord record) => db.Set<DataShareRecord>().Add(record);
    public Task<List<DataShareRecord>> Shares(Guid shopId, int take, CancellationToken ct) => db.Set<DataShareRecord>().AsNoTracking().Where(s => s.ShopId == shopId).OrderByDescending(s => s.CreatedAt).Take(take).ToListAsync(ct);

    public void Add(CreditApplication application) => db.Set<CreditApplication>().Add(application);
    public Task<CreditApplication?> Application(Guid id, CancellationToken ct) => db.Set<CreditApplication>().FirstOrDefaultAsync(a => a.Id == id, ct);
    public Task<List<CreditApplication>> Applications(Guid shopId, CancellationToken ct) => db.Set<CreditApplication>().AsNoTracking().Where(a => a.ShopId == shopId).ToListAsync(ct);

    public void Add(CreditDraw draw) => db.Set<CreditDraw>().Add(draw);
    public Task<CreditDraw?> DrawForOrder(Guid orderId, CancellationToken ct) => db.Set<CreditDraw>().FirstOrDefaultAsync(d => d.OrderId == orderId, ct);
    public Task<CreditDraw?> DrawByReference(string partnerCode, string reference, CancellationToken ct) =>
        db.Set<CreditDraw>().FirstOrDefaultAsync(d => d.PartnerReference == reference && db.Set<CreditApplication>().Any(a => a.Id == d.ApplicationId && a.PartnerCode == partnerCode), ct);
    public Task<List<CreditDraw>> Draws(Guid shopId, CancellationToken ct) => db.Set<CreditDraw>().AsNoTracking().Where(d => d.ShopId == shopId).ToListAsync(ct);
    public Task<List<CreditDraw>> DrawsOf(Guid applicationId, CancellationToken ct) => db.Set<CreditDraw>().AsNoTracking().Where(d => d.ApplicationId == applicationId).ToListAsync(ct);

    public void Add(CreditRepayment repayment) => db.Set<CreditRepayment>().Add(repayment);
    public Task<bool> RepaymentExists(string reference, CancellationToken ct) => db.Set<CreditRepayment>().AnyAsync(r => r.Reference == reference, ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public static class FinanceEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/shops/{shopId:guid}/credit/consents", async (Guid shopId, GrantConsentRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new GrantConsent(shopId, req), ct)));
        api.MapDelete("/shops/{shopId:guid}/credit/consents/{id:guid}", async (Guid shopId, Guid id, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new WithdrawConsent(shopId, id, personId), ct)));
        api.MapPost("/shops/{shopId:guid}/credit/applications", async (Guid shopId, ApplyRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ApplyForCredit(shopId, req), ct)));
        api.MapPost("/shops/{shopId:guid}/credit/draws", async (Guid shopId, DrawRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new DrawOnCredit(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/credit", async (Guid shopId, Guid personId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new GetCreditSummary(shopId, personId), ct)));
        api.MapPost("/credit/callbacks/{partner}", async (string partner, HttpRequest request, IDispatcher d, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var result = await d.Send(new HandleFinanceCallback(partner, request.Headers["X-Signature"].ToString(), await reader.ReadToEndAsync(ct)), ct);
            return result.IsSuccess ? Results.NoContent() : Problem(result.Error!);
        });
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" or "plan.feature_not_included" => 403,
            "webhook.signature_invalid" => 401,
            "partner.unavailable" => 502,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "consent.already_withdrawn" or "consent.withdrawn" or "credit.limit_exceeded" or "order.not_owing" => 409,
            _ => 422,
        });
}

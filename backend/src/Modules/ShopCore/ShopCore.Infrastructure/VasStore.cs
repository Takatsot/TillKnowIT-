using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopCore.Application.Vas;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Dispatching;

namespace ShopCore.Infrastructure;

internal sealed class VasStore(ShopCoreDbContext db) : IVasStore
{
    public Task<VasSale?> Get(Guid id, CancellationToken ct) => db.Set<VasSale>().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task Add(VasSale sale, CancellationToken ct) => await db.Set<VasSale>().AddAsync(sale, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal static class VasEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/vas/sales", async (VasSaleRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new SellVas(req), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error!.Message, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code },
                    statusCode: result.Error.Code switch
                    {
                        "vas.unavailable" => 503,
                        "vas.id_taken" => 409,
                        "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "permission.denied" => 403,
                        _ => 422,
                    });
        });
}

internal sealed class DevVasPartner : IVasPartner
{
    public Task<VasPartnerResult> Purchase(VasPurchase p, CancellationToken ct) => Task.FromResult(new VasPartnerResult(
        VasOutcome.Success, $"dev-{p.Id:N}", p.Type == VasProductType.Electricity ? "1234 5678 9012 3456 7890" : null));
}

public static class VasHostingExtensions
{
    /// <summary>Development only: a fake partner. Elsewhere VAS reports "not available" until the real partner adapter is registered.</summary>
    public static IServiceCollection AddDevVasPartner(this IServiceCollection services) => services.AddSingleton<IVasPartner, DevVasPartner>();
}

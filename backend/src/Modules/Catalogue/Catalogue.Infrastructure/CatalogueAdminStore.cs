using Catalogue.Application.Admin;
using Catalogue.Application.Products;
using Catalogue.Contracts;
using Catalogue.Domain;
using Catalogue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Dispatching;

namespace Catalogue.Infrastructure;

internal sealed class CatalogueAdminStore(CatalogueDbContext db) : ICatalogueAdminStore
{
    public Task<List<CatalogueProduct>> Suggestions(CancellationToken ct) =>
        db.Products.AsNoTracking().Where(p => p.Status == CatalogueProductStatus.Suggested).ToListAsync(ct);

    public Task<CatalogueProduct?> GetProduct(Guid id, CancellationToken ct) => db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Lookup?> GetLookup(string type, string code, CancellationToken ct) =>
        db.Set<Lookup>().FirstOrDefaultAsync(l => l.Type == type && l.Code == code, ct);

    public Task<List<Lookup>> Lookups(string type, CancellationToken ct) => db.Set<Lookup>().AsNoTracking().Where(l => l.Type == type).ToListAsync(ct);

    public async Task Add(Lookup lookup, CancellationToken ct) => await db.Set<Lookup>().AddAsync(lookup, ct);

    public async Task Add(AuditLogEntry entry, CancellationToken ct) => await db.Set<AuditLogEntry>().AddAsync(entry, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal static class CatalogueAdminEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        // TODO(auth): adminPersonId should come from the signed-in person.
        api.MapGet("/admin/catalogue/suggestions", async (Guid adminPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListSuggestions(adminPersonId), ct)));

        api.MapPost("/admin/catalogue/suggestions/{id:guid}/approve", async (Guid id, ApproveSuggestionRequest r, IDispatcher d, CancellationToken ct) =>
            Done(await d.Send(new ApproveSuggestion(id, r), ct)));

        api.MapPost("/admin/catalogue/suggestions/{id:guid}/reject", async (Guid id, RejectSuggestionRequest r, IDispatcher d, CancellationToken ct) =>
            Done(await d.Send(new RejectSuggestion(id, r), ct)));

        api.MapPost("/admin/catalogue/suggestions/{id:guid}/merge", async (Guid id, MergeSuggestionRequest r, IDispatcher d, CancellationToken ct) =>
            Done(await d.Send(new MergeSuggestion(id, r), ct)));

        api.MapGet("/lookups/{type}", async (string type, bool? includeInactive, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new GetLookups(type, includeInactive ?? false), ct)));

        api.MapPut("/admin/lookups/{type}/{code}", async (string type, string code, SaveLookupRequest r, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Send(new SaveLookup(type, code, r), ct)));
    }

    private static IResult Reply<T>(TillKnowIT.SharedKernel.Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Done(TillKnowIT.SharedKernel.Result<TillKnowIT.Dispatching.Unit> result) => result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(TillKnowIT.SharedKernel.Error error) => Results.Problem(title: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" => 403,
            "catalogue.not_found" => 404,
            "catalogue.already_reviewed" => 409,
            _ => 422,
        });
}

using Catalogue.Contracts;
using Catalogue.Domain;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Catalogue.Application.Admin;

public interface ICatalogueAdminStore
{
    Task<List<CatalogueProduct>> Suggestions(CancellationToken ct);
    Task<CatalogueProduct?> GetProduct(Guid id, CancellationToken ct);
    Task<Lookup?> GetLookup(string type, string code, CancellationToken ct);
    Task<List<Lookup>> Lookups(string type, CancellationToken ct);
    Task Add(Lookup lookup, CancellationToken ct);
    Task Add(AuditLogEntry entry, CancellationToken ct);
    Task Save(CancellationToken ct);
}

// ---- E2-3: the review queue ----

/// <summary>The queue of products shops have suggested, oldest first.</summary>
public sealed record ListSuggestions(Guid AdminPersonId) : IQuery<List<SuggestionResponse>>;

internal sealed class ListSuggestionsHandler(ICatalogueAdminStore store, IPermissionChecker permissions)
    : IQueryHandler<ListSuggestions, List<SuggestionResponse>>
{
    public async Task<Result<List<SuggestionResponse>>> Handle(ListSuggestions q, CancellationToken ct)
    {
        if (!await permissions.Has(q.AdminPersonId, "catalogue.approve", ct))
            return Result<List<SuggestionResponse>>.Fail("permission.denied", "Only a platform admin can review suggestions.");
        return Result<List<SuggestionResponse>>.Ok((await store.Suggestions(ct)).OrderBy(p => p.CreatedAt)
            .Select(p => new SuggestionResponse(p.Id, p.Barcode, p.Name, p.Size, p.CategoryCode, p.CreatedAt)).ToList());
    }
}

public sealed record ApproveSuggestion(Guid SuggestionId, ApproveSuggestionRequest Request) : ICommand<Unit>;

public sealed record RejectSuggestion(Guid SuggestionId, RejectSuggestionRequest Request) : ICommand<Unit>;

public sealed record MergeSuggestion(Guid SuggestionId, MergeSuggestionRequest Request) : ICommand<Unit>;

/// <summary>Approve, reject and merge share the same checks: the reviewer's permission and an existing suggestion. Every decision is audited.</summary>
internal sealed class ReviewHandlers(ICatalogueAdminStore store, IPermissionChecker permissions, ILookupChecker lookups, TimeProvider clock)
    : ICommandHandler<ApproveSuggestion, Unit>, ICommandHandler<RejectSuggestion, Unit>, ICommandHandler<MergeSuggestion, Unit>
{
    public async Task<Result<Unit>> Handle(ApproveSuggestion c, CancellationToken ct)
    {
        var r = c.Request;
        if (r.CategoryCode is not null && !await lookups.IsActive("categories", r.CategoryCode, ct))
            return Result<Unit>.Fail("catalogue.category_invalid", "Unknown category.");
        return await Review(c.SuggestionId, r.AdminPersonId, "approved", (p, now) => p.Approve(r.AdminPersonId, now, r.Name, r.Size, r.CategoryCode), ct);
    }

    public Task<Result<Unit>> Handle(RejectSuggestion c, CancellationToken ct) =>
        Review(c.SuggestionId, c.Request.AdminPersonId, "rejected", (p, now) => p.Reject(c.Request.AdminPersonId, now, c.Request.Reason), ct, c.Request.Reason);

    public async Task<Result<Unit>> Handle(MergeSuggestion c, CancellationToken ct)
    {
        var target = await store.GetProduct(c.Request.IntoProductId, ct);
        if (target is null) return Result<Unit>.Fail("catalogue.not_found", "The product to merge into was not found.");
        return await Review(c.SuggestionId, c.Request.AdminPersonId, "merged", (p, now) => p.MergeInto(target, c.Request.AdminPersonId, now), ct, $"into {target.Id}");
    }

    private async Task<Result<Unit>> Review(Guid id, Guid admin, string action, Func<CatalogueProduct, DateTimeOffset, Result> apply, CancellationToken ct, string? detail = null)
    {
        if (!await permissions.Has(admin, "catalogue.approve", ct))
            return Result<Unit>.Fail("permission.denied", "Only a platform admin can review suggestions.");
        var product = await store.GetProduct(id, ct);
        if (product is null) return Result<Unit>.Fail("catalogue.not_found", "Suggestion not found.");

        var now = clock.GetUtcNow();
        var done = apply(product, now);
        if (!done.IsSuccess) return Result<Unit>.Fail(done.Error!.Code, done.Error.Message);

        await store.Add(new AuditLogEntry("catalogue_product", product.Id, action, System.Text.Json.JsonSerializer.Serialize(new { by = admin, detail })), ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

// ---- lookups ----

/// <summary>Public: the till downloads these, and every screen uses them. Inactive entries are left out unless asked for.</summary>
public sealed record GetLookups(string Type, bool IncludeInactive = false) : IQuery<List<LookupResponse>>;

internal sealed class GetLookupsHandler(ICatalogueAdminStore store) : IQueryHandler<GetLookups, List<LookupResponse>>
{
    public async Task<Result<List<LookupResponse>>> Handle(GetLookups q, CancellationToken ct)
    {
        if (!Lookup.Types.Contains(q.Type))
            return Result<List<LookupResponse>>.Fail("lookup.type_invalid", $"Lookup types are: {string.Join(", ", Lookup.Types)}.");
        return Result<List<LookupResponse>>.Ok((await store.Lookups(q.Type, ct))
            .Where(l => q.IncludeInactive || l.IsActive).OrderBy(l => l.SortOrder).ThenBy(l => l.Code, StringComparer.Ordinal)
            .Select(l => new LookupResponse(l.Type, l.Code, l.Names, l.IsActive, l.SortOrder)).ToList());
    }
}

/// <summary>Create a lookup, or change its translations, order or whether it is switched on. Nothing is ever deleted, so old records keep their meaning.</summary>
public sealed record SaveLookup(string Type, string Code, SaveLookupRequest Request) : ICommand<LookupResponse>;

internal sealed class SaveLookupHandler(ICatalogueAdminStore store, IPermissionChecker permissions) : ICommandHandler<SaveLookup, LookupResponse>
{
    public async Task<Result<LookupResponse>> Handle(SaveLookup c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await permissions.Has(r.AdminPersonId, "lookup.manage", ct))
            return Result<LookupResponse>.Fail("permission.denied", "Only a platform admin can change lookups.");

        var lookup = await store.GetLookup(c.Type, c.Code, ct);
        var created = lookup is null;
        if (created)
        {
            var made = Lookup.Create(c.Type, c.Code, r.Names ?? [], r.SortOrder ?? 0);
            if (!made.IsSuccess) return Result<LookupResponse>.Fail(made.Error!.Code, made.Error.Message);
            lookup = made.Value!;
            if (r.IsActive is false) lookup.SetActive(false);
            await store.Add(lookup, ct);
        }
        else
        {
            if (r.Names is { Count: > 0 })
            {
                var translated = lookup!.Translate(r.Names);
                if (!translated.IsSuccess) return Result<LookupResponse>.Fail(translated.Error!.Code, translated.Error.Message);
            }
            if (r.IsActive is { } active) lookup!.SetActive(active);
            if (r.SortOrder is { } order) lookup!.SetOrder(order);
        }

        await store.Add(new AuditLogEntry("lookup", lookup!.Id, created ? "created" : "changed",
            System.Text.Json.JsonSerializer.Serialize(new { by = r.AdminPersonId, lookup.Type, lookup.Code, lookup.IsActive })), ct);
        await store.Save(ct);
        return Result<LookupResponse>.Ok(new LookupResponse(lookup.Type, lookup.Code, lookup.Names, lookup.IsActive, lookup.SortOrder));
    }
}

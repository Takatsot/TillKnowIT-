using Catalogue.Application.Admin;
using Catalogue.Contracts;
using Catalogue.Domain;
using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Catalogue.Application.Tests;

public class CatalogueAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : ICatalogueAdminStore
    {
        public List<CatalogueProduct> Products { get; } = [];
        public List<Lookup> LookupRows { get; } = [];
        public List<AuditLogEntry> Audit { get; } = [];
        public Task<List<CatalogueProduct>> Suggestions(CancellationToken ct) => Task.FromResult(Products.Where(p => p.Status == CatalogueProductStatus.Suggested).ToList());
        public Task<CatalogueProduct?> GetProduct(Guid id, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        public Task<Lookup?> GetLookup(string type, string code, CancellationToken ct) => Task.FromResult(LookupRows.FirstOrDefault(l => l.Type == type && l.Code == code));
        public Task<List<Lookup>> Lookups(string type, CancellationToken ct) => Task.FromResult(LookupRows.Where(l => l.Type == type).ToList());
        public Task Add(Lookup lookup, CancellationToken ct) { LookupRows.Add(lookup); return Task.CompletedTask; }
        public Task Add(AuditLogEntry entry, CancellationToken ct) { Audit.Add(entry); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePermissions : IPermissionChecker
    {
        public HashSet<(Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid personId, string permission, CancellationToken ct) => Task.FromResult(Granted.Contains((personId, permission)));
    }

    private sealed class FakeLookups : ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(type == "categories" && code is "drinks" or "groceries");
    }

    private readonly FakeStore _store = new();
    private readonly FakePermissions _permissions = new();
    private readonly Guid _admin = Guid.NewGuid();

    public CatalogueAdminTests()
    {
        _permissions.Granted.Add((_admin, "catalogue.approve"));
        _permissions.Granted.Add((_admin, "lookup.manage"));
    }

    private ReviewHandlers Review() => new(_store, _permissions, new FakeLookups(), new FakeTimeProvider(Now));

    private CatalogueProduct Suggested(string barcode = "6001234567890", string name = "coke 330", DateTimeOffset? at = null)
    {
        var p = new CatalogueProduct(barcode, name, "330ml", "drinks");
        _store.Products.Add(p);
        return p;
    }

    private CatalogueProduct ApprovedProduct(string barcode = "6009999999999")
    {
        var p = new CatalogueProduct(barcode, "Coca-Cola 330ml", "330ml", "drinks");
        p.Approve();
        _store.Products.Add(p);
        return p;
    }

    // ---- queue ----

    [Fact]
    public async Task The_queue_shows_only_suggestions_and_only_to_a_platform_admin()
    {
        var a = Suggested("6001111111111", "A");
        ApprovedProduct();

        var list = await new ListSuggestionsHandler(_store, _permissions).Handle(new ListSuggestions(_admin), default);
        var stranger = await new ListSuggestionsHandler(_store, _permissions).Handle(new ListSuggestions(Guid.NewGuid()), default);

        Assert.Equal([a.Id], list.Value!.Select(s => s.Id));
        Assert.Equal("permission.denied", stranger.Error!.Code);
    }

    // ---- approve ----

    [Fact]
    public async Task Approving_can_tidy_the_name_size_and_category_and_is_audited()
    {
        var p = Suggested(name: "coke 330");

        var result = await Review().Handle(new ApproveSuggestion(p.Id, new ApproveSuggestionRequest(_admin, "Coca-Cola 330ml", "330 ml", "drinks")), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogueProductStatus.Approved, p.Status);
        Assert.Equal("Coca-Cola 330ml", p.Name);
        Assert.Equal("330 ml", p.Size);
        Assert.Equal(_admin, p.ReviewedBy);
        Assert.Equal(Now, p.ReviewedAt);
        var entry = Assert.Single(_store.Audit);
        Assert.Equal("approved", entry.Action);
        Assert.Equal(p.Id, entry.EntityId);
    }

    [Fact]
    public async Task Approving_with_an_unknown_category_or_a_blank_name_is_refused_and_leaves_it_in_the_queue()
    {
        var p = Suggested();
        Assert.Equal("catalogue.category_invalid", (await Review().Handle(new ApproveSuggestion(p.Id, new(_admin, null, null, "guns")), default)).Error!.Code);
        Assert.Equal("catalogue.name_invalid", (await Review().Handle(new ApproveSuggestion(p.Id, new(_admin, " ", null, null)), default)).Error!.Code);

        Assert.Equal(CatalogueProductStatus.Suggested, p.Status);
        Assert.Empty(_store.Audit);
    }

    // ---- reject ----

    [Fact]
    public async Task Rejecting_needs_a_reason_and_a_decision_is_made_only_once()
    {
        var p = Suggested();

        Assert.Equal("catalogue.reason_required", (await Review().Handle(new RejectSuggestion(p.Id, new(_admin, " ")), default)).Error!.Code);
        Assert.True((await Review().Handle(new RejectSuggestion(p.Id, new(_admin, "Not a real barcode")), default)).IsSuccess);
        Assert.Equal("Not a real barcode", p.ReviewNote);

        Assert.Equal("catalogue.already_reviewed", (await Review().Handle(new ApproveSuggestion(p.Id, new(_admin, null, null, null)), default)).Error!.Code);
        Assert.Single(_store.Audit);
    }

    // ---- merge ----

    [Fact]
    public async Task Merging_closes_the_suggestion_and_tells_the_shops_to_move_to_the_approved_product()
    {
        var target = ApprovedProduct();
        var dup = Suggested("6001234567890", "Coke can");

        var result = await Review().Handle(new MergeSuggestion(dup.Id, new(_admin, target.Id)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogueProductStatus.Merged, dup.Status);
        Assert.Equal(target.Id, dup.MergedIntoId);
        var e = Assert.IsType<CatalogueProductMerged>(Assert.Single(dup.DomainEvents));
        Assert.Equal((dup.Id, target.Id), (e.FromId, e.IntoId));
    }

    [Fact]
    public async Task A_merge_needs_an_approved_target_that_is_a_different_product()
    {
        var dup = Suggested();
        var other = Suggested("6002222222222", "Other");

        Assert.Equal("catalogue.merge_target_invalid", (await Review().Handle(new MergeSuggestion(dup.Id, new(_admin, other.Id)), default)).Error!.Code);
        Assert.Equal("catalogue.merge_self", dup.MergeInto(dup, _admin, Now).Error!.Code);
        Assert.Equal("catalogue.not_found", (await Review().Handle(new MergeSuggestion(dup.Id, new(_admin, Guid.NewGuid())), default)).Error!.Code);
        Assert.Equal(CatalogueProductStatus.Suggested, dup.Status);
    }

    [Fact]
    public async Task Nobody_without_the_approve_permission_can_decide_anything()
    {
        var p = Suggested();
        var stranger = Guid.NewGuid();

        Assert.Equal("permission.denied", (await Review().Handle(new ApproveSuggestion(p.Id, new(stranger, null, null, null)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Review().Handle(new RejectSuggestion(p.Id, new(stranger, "x")), default)).Error!.Code);
        Assert.Equal(CatalogueProductStatus.Suggested, p.Status);
    }

    // ---- lookups ----

    private SaveLookupHandler Save() => new(_store, _permissions);

    [Fact]
    public async Task A_new_lookup_needs_english_a_good_code_and_a_known_type_and_language()
    {
        Assert.Equal("lookup.english_required", (await Save().Handle(new SaveLookup("categories", "pets", new(_admin, new() { ["zu"] = "Ezilwane" }, null, null)), default)).Error!.Code);
        Assert.Equal("lookup.code_invalid", (await Save().Handle(new SaveLookup("categories", "Pets & Co", new(_admin, new() { ["en"] = "Pets" }, null, null)), default)).Error!.Code);
        Assert.Equal("lookup.type_invalid", (await Save().Handle(new SaveLookup("colours", "red", new(_admin, new() { ["en"] = "Red" }, null, null)), default)).Error!.Code);
        Assert.Equal("lookup.language_invalid", (await Save().Handle(new SaveLookup("categories", "pets", new(_admin, new() { ["en"] = "Pets", ["fr"] = "Animaux" }, null, null)), default)).Error!.Code);
        Assert.Empty(_store.LookupRows);

        var ok = await Save().Handle(new SaveLookup("categories", "pets", new(_admin, new() { ["en"] = " Pet food " }, null, 12)), default);
        Assert.Equal("Pet food", ok.Value!.Names["en"]);
        Assert.Equal(12, ok.Value.SortOrder);
        Assert.Equal("created", _store.Audit.Single().Action);
    }

    [Fact]
    public async Task Translators_add_languages_without_losing_english_and_the_new_phase_5_languages_are_allowed()
    {
        await Save().Handle(new SaveLookup("categories", "pets", new(_admin, new() { ["en"] = "Pets" }, null, null)), default);

        var result = await Save().Handle(new SaveLookup("categories", "pets", new(_admin, new() { ["zu"] = "Ezilwane", ["nso"] = "Diruiwa" }, null, null)), default);

        Assert.Equal(new Dictionary<string, string> { ["en"] = "Pets", ["zu"] = "Ezilwane", ["nso"] = "Diruiwa" }, result.Value!.Names);
        Assert.Equal("changed", _store.Audit.Last().Action);
    }

    [Fact]
    public async Task A_lookup_is_switched_off_not_deleted_and_inactive_ones_are_hidden_from_the_till()
    {
        _store.LookupRows.Add(new Lookup("categories", "drinks", new() { ["en"] = "Drinks" }, 1));
        _store.LookupRows.Add(new Lookup("categories", "snacks", new() { ["en"] = "Snacks" }, 2));
        await Save().Handle(new SaveLookup("categories", "snacks", new(_admin, null, false, null)), default);

        var visible = await new GetLookupsHandler(_store).Handle(new GetLookups("categories"), default);
        var all = await new GetLookupsHandler(_store).Handle(new GetLookups("categories", IncludeInactive: true), default);

        Assert.Equal(["drinks"], visible.Value!.Select(l => l.Code));
        Assert.Equal(["drinks", "snacks"], all.Value!.Select(l => l.Code));
        Assert.Equal("lookup.type_invalid", (await new GetLookupsHandler(_store).Handle(new GetLookups("colours"), default)).Error!.Code);
    }

    [Fact]
    public async Task Only_someone_with_the_lookup_permission_can_change_lookups()
    {
        var result = await Save().Handle(new SaveLookup("categories", "pets", new(Guid.NewGuid(), new() { ["en"] = "Pets" }, null, null)), default);
        Assert.Equal("permission.denied", result.Error!.Code);
        Assert.Empty(_store.LookupRows);
    }
}

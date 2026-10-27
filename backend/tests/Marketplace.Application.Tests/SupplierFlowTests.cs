using System.Text;
using Catalogue.Contracts;
using Identity.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Tests;

public class SupplierFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeSuppliers : ISupplierStore, ISupplierProductStore
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = [];
        public Dictionary<Guid, List<SupplierArea>> AreaRows { get; } = [];
        public List<SupplierProduct> Products { get; } = [];
        public int Saves { get; private set; }

        public Task Add(Supplier s, CancellationToken ct) { Suppliers[s.Id] = s; return Task.CompletedTask; }
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult(Suppliers.GetValueOrDefault(id));
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(Suppliers.Values.Where(s => s.Status == SupplierStatus.Pending).ToList());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(AreaRows.GetValueOrDefault(id) ?? []);
        public Task ReplaceAreas(Guid id, List<SupplierArea> areas, CancellationToken ct) { AreaRows[id] = areas; return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }

        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) =>
            Task.FromResult(Products.Where(p => p.SupplierId == id && p.Barcode != null).ToDictionary(p => p.Barcode!));
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(Products.Where(p => p.SupplierId == id).ToList());
        public Task Add(SupplierProduct p, CancellationToken ct) { Products.Add(p); return Task.CompletedTask; }
    }

    private sealed class FakeOrganisations : ISupplierOrganisations
    {
        public List<(Guid Id, string Name, Guid Admin)> Created { get; } = [];
        public List<(Guid Org, string Name, string Phone)> Reps { get; } = [];
        public Dictionary<Guid, string> Status { get; } = [];
        public Result<Guid>? CreateAnswer { get; set; }

        public Task<Result<Guid>> Create(string name, Guid admin, CancellationToken ct)
        {
            if (CreateAnswer is { } a) return Task.FromResult(a);
            var id = Guid.NewGuid();
            Created.Add((id, name, admin));
            return Task.FromResult(Result<Guid>.Ok(id));
        }
        public Task<Result<Guid>> AddRep(Guid org, string name, string phone, CancellationToken ct) { Reps.Add((org, name, phone)); return Task.FromResult(Result<Guid>.Ok(Guid.NewGuid())); }
        public Task<Result> SetStatus(Guid org, string status, CancellationToken ct) { Status[org] = status; return Task.FromResult(Result.Ok()); }
    }

    private sealed class FakeAccess : IOrganisationAccess, IPermissionChecker
    {
        public HashSet<(Guid Person, Guid Org, string Permission)> InOrg { get; } = [];
        public HashSet<(Guid Person, string Permission)> Global { get; } = [];
        public Task<bool> Has(Guid person, Guid org, string permission, CancellationToken ct) => Task.FromResult(InOrg.Contains((person, org, permission)));
        public Task<bool> Has(Guid person, string permission, CancellationToken ct) => Task.FromResult(Global.Contains((person, permission)));
    }

    private sealed class FakeLookups : ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(type == "municipalities" && code is "jhb" or "cpt" or "eth");
    }

    private sealed class FakeCatalogue : ICatalogueDirectory
    {
        public Dictionary<string, CatalogueMatch> Known { get; } = [];
        public Task<Dictionary<string, CatalogueMatch>> FindApproved(IEnumerable<string> barcodes, CancellationToken ct) =>
            Task.FromResult(barcodes.Where(Known.ContainsKey).ToDictionary(b => b, b => Known[b]));
    }

    private readonly FakeSuppliers _store = new();
    private readonly FakeOrganisations _orgs = new();
    private readonly FakeAccess _access = new();
    private readonly FakeCatalogue _catalogue = new();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public SupplierFlowTests() => _access.Global.Add((_admin, "supplier.verify"));

    private static ApplyAsSupplierRequest Form(Guid person, string name = "Fresh Foods", List<AreaRequest>? areas = null) =>
        new(person, name, "2015/123456/07", "Sipho", "0821234567", null, "orders@fresh.co.za", areas ?? [new("jhb", ["Mon", "Wed"])]);

    private ApplyAsSupplierHandler Apply() => new(_store, _orgs, new FakeLookups(), new FakeTimeProvider(Now));

    /// <summary>A supplier that exists and whose owner holds the usual supplier permissions.</summary>
    private async Task<(Supplier Supplier, Guid Org)> ExistingSupplier()
    {
        var result = (await Apply().Handle(new ApplyAsSupplier(Form(_owner)), default)).Value!;
        foreach (var p in new[] { "supplier.manage", "supplier.catalogue", "supplier.reps" }) _access.InOrg.Add((_owner, result.OrganisationId, p));
        return (_store.Suppliers[result.SupplierId], result.OrganisationId);
    }

    // ---- apply ----

    [Fact]
    public async Task Applying_creates_the_organisation_the_pending_supplier_and_its_areas()
    {
        var result = await Apply().Handle(new ApplyAsSupplier(Form(_owner, areas: [new("jhb", ["Mon", "Wed"]), new("cpt", ["Fri"])])), default);

        Assert.Equal("pending", result.Value!.Status);
        var created = Assert.Single(_orgs.Created);
        Assert.Equal((result.Value.OrganisationId, "Fresh Foods", _owner), created);
        var supplier = _store.Suppliers[result.Value.SupplierId];
        Assert.Equal(created.Id, supplier.OrganisationId);
        Assert.Equal(2, _store.AreaRows[supplier.Id].Count);
    }

    [Fact]
    public async Task A_bad_form_creates_nothing_so_no_orphan_organisation_is_left_behind()
    {
        Assert.Equal("supplier.area_invalid", (await Apply().Handle(new ApplyAsSupplier(Form(_owner, areas: [new("atlantis", ["Mon"])])), default)).Error!.Code);
        Assert.Equal("supplier.days_invalid", (await Apply().Handle(new ApplyAsSupplier(Form(_owner, areas: [new("jhb", ["Someday"])])), default)).Error!.Code);
        Assert.Equal("supplier.areas_required", (await Apply().Handle(new ApplyAsSupplier(Form(_owner, areas: [])), default)).Error!.Code);
        Assert.Equal("supplier.name_invalid", (await Apply().Handle(new ApplyAsSupplier(Form(_owner, name: " ")), default)).Error!.Code);

        Assert.Empty(_orgs.Created);
        Assert.Empty(_store.Suppliers);
    }

    [Fact]
    public async Task An_unknown_applicant_is_refused_by_identity()
    {
        _orgs.CreateAnswer = Result<Guid>.Fail("person.not_found", "That person was not found.");
        var result = await Apply().Handle(new ApplyAsSupplier(Form(Guid.NewGuid())), default);
        Assert.Equal("person.not_found", result.Error!.Code);
        Assert.Empty(_store.Suppliers);
    }

    // ---- verify and reject ----

    private SupplierReviewHandlers Review() => new(_store, _access, _orgs, new FakeTimeProvider(Now));

    [Fact]
    public async Task A_platform_admin_verifies_a_supplier_with_the_full_checklist_and_the_organisation_becomes_verified()
    {
        var (supplier, org) = await ExistingSupplier();

        var result = await Review().Handle(new VerifySupplier(supplier.Id, new(_admin, [.. SupplierVerification.Items])), default);

        Assert.True(result.IsSuccess);
        Assert.True(supplier.IsTrading);
        Assert.Equal("verified", _orgs.Status[org]);
    }

    [Fact]
    public async Task Verification_needs_the_permission_and_every_checklist_item()
    {
        var (supplier, org) = await ExistingSupplier();

        Assert.Equal("permission.denied", (await Review().Handle(new VerifySupplier(supplier.Id, new(Guid.NewGuid(), [.. SupplierVerification.Items])), default)).Error!.Code);
        Assert.Equal("supplier.checklist_incomplete", (await Review().Handle(new VerifySupplier(supplier.Id, new(_admin, ["registration_checked"])), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Review().Handle(new VerifySupplier(Guid.NewGuid(), new(_admin, [.. SupplierVerification.Items])), default)).Error!.Code);

        Assert.False(supplier.IsTrading);
        Assert.DoesNotContain(org, _orgs.Status.Keys);
    }

    [Fact]
    public async Task Rejecting_records_the_reason_and_the_pending_list_is_for_admins_only()
    {
        var (supplier, _) = await ExistingSupplier();
        var pending = await Review().Handle(new ListPendingSuppliers(_admin), default);
        Assert.Equal([supplier.Id], pending.Value!.Select(s => s.Id));
        Assert.Equal("permission.denied", (await Review().Handle(new ListPendingSuppliers(Guid.NewGuid()), default)).Error!.Code);

        Assert.Equal("supplier.reason_required", (await Review().Handle(new RejectSupplier(supplier.Id, new(_admin, " ")), default)).Error!.Code);
        Assert.True((await Review().Handle(new RejectSupplier(supplier.Id, new(_admin, "Cannot confirm the registration")), default)).IsSuccess);
        Assert.Equal("rejected", (await new GetSupplierHandler(_store, _access).Handle(new GetSupplier(supplier.Id, _owner), default)).Value!.Status);
        Assert.Empty((await Review().Handle(new ListPendingSuppliers(_admin), default)).Value!);
    }

    // ---- reps, areas, terms ----

    [Fact]
    public async Task Only_someone_allowed_to_manage_reps_in_that_supplier_can_invite_one()
    {
        var (supplier, org) = await ExistingSupplier();
        var handler = new InviteRepHandler(_store, _access, _orgs);

        Assert.True((await handler.Handle(new InviteRep(supplier.Id, new(_owner, "Thandi", "0825550000")), default)).IsSuccess);
        Assert.Equal((org, "Thandi", "0825550000"), Assert.Single(_orgs.Reps));

        var stranger = await handler.Handle(new InviteRep(supplier.Id, new(Guid.NewGuid(), "Spy", "0825551111")), default);
        Assert.Equal("permission.denied", stranger.Error!.Code);
        Assert.Single(_orgs.Reps);
    }

    [Fact]
    public async Task Areas_are_replaced_as_a_whole_after_checking_the_municipality_and_days()
    {
        var (supplier, _) = await ExistingSupplier();
        var handler = new SupplierSettingsHandlers(_store, _access, new FakeLookups());

        Assert.True((await handler.Handle(new SetSupplierAreas(supplier.Id, new(_owner, [new("cpt", ["Tue", "Thu"]), new("eth", ["Sat"])])), default)).IsSuccess);
        Assert.Equal(["cpt", "eth"], _store.AreaRows[supplier.Id].Select(a => a.MunicipalityCode).Order());

        Assert.Equal("supplier.area_invalid", (await handler.Handle(new SetSupplierAreas(supplier.Id, new(_owner, [new("xyz", ["Mon"])])), default)).Error!.Code);
        Assert.Equal("supplier.areas_required", (await handler.Handle(new SetSupplierAreas(supplier.Id, new(_owner, [])), default)).Error!.Code);
        Assert.Equal(2, _store.AreaRows[supplier.Id].Count);          // a failed change leaves the old areas in place
        Assert.Equal("permission.denied", (await handler.Handle(new SetSupplierAreas(supplier.Id, new(Guid.NewGuid(), [new("jhb", ["Mon"])])), default)).Error!.Code);
    }

    [Fact]
    public async Task Terms_are_saved_with_a_known_payment_option_and_refused_otherwise()
    {
        var (supplier, _) = await ExistingSupplier();
        var handler = new SupplierSettingsHandlers(_store, _access, new FakeLookups());

        Assert.True((await handler.Handle(new SetSupplierTerms(supplier.Id, new(_owner, 50_000, 7_500, "in_app")), default)).IsSuccess);
        Assert.Equal((50_000L, 7_500L, PaymentTerms.InApp), (supplier.MinOrderCents, supplier.DeliveryFeeCents, supplier.PaymentTerms));
        Assert.Equal("supplier.terms_invalid", (await handler.Handle(new SetSupplierTerms(supplier.Id, new(_owner, 0, 0, "bitcoin")), default)).Error!.Code);
        Assert.Equal("supplier.terms_invalid", (await handler.Handle(new SetSupplierTerms(supplier.Id, new(_owner, -5, 0, "both")), default)).Error!.Code);
    }

    // ---- catalogue upload ----

    private ImportSupplierCatalogueHandler Import() => new(_store, _store, _access, _catalogue, new FakeTimeProvider(Now));

    private const string PriceList = "EAN,Description,Case Size,Case Price,VAT\n6001111111111,Coke 330ml,24,R180.00,yes\n6002222222222,Bread,1,R18.00,no\n,Mystery,12,,yes\n";

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task A_preview_shows_what_would_happen_and_saves_nothing()
    {
        var (supplier, _) = await ExistingSupplier();
        _catalogue.Known["6001111111111"] = new CatalogueMatch(Guid.NewGuid(), "Coca-Cola 330ml can", "6001111111111");

        var result = (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, Apply: false), default)).Value!;

        Assert.False(result.Applied);
        Assert.Equal((2, 0, 0, 1), (result.Created, result.Updated, result.Unchanged, result.Errors));
        Assert.Equal("EAN", result.Mapping["barcode"]);
        Assert.Equal("Case Price", result.Mapping["case_cost"]);
        Assert.NotNull(result.Lines[0].CatalogueProductId);                  // matched to the shared catalogue by barcode
        Assert.Contains("price_missing", result.Lines[2].Issues);
        Assert.Empty(_store.Products);
    }

    [Fact]
    public async Task Applying_saves_every_usable_row_skips_the_bad_ones_and_a_second_upload_changes_nothing()
    {
        var (supplier, _) = await ExistingSupplier();

        var first = (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, Apply: true), default)).Value!;
        var again = (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, Apply: true), default)).Value!;

        Assert.True(first.Applied);
        Assert.Equal(2, _store.Products.Count);
        var coke = _store.Products.Single(p => p.Barcode == "6001111111111");
        Assert.Equal((24, 18000L, 750L, true), (coke.UnitsPerCase, coke.CasePriceCents, coke.UnitPriceCents, coke.VatIncluded));
        Assert.Equal((0, 0, 2, 1), (again.Created, again.Updated, again.Unchanged, again.Errors));
        Assert.Equal(2, _store.Products.Count);
    }

    [Fact]
    public async Task A_changed_price_updates_the_existing_product_instead_of_adding_a_second_one()
    {
        var (supplier, _) = await ExistingSupplier();
        await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, Apply: true), default);

        var newer = PriceList.Replace("R180.00", "R190.00");
        var result = (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(newer), null, Apply: true), default)).Value!;

        Assert.Equal((0, 1, 1), (result.Created, result.Updated, result.Unchanged));
        Assert.Equal(19000, _store.Products.Single(p => p.Barcode == "6001111111111").CasePriceCents);
    }

    [Fact]
    public async Task The_supplier_can_correct_the_columns_and_bad_files_or_strangers_are_refused()
    {
        var (supplier, _) = await ExistingSupplier();
        var odd = "Code,Stuff,Pack,Cost\n6001111111111,Coke,24,R180.00\n";
        var mapping = new Dictionary<string, string> { ["barcode"] = "Code", ["name"] = "Stuff", ["units_per_case"] = "Pack", ["case_cost"] = "Cost" };

        var ok = (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "odd.csv", Csv(odd), mapping, Apply: false), default)).Value!;
        Assert.Equal(1, ok.Created);

        Assert.Equal("import.mapping_invalid", (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "odd.csv", Csv(odd), new() { ["name"] = "No such column" }, false), default)).Error!.Code);
        Assert.Equal("import.type_invalid", (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.exe", [1, 2, 3], null, false), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, Guid.NewGuid(), "prices.csv", Csv(PriceList), null, false), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Import().Handle(new ImportSupplierCatalogue(Guid.NewGuid(), _owner, "prices.csv", Csv(PriceList), null, false), default)).Error!.Code);
    }

    [Fact]
    public async Task A_rejected_supplier_cannot_upload_and_a_product_can_be_edited_only_by_its_own_supplier()
    {
        var (supplier, _) = await ExistingSupplier();
        await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, Apply: true), default);
        var product = _store.Products.First();

        var edit = new UpdateSupplierProductHandler(_store, _store, _access);
        var changed = await edit.Handle(new UpdateSupplierProduct(product.Id, new(_owner, null, null, 19500, null, false, "Short dated on request")), default);
        Assert.Equal((19500L, false, "Short dated on request"), (changed.Value!.CasePriceCents, changed.Value.InStock, changed.Value.ExpiryPolicy));
        Assert.Equal("permission.denied", (await edit.Handle(new UpdateSupplierProduct(product.Id, new(Guid.NewGuid(), null, null, 1, null, null, null)), default)).Error!.Code);
        Assert.Equal("supplier_product.price_invalid", (await edit.Handle(new UpdateSupplierProduct(product.Id, new(_owner, null, null, -5, null, null, null)), default)).Error!.Code);

        await Review().Handle(new RejectSupplier(supplier.Id, new(_admin, "Not a real business")), default);
        Assert.Equal("supplier.cannot_upload", (await Import().Handle(new ImportSupplierCatalogue(supplier.Id, _owner, "prices.csv", Csv(PriceList), null, false), default)).Error!.Code);
    }
}

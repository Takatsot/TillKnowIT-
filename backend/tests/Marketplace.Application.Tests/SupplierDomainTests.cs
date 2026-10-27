using Marketplace.Domain;

namespace Marketplace.Application.Tests;

public class SupplierDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static SupplierApplication Application(string name = "Fresh Foods", string? reg = "2015/123456/07", string contact = "Sipho", string? phone = "0821234567",
        string? whatsapp = null, string? email = null, int areas = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, reg, contact, phone, whatsapp, email, areas);

    // ---- apply ----

    [Fact]
    public void An_application_starts_pending_and_keeps_who_applied()
    {
        var a = Application();
        var supplier = Supplier.Apply(a, Now).Value!;

        Assert.Equal(SupplierStatus.Pending, supplier.Status);
        Assert.False(supplier.IsTrading);
        Assert.Equal(a.OrganisationId, supplier.OrganisationId);
        Assert.Equal(a.ApplicantPersonId, supplier.ApplicantPersonId);
        Assert.Equal(PaymentTerms.Both, supplier.PaymentTerms);
    }

    [Theory]
    [InlineData("", "supplier.name_invalid")]
    [InlineData("  ", "supplier.name_invalid")]
    public void A_supplier_needs_a_name(string name, string code) =>
        Assert.Equal(code, Supplier.Apply(Application(name: name), Now).Error!.Code);

    [Fact]
    public void A_supplier_needs_a_contact_person_a_way_to_be_reached_and_somewhere_to_deliver()
    {
        Assert.Equal("supplier.contact_required", Supplier.Apply(Application(contact: " "), Now).Error!.Code);
        Assert.Equal("supplier.contact_required", Supplier.Apply(Application(phone: null), Now).Error!.Code);
        Assert.True(Supplier.Apply(Application(phone: null, email: "orders@fresh.co.za"), Now).IsSuccess);
        Assert.True(Supplier.Apply(Application(phone: null, whatsapp: "0821234567"), Now).IsSuccess);
        Assert.Equal("supplier.email_invalid", Supplier.Apply(Application(email: "nope"), Now).Error!.Code);
        Assert.Equal("supplier.areas_required", Supplier.Apply(Application(areas: 0), Now).Error!.Code);
    }

    [Theory]
    [InlineData("2015/123456/07", true)]
    [InlineData("2015/1234/07", true)]
    [InlineData("12345", false)]
    [InlineData("2015-123456-07", false)]
    public void Registration_numbers_have_the_company_registration_shape(string reg, bool ok) =>
        Assert.Equal(ok, Supplier.Apply(Application(reg: reg), Now).IsSuccess);

    [Fact]
    public void A_registration_number_is_optional_for_informal_suppliers()
    {
        var supplier = Supplier.Apply(Application(reg: null), Now).Value!;
        Assert.Null(supplier.RegistrationNo);
    }

    [Fact]
    public void A_supplier_a_shop_added_itself_is_unlisted_and_never_trades_on_the_platform()
    {
        var shop = Guid.NewGuid();
        var supplier = Supplier.Unlisted(shop, " Cash & Carry Joe ", null, "0825550000", null, Now).Value!;

        Assert.Equal(SupplierStatus.Unlisted, supplier.Status);
        Assert.Equal(shop, supplier.CreatedByShopId);
        Assert.Null(supplier.OrganisationId);
        Assert.False(supplier.IsTrading);
        Assert.Equal("supplier.not_on_platform", supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Now).Error!.Code);
        Assert.Equal("supplier.contact_required", Supplier.Unlisted(shop, "Joe", null, null, null, Now).Error!.Code);
    }

    // ---- verify and reject ----

    [Fact]
    public void Verification_needs_every_checklist_item_and_then_the_supplier_trades()
    {
        var supplier = Supplier.Apply(Application(), Now).Value!;
        var admin = Guid.NewGuid();

        var early = supplier.Verify(admin, ["registration_checked", "contact_confirmed"], Now);
        Assert.Equal("supplier.checklist_incomplete", early.Error!.Code);
        Assert.Contains("delivery_capability", early.Error.Message);
        Assert.False(supplier.IsTrading);

        Assert.True(supplier.Verify(admin, SupplierVerification.Items, Now).IsSuccess);
        Assert.True(supplier.IsTrading);
        Assert.Equal((Now, admin), (supplier.VerifiedAt!.Value, supplier.VerifiedBy!.Value));
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_only_applies_to_a_pending_application()
    {
        var supplier = Supplier.Apply(Application(), Now).Value!;
        Assert.Equal("supplier.reason_required", supplier.Reject(Guid.NewGuid(), " ", Now).Error!.Code);

        Assert.True(supplier.Reject(Guid.NewGuid(), "Registration number does not match the name", Now).IsSuccess);
        Assert.Equal(SupplierStatus.Rejected, supplier.Status);
        Assert.Equal("Registration number does not match the name", supplier.RejectionReason);
        Assert.Equal("supplier.not_pending", supplier.Reject(Guid.NewGuid(), "again", Now).Error!.Code);
    }

    [Fact]
    public void A_suspended_supplier_cannot_be_verified_until_reinstated_and_a_rejected_one_can_be_checked_again()
    {
        var supplier = Supplier.Apply(Application(), Now).Value!;
        supplier.Suspend();
        Assert.Equal("supplier.suspended", supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Now).Error!.Code);

        var rejected = Supplier.Apply(Application(), Now).Value!;
        rejected.Reject(Guid.NewGuid(), "Missing details", Now);
        Assert.True(rejected.Verify(Guid.NewGuid(), SupplierVerification.Items, Now).IsSuccess);
        Assert.Null(rejected.RejectionReason);
    }

    // ---- terms ----

    [Fact]
    public void Terms_cannot_be_negative()
    {
        var supplier = Supplier.Apply(Application(), Now).Value!;
        Assert.True(supplier.SetTerms(50_000, 7_500, PaymentTerms.InApp).IsSuccess);
        Assert.Equal((50_000L, 7_500L, PaymentTerms.InApp), (supplier.MinOrderCents, supplier.DeliveryFeeCents, supplier.PaymentTerms));
        Assert.Equal("supplier.terms_invalid", supplier.SetTerms(-1, 0, PaymentTerms.Both).Error!.Code);
        Assert.Equal("supplier.terms_invalid", supplier.SetTerms(0, -1, PaymentTerms.Both).Error!.Code);
    }

    // ---- areas ----

    [Fact]
    public void Delivery_days_parse_to_flags_and_back()
    {
        Assert.True(SupplierArea.TryParseDays(["Mon", "wed", "FRI"], out var days));
        Assert.Equal(DeliveryDays.Mon | DeliveryDays.Wed | DeliveryDays.Fri, days);
        Assert.Equal(["Mon", "Wed", "Fri"], SupplierArea.DayNames(days));
        Assert.False(SupplierArea.TryParseDays(["Mon", "Funday"], out _));
        Assert.False(SupplierArea.TryParseDays(["None"], out _));
    }

    [Fact]
    public void Areas_need_at_least_one_unique_code_with_delivery_days()
    {
        var id = Guid.NewGuid();
        Assert.Equal(2, SupplierArea.Build(id, [("jhb", DeliveryDays.Mon), ("cpt", DeliveryDays.Tue | DeliveryDays.Thu)]).Value!.Count);
        Assert.Equal("supplier.areas_required", SupplierArea.Build(id, []).Error!.Code);
        Assert.Equal("supplier.area_duplicate", SupplierArea.Build(id, [("jhb", DeliveryDays.Mon), ("jhb", DeliveryDays.Tue)]).Error!.Code);
        Assert.Equal("supplier.days_required", SupplierArea.Build(id, [("jhb", DeliveryDays.None)]).Error!.Code);
    }
}

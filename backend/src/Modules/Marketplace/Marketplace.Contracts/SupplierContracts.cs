namespace Marketplace.Contracts;

/// <summary>Days are Mon, Tue, Wed, Thu, Fri, Sat, Sun. MunicipalityCode comes from the municipalities lookup.</summary>
public sealed record AreaRequest(string MunicipalityCode, List<string> Days);

public sealed record AreaResponse(string MunicipalityCode, List<string> Days);

/// <summary>M1-1: a supplier applies to join.</summary>
public sealed record ApplyAsSupplierRequest(
    Guid ApplicantPersonId, string Name, string? RegistrationNo, string ContactName, string? Phone, string? Whatsapp, string? Email, List<AreaRequest> Areas);

public sealed record SupplierApplicationResponse(Guid SupplierId, Guid OrganisationId, string Status);

/// <summary>Checklist items: registration_checked, contact_confirmed, delivery_capability, prices_reviewed. All are needed.</summary>
public sealed record VerifySupplierRequest(Guid AdminPersonId, List<string> Checklist);

public sealed record RejectSupplierRequest(Guid AdminPersonId, string Reason);

public sealed record InviteRepRequest(Guid CallerPersonId, string Name, string Phone);

public sealed record SetAreasRequest(Guid CallerPersonId, List<AreaRequest> Areas);

/// <summary>PaymentTerms is on_delivery, in_app or both.</summary>
public sealed record SetTermsRequest(Guid CallerPersonId, long MinOrderCents, long DeliveryFeeCents, string PaymentTerms);

public sealed record SupplierResponse(
    Guid Id, string Name, string Status, string? RejectionReason, long MinOrderCents, long DeliveryFeeCents, string PaymentTerms,
    List<AreaResponse> Areas, bool Verified);

/// <summary>Action is create, update, unchanged or error. Warnings (case_size_assumed_1, vat_assumed_excluded) do not stop a row.</summary>
public sealed record CatalogueLineResponse(
    int Row, string? Barcode, string Name, int UnitsPerCase, long CasePriceCents, bool VatIncluded, bool InStock,
    Guid? CatalogueProductId, string Action, List<string> Issues);

/// <summary>
/// The result of a catalogue upload. With Apply false this is a preview and nothing is saved; send the same file again with
/// Apply true to save. Mapping says which column is which and can be corrected.
/// </summary>
public sealed record CatalogueUploadResponse(
    bool Applied, int Created, int Updated, int Unchanged, int Errors, List<CatalogueLineResponse> Lines,
    List<string> Headers, Dictionary<string, string> Mapping);

public sealed record SupplierProductResponse(
    Guid Id, string? Barcode, string Name, int UnitsPerCase, long CasePriceCents, long UnitPriceCents, bool VatIncluded, bool InStock,
    string? ExpiryPolicy, Guid? CatalogueProductId);

public sealed record UpdateSupplierProductRequest(
    Guid CallerPersonId, string? Name, int? UnitsPerCase, long? CasePriceCents, bool? VatIncluded, bool? InStock, string? ExpiryPolicy);

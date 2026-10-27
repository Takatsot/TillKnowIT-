namespace Marketplace.Contracts;

public sealed record ComboItemDto(Guid SupplierProductId, int Cases);

/// <summary>Which fields apply depends on the deal type: see Deal.Create. Discount needs a product and either PercentOff or CentsOffPerCase.</summary>
public sealed record DealTermsDto(
    Guid? SupplierProductId, int? PercentOff, long? CentsOffPerCase, int? BuyCases, int? FreeCases, List<ComboItemDto>? Items, long? ComboPriceCents);

/// <summary>Type is discount, free_goods or combo. A sponsored deal is always labelled "Sponsored" wherever shops see it.</summary>
public sealed record CreateDealRequest(
    Guid CallerPersonId, string Title, string Type, DealTermsDto Terms, DateTimeOffset StartsAt, DateTimeOffset EndsAt, List<string> Areas, bool IsSponsored);

public sealed record DealResponse(
    Guid Id, Guid SupplierId, string SupplierName, string Title, string Type, DealTermsDto Terms, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    List<string> Areas, bool IsSponsored, string? Label, bool IsActive);

public sealed record CancelDealRequest(Guid CallerPersonId);

/// <summary>Mode is off, usual or all.</summary>
public sealed record SetSharingRequest(Guid CallerPersonId, string Mode);

public sealed record SharingResponse(string Mode);

public sealed record SetUsualSupplierRequest(Guid CallerPersonId, Guid SupplierId);

public sealed record SaveMarketplaceSettingRequest(Guid AdminPersonId, string Value);

/// <summary>
/// One option for reordering a product. TrueCostPerUnitCents includes VAT, any deal and the delivery fee, per unit received.
/// MarginPercent is at the shop's current selling price. Label is "Sponsored" for sponsored deals and null otherwise.
/// </summary>
public sealed record OfferResponse(
    int Position, Guid SupplierId, string SupplierName, Guid SupplierProductId, int Cases, int UnitsReceived, long TotalCostCents,
    long TrueCostPerUnitCents, double? MarginPercent, bool IsCheapest, bool IsUsual, long? LastPriceCents, bool MeetsMinimum,
    int DeliveryDaysAway, double? Rating, string? DealTitle, string? Label);

public sealed record OffersResponse(Guid ProductId, string ProductName, int QuantityUnits, long? SellingPriceCents, List<OfferResponse> Offers);

/// <summary>M3-1: one line of the order list: a product worth reordering, with the top option and the cheapest.</summary>
public sealed record OrderListItem(
    Guid ProductId, string ProductName, int OnHand, int? DaysLeft, int QuantityUnits, List<string> Flags, int OfferCount,
    OfferResponse? Best, OfferResponse? Cheapest);

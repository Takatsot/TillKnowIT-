namespace Marketplace.Contracts;

public sealed record RateOrderRequest(Guid CallerPersonId, bool OnTime, bool Complete, int Score, string? Comment);

public sealed record RatingResponse(Guid OrderId, bool OnTime, bool Complete, int Score, string? Comment, DateTimeOffset RatedAt);

/// <summary>Comments are shown without the shop that wrote them. Average and percentages are empty until enough deliveries are rated.</summary>
public sealed record SupplierRatingSummary(int Count, double? AverageScore, int? OnTimePercent, int? CompletePercent, List<string> RecentComments);

/// <summary>Opted-in shops in one area that are running low on one of the supplier's products. Counts only: never names, sales or stock levels.</summary>
public sealed record DemandProduct(Guid SupplierProductId, string Name, int ShopsLow);

public sealed record DemandArea(string AreaCode, List<DemandProduct> Products);

/// <summary>
/// Areas and products with fewer than MinShops shops are left out, so a total can never point at one shop.
/// </summary>
public sealed record DemandResponse(int MinShops, List<DemandArea> Areas);

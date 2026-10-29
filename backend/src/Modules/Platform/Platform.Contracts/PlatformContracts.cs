using TillKnowIT.SharedKernel;

namespace Platform.Contracts;

public sealed record FeatureDto(string Flag, string? SettingsJson);

public sealed record PackResponse(Guid Id, string Code, string Name, string? Description, string SlipTemplateCode, int Version, bool IsActive, List<FeatureDto> Features);

public sealed record SavePackRequest(Guid CallerPersonId, string Code, string Name, string? Description, string SlipTemplateCode, List<FeatureDto> Features);

public sealed record ChoosePackRequest(Guid CallerPersonId, string PackCode);

/// <summary>What a shop is on now, in the form a till downloads: the pack, its version and every feature that is on. ShopName is the shop's own name, for the slip.</summary>
public sealed record ShopPackResponse(Guid ShopId, string PackCode, string PackName, int PackVersion, string SlipTemplateCode, List<FeatureDto> Features, string ShopName);

/// <summary>
/// How other modules ask "is this feature on for this shop?" without knowing what a pack is. A shop with no pack chosen is on the default pack.
/// </summary>
public interface IShopFeatures
{
    Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct);

    Task<ShopPackResponse> For(Guid shopId, CancellationToken ct);
}

/// <summary>A shop changed business type. Tills refresh their pack when they hear this.</summary>
public sealed record ShopPackChanged(Guid ShopId, string? FromCode, string ToCode) : DomainEvent;

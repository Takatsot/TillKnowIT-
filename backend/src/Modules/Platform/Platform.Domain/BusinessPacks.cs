using System.Text.RegularExpressions;
using TillKnowIT.SharedKernel;

namespace Platform.Domain;

/// <summary>
/// Every switch a business pack can turn on. The selling code never asks "which pack is this shop on?"; it asks whether a feature is on,
/// so a new business type is a new pack made of these flags, with no change to the core.
/// </summary>
public static class FeatureFlags
{
    public const string SplitPayments = "split_payments";
    public const string Refunds = "refunds";
    public const string OpenTickets = "open_tickets";
    public const string Tables = "tables";
    public const string Kitchen = "kitchen";
    public const string Tips = "tips";
    public const string Variants = "variants";
    public const string Modifiers = "modifiers";
    public const string Promotions = "promotions";
    public const string Weight = "weight";
    public const string PriceEmbeddedBarcodes = "price_embedded_barcodes";
    public const string Appointments = "appointments";
    public const string Commission = "commission";
    public const string Quotes = "quotes";
    public const string Layby = "layby";
    public const string SerialNumbers = "serial_numbers";
    public const string Customers = "customers";
    public const string Loyalty = "loyalty";
    public const string Vouchers = "vouchers";
    public const string Printer = "printer";
    public const string CashDrawer = "cash_drawer";
    public const string Scale = "scale";
    public const string MessageBundles = "message_bundles";

    public static readonly string[] All =
    [
        SplitPayments, Refunds, OpenTickets, Tables, Kitchen, Tips, Variants, Modifiers, Promotions, Weight, PriceEmbeddedBarcodes,
        Appointments, Commission, Quotes, Layby, SerialNumbers, Customers, Loyalty, Vouchers, Printer, CashDrawer, Scale, MessageBundles,
    ];

    public static bool IsKnown(string? flag) => flag is not null && All.Contains(flag);
}

public sealed record FeatureSetting(string Flag, string? SettingsJson);

public sealed record PackInput(string Code, string Name, string? Description, string SlipTemplateCode, IReadOnlyList<FeatureSetting> Features);

/// <summary>
/// B1-1: a business type as data. Which features, settings and slip layout a shop gets. Editing a pack makes a new version, so a till can tell
/// that what it holds is out of date.
/// </summary>
public sealed class BusinessPack : VersionedEntity
{
    private static readonly Regex CodeShape = new("^[a-z][a-z0-9_]{1,29}$", RegexOptions.Compiled);

    private readonly List<PackFeature> _features = [];

    private BusinessPack() { }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string SlipTemplateCode { get; private set; } = "slip.sale";
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<PackFeature> Features => _features;

    public bool Has(string flag) => _features.Any(f => f.Flag == flag);

    public static Result<BusinessPack> Create(PackInput input, DateTimeOffset now)
    {
        var valid = Validate(input);
        if (!valid.IsSuccess) return Result<BusinessPack>.Fail(valid.Error!.Code, valid.Error.Message);
        var pack = new BusinessPack { Code = input.Code.Trim(), CreatedAt = now };
        pack.Apply(input);
        return Result<BusinessPack>.Ok(pack);
    }

    /// <summary>Replaces the name, features and layout. The version goes up by one.</summary>
    public Result Edit(PackInput input)
    {
        var valid = Validate(input with { Code = Code });
        if (!valid.IsSuccess) return valid;
        Apply(input with { Code = Code });
        Touch();
        return Result.Ok();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private void Apply(PackInput input)
    {
        Name = input.Name.Trim();
        Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        SlipTemplateCode = input.SlipTemplateCode.Trim();
        _features.Clear();
        foreach (var f in input.Features.DistinctBy(f => f.Flag)) _features.Add(new PackFeature(Id, f.Flag, f.SettingsJson));
    }

    private static Result Validate(PackInput i)
    {
        if (!CodeShape.IsMatch(i.Code?.Trim() ?? "")) return Result.Fail("pack.code_invalid", "A pack code is lowercase letters, digits and underscores, 2 to 30 characters, starting with a letter.");
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > 80) return Result.Fail("pack.name_invalid", "Give the pack a name of up to 80 characters.");
        if (string.IsNullOrWhiteSpace(i.SlipTemplateCode) || i.SlipTemplateCode.Length > 50) return Result.Fail("pack.template_invalid", "Choose the slip template.");
        var unknown = i.Features.FirstOrDefault(f => !FeatureFlags.IsKnown(f.Flag));
        if (unknown is not null) return Result.Fail("pack.feature_unknown", $"'{unknown.Flag}' is not a known feature.");
        var badSettings = i.Features.FirstOrDefault(f => f.SettingsJson is { } j && !IsJsonObject(j));
        if (badSettings is not null) return Result.Fail("pack.settings_invalid", $"The settings for '{badSettings.Flag}' must be a JSON object.");
        return Result.Ok();
    }

    private static bool IsJsonObject(string json)
    {
        try { return System.Text.Json.JsonDocument.Parse(json).RootElement.ValueKind == System.Text.Json.JsonValueKind.Object; }
        catch (System.Text.Json.JsonException) { return false; }
    }
}

public sealed class PackFeature : Entity
{
    private PackFeature() { }

    public PackFeature(Guid packId, string flag, string? settingsJson)
    {
        PackId = packId;
        Flag = flag;
        SettingsJson = settingsJson;
    }

    public Guid PackId { get; private set; }
    public string Flag { get; private set; } = default!;
    public string? SettingsJson { get; private set; }
}

/// <summary>The pack a shop is on now. A shop with none is on the default (spaza) pack.</summary>
public sealed class ShopPack : VersionedEntity
{
    public const string DefaultCode = "spaza";

    private ShopPack() { }

    public ShopPack(Guid shopId, string packCode, Guid? chosenBy, DateTimeOffset now)
    {
        ShopId = shopId;
        PackCode = packCode;
        ChosenBy = chosenBy;
        ChosenAt = now;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public string PackCode { get; private set; } = default!;
    public Guid? ChosenBy { get; private set; }
    public DateTimeOffset ChosenAt { get; private set; }

    public void Change(string packCode, Guid? by, DateTimeOffset now)
    {
        PackCode = packCode;
        ChosenBy = by;
        ChosenAt = now;
        Touch();
    }
}

/// <summary>Every time a shop changed pack, for good. Switching never deletes the shop's data; this is only the record of the switch.</summary>
public sealed class ShopPackChange : Entity
{
    private ShopPackChange() { }

    public ShopPackChange(Guid shopId, string? fromCode, string toCode, Guid? by, DateTimeOffset now)
    {
        ShopId = shopId;
        FromCode = fromCode;
        ToCode = toCode;
        CreatedBy = by;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public string? FromCode { get; private set; }
    public string ToCode { get; private set; } = default!;
}

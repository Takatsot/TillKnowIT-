using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain.Selling;

/// <summary>
/// B2-2: a group of add-ons that can be offered on products, such as "Extras" or "Sauce". Each option is an ordinary product (the cheese, the bacon),
/// so what an add-on costs, and its stock, are managed like any other product and the sale flow needs nothing new to sell one.
/// </summary>
public sealed class ModifierGroup : VersionedEntity
{
    private readonly List<ModifierOptionRow> _options = [];

    private ModifierGroup() { }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool Required { get; private set; }
    public int Min { get; private set; }
    public int Max { get; private set; }
    public IReadOnlyList<ModifierOptionRow> Options => _options;

    public static Result<ModifierGroup> Create(Guid shopId, string name, bool required, int min, int max, IReadOnlyList<Guid> optionProductIds, DateTimeOffset now)
    {
        var valid = ModifierGroupDef.Validate(name, required, min, max, optionProductIds.Count);
        if (!valid.IsSuccess) return Result<ModifierGroup>.Fail(valid.Error!.Code, valid.Error.Message);
        if (optionProductIds.Distinct().Count() != optionProductIds.Count) return Result<ModifierGroup>.Fail("modifier.option_repeated", "Each option can be listed once.");
        if (max > optionProductIds.Count) return Result<ModifierGroup>.Fail("modifier.limits_invalid", "The maximum cannot be more than the number of options.");

        var group = new ModifierGroup { ShopId = shopId, Name = name.Trim(), Required = required, Min = min, Max = max, CreatedAt = now };
        for (var i = 0; i < optionProductIds.Count; i++) group._options.Add(new ModifierOptionRow(group.Id, optionProductIds[i], i));
        return Result<ModifierGroup>.Ok(group);
    }

    public Result Change(string name, bool required, int min, int max, IReadOnlyList<Guid> optionProductIds)
    {
        var valid = ModifierGroupDef.Validate(name, required, min, max, optionProductIds.Count);
        if (!valid.IsSuccess) return valid;
        if (optionProductIds.Distinct().Count() != optionProductIds.Count) return Result.Fail("modifier.option_repeated", "Each option can be listed once.");
        if (max > optionProductIds.Count) return Result.Fail("modifier.limits_invalid", "The maximum cannot be more than the number of options.");
        Name = name.Trim();
        Required = required;
        Min = min;
        Max = max;
        _options.Clear();
        for (var i = 0; i < optionProductIds.Count; i++) _options.Add(new ModifierOptionRow(Id, optionProductIds[i], i));
        Touch();
        return Result.Ok();
    }
}

public sealed class ModifierOptionRow : Entity
{
    private ModifierOptionRow() { }

    public ModifierOptionRow(Guid groupId, Guid productId, int sortOrder)
    {
        GroupId = groupId;
        ProductId = productId;
        SortOrder = sortOrder;
    }

    public Guid GroupId { get; private set; }

    /// <summary>The add-on, which is a product of the shop.</summary>
    public Guid ProductId { get; private set; }

    public int SortOrder { get; private set; }
}

/// <summary>Which groups a product offers.</summary>
public sealed class ProductModifierGroup : Entity
{
    private ProductModifierGroup() { }

    public ProductModifierGroup(Guid productId, Guid groupId, int sortOrder)
    {
        ProductId = productId;
        GroupId = groupId;
        SortOrder = sortOrder;
    }

    public Guid ProductId { get; private set; }
    public Guid GroupId { get; private set; }
    public int SortOrder { get; private set; }
}

/// <summary>B2-3: a promotion a shop runs, stored; the pricing engine reads it as a <see cref="PromotionDef"/>.</summary>
public sealed class Promotion : VersionedEntity
{
    private Promotion() { }

    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = default!;
    public PromotionType Type { get; private set; }
    public string ProductIdsJson { get; private set; } = "[]";
    public int Percent { get; private set; }
    public long AmountCents { get; private set; }
    public string ItemsJson { get; private set; } = "[]";
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public Weekdays Days { get; private set; } = Weekdays.All;
    public TimeOnly? FromTime { get; private set; }
    public TimeOnly? ToTime { get; private set; }
    public bool IsActive { get; private set; } = true;

    public sealed record Input(
        string Name, PromotionType Type, IReadOnlyList<Guid> ProductIds, int Percent, long AmountCents, IReadOnlyList<ComboItem> Items,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt, Weekdays Days, TimeOnly? From, TimeOnly? To);

    public static Result<Promotion> Create(Guid shopId, Input i, DateTimeOffset now)
    {
        var valid = Validate(i);
        if (!valid.IsSuccess) return Result<Promotion>.Fail(valid.Error!.Code, valid.Error.Message);
        var p = new Promotion { ShopId = shopId, CreatedAt = now };
        p.Apply(i);
        return Result<Promotion>.Ok(p);
    }

    public Result Change(Input i)
    {
        var valid = Validate(i);
        if (!valid.IsSuccess) return valid;
        Apply(i);
        Touch();
        return Result.Ok();
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        Touch();
    }

    private static Result Validate(Input i)
    {
        var basic = PromotionDef.Validate(i.Type, i.Name, i.ProductIds, i.Percent, i.AmountCents, i.Items, i.StartsAt, i.EndsAt, i.Days);
        if (!basic.IsSuccess) return basic;
        if ((i.From is null) != (i.To is null)) return Result.Fail("promotion.hours_invalid", "Give both the start and end hour, or neither.");
        if (i.From is { } f && i.To is { } t && f == t) return Result.Fail("promotion.hours_invalid", "The start and end hour cannot be the same.");
        return Result.Ok();
    }

    private void Apply(Input i)
    {
        Name = i.Name.Trim();
        Type = i.Type;
        ProductIdsJson = JsonSerializer.Serialize(i.ProductIds);
        Percent = i.Percent;
        AmountCents = i.AmountCents;
        ItemsJson = JsonSerializer.Serialize(i.Items);
        StartsAt = i.StartsAt;
        EndsAt = i.EndsAt;
        Days = i.Days;
        FromTime = i.From;
        ToTime = i.To;
    }

    public PromotionDef ToDef() => new(
        Id, Name, Type, JsonSerializer.Deserialize<List<Guid>>(ProductIdsJson) ?? [], Percent, AmountCents,
        JsonSerializer.Deserialize<List<ComboItem>>(ItemsJson) ?? [], StartsAt, EndsAt, Days, FromTime, ToTime, IsActive);
}

/// <summary>B3-1: a table, bar stool or counter spot the floor plan lists. Tickets point at it; replacing the plan never deletes a table, it retires it.</summary>
public sealed class FloorTable : Entity
{
    private FloorTable() { }

    public FloorTable(Guid shopId, string area, string name, int seats, int sortOrder, DateTimeOffset now)
    {
        ShopId = shopId;
        Area = area;
        Name = name;
        Seats = seats;
        SortOrder = sortOrder;
        IsActive = true;
        CreatedAt = now;
    }

    public Guid ShopId { get; private set; }
    public string Area { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public int Seats { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(int seats, int sortOrder)
    {
        Seats = seats;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public void Retire() => IsActive = false;

    public sealed record Input(string Area, string Name, int Seats);

    public static Result Validate(IReadOnlyList<Input> plan)
    {
        if (plan.Count > 200) return Result.Fail("floor.too_many", "A floor plan has at most 200 tables.");
        foreach (var t in plan)
        {
            if (string.IsNullOrWhiteSpace(t.Name) || t.Name.Trim().Length > 30) return Result.Fail("floor.name_invalid", "Every table needs a name of up to 30 characters.");
            if (string.IsNullOrWhiteSpace(t.Area) || t.Area.Trim().Length > 30) return Result.Fail("floor.area_invalid", "Every table needs an area, such as Inside or Patio.");
            if (t.Seats is < 1 or > 50) return Result.Fail("floor.seats_invalid", "A table seats from 1 to 50.");
        }
        if (plan.Select(t => (t.Area.Trim().ToLowerInvariant(), t.Name.Trim().ToLowerInvariant())).Distinct().Count() != plan.Count) return Result.Fail("floor.duplicate", "Two tables in the same area cannot have the same name.");
        return Result.Ok();
    }
}

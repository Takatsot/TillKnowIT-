using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

public enum GroupDealStatus { Open, Reached, Failed, Cancelled }

public sealed record GroupDealInput(Guid SupplierProductId, long BulkPriceCents, int MinTotalCases, DateTimeOffset ClosesAt, string AreaCode);

/// <summary>
/// M6-1: a supplier posts a bulk price that holds only if enough shops in an area commit in total by a closing date.
/// Reached: each shop gets its own order at the bulk price. Not reached: nobody is charged, because nothing is charged until delivery.
/// Shops only ever see the group's total, never who else joined or how much they asked for.
/// </summary>
public sealed class GroupDeal : VersionedEntity
{
    public const int MaxCasesPerShop = 10_000;
    public static readonly TimeSpan MaxRunTime = TimeSpan.FromDays(60);

    private readonly List<GroupCommitment> _commitments = [];

    private GroupDeal() { }

    public Guid SupplierId { get; private set; }
    public Guid SupplierProductId { get; private set; }
    public long BulkPriceCents { get; private set; }
    public int MinTotalCases { get; private set; }
    public DateTimeOffset ClosesAt { get; private set; }
    public string AreaCode { get; private set; } = default!;
    public GroupDealStatus Status { get; private set; }
    public IReadOnlyCollection<GroupCommitment> Commitments => _commitments;

    public int TotalCases => _commitments.Sum(c => c.Cases);
    public bool ThresholdReached => TotalCases >= MinTotalCases;

    public static Result<GroupDeal> Create(Guid supplierId, GroupDealInput i, IReadOnlyCollection<string> supplierAreas, DateTimeOffset now)
    {
        if (i.BulkPriceCents <= 0) return Result<GroupDeal>.Fail("group.price_invalid", "The bulk price must be more than zero.");
        if (i.MinTotalCases < 2 || i.MinTotalCases > 1_000_000) return Result<GroupDeal>.Fail("group.minimum_invalid", "The minimum must be at least 2 cases.");
        if (i.ClosesAt <= now) return Result<GroupDeal>.Fail("group.closing_invalid", "The closing date must be in the future.");
        if (i.ClosesAt - now > MaxRunTime) return Result<GroupDeal>.Fail("group.closing_invalid", "A group deal can run for at most 60 days.");
        if (!supplierAreas.Contains(i.AreaCode)) return Result<GroupDeal>.Fail("group.area_invalid", "You can only open a group in an area you deliver to.");
        return Result<GroupDeal>.Ok(new GroupDeal
        {
            SupplierId = supplierId, SupplierProductId = i.SupplierProductId, BulkPriceCents = i.BulkPriceCents, MinTotalCases = i.MinTotalCases,
            ClosesAt = i.ClosesAt, AreaCode = i.AreaCode, Status = GroupDealStatus.Open, CreatedAt = now,
        });
    }

    /// <summary>A shop joins with its quantity, changes it, or withdraws with zero. Only while the group is open.</summary>
    public Result Commit(Guid shopId, Guid person, int cases, DateTimeOffset now)
    {
        if (Status != GroupDealStatus.Open || now >= ClosesAt) return Result.Fail("group.closed", "This group deal is closed.");
        if (cases < 0 || cases > MaxCasesPerShop) return Result.Fail("group.quantity_invalid", $"Choose from 0 to {MaxCasesPerShop} cases.");

        var existing = _commitments.FirstOrDefault(c => c.ShopId == shopId);
        if (cases == 0)
        {
            if (existing is not null) _commitments.Remove(existing);
        }
        else if (existing is null) _commitments.Add(new GroupCommitment(Id, shopId, person, cases, now));
        else existing.Change(cases, person);
        Touch();
        return Result.Ok();
    }

    public int CasesOf(Guid shopId) => _commitments.FirstOrDefault(c => c.ShopId == shopId)?.Cases ?? 0;

    /// <summary>Closing time: reached or failed by the total. Does nothing before the closing time.</summary>
    public Result CloseDue(DateTimeOffset now)
    {
        if (Status != GroupDealStatus.Open) return Result.Fail("group.not_open", "This group deal is not open.");
        if (now < ClosesAt) return Result.Fail("group.not_due", "This group deal has not reached its closing time.");
        Status = ThresholdReached ? GroupDealStatus.Reached : GroupDealStatus.Failed;
        Touch();
        return Result.Ok();
    }

    /// <summary>The supplier may close early once the minimum is met, so the shops get their orders sooner.</summary>
    public Result CloseEarly()
    {
        if (Status != GroupDealStatus.Open) return Result.Fail("group.not_open", "This group deal is not open.");
        if (!ThresholdReached) return Result.Fail("group.minimum_not_met", "The minimum has not been reached yet.");
        Status = GroupDealStatus.Reached;
        Touch();
        return Result.Ok();
    }

    public Result Cancel()
    {
        if (Status != GroupDealStatus.Open) return Result.Fail("group.not_open", "Only an open group deal can be cancelled.");
        Status = GroupDealStatus.Cancelled;
        Touch();
        return Result.Ok();
    }
}

public sealed class GroupCommitment : Entity
{
    private GroupCommitment() { }

    internal GroupCommitment(Guid groupDealId, Guid shopId, Guid person, int cases, DateTimeOffset now)
    {
        GroupDealId = groupDealId;
        ShopId = shopId;
        CreatedBy = person;
        Cases = cases;
        CreatedAt = now;
    }

    public Guid GroupDealId { get; private set; }
    public Guid ShopId { get; private set; }
    public int Cases { get; private set; }

    /// <summary>Set when the group is reached and this shop's order has been made.</summary>
    public Guid? OrderId { get; private set; }

    internal void Change(int cases, Guid person)
    {
        Cases = cases;
        CreatedBy = person;
    }

    public void AttachOrder(Guid orderId) => OrderId = orderId;
}

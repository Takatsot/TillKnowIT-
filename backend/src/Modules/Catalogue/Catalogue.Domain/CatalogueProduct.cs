using Catalogue.Contracts;
using TillKnowIT.SharedKernel;

namespace Catalogue.Domain;

public enum CatalogueProductStatus { Suggested, Approved, Rejected, Merged }

/// <summary>Shared master list of products, identified by barcode.</summary>
public sealed class CatalogueProduct : VersionedEntity
{
    private CatalogueProduct() { }

    public CatalogueProduct(string barcode, string name, string? size, string categoryCode)
    {
        Barcode = barcode;
        Name = name;
        Size = size;
        CategoryCode = categoryCode;
    }

    public string Barcode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Size { get; private set; }
    public string CategoryCode { get; private set; } = default!;
    public string? PhotoUrl { get; private set; }
    public CatalogueProductStatus Status { get; private set; } = CatalogueProductStatus.Suggested;
    public Guid? MergedIntoId { get; private set; }
    public string? ReviewNote { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    private Result CanReview() => Status == CatalogueProductStatus.Suggested
        ? Result.Ok()
        : Result.Fail("catalogue.already_reviewed", "This suggestion has already been reviewed.");

    /// <summary>The reviewer may tidy the name, size or category while approving.</summary>
    public Result Approve(Guid? reviewer = null, DateTimeOffset? at = null, string? name = null, string? size = null, string? categoryCode = null)
    {
        var can = CanReview();
        if (!can.IsSuccess) return can;
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) return Result.Fail("catalogue.name_invalid", "A product needs a name (up to 200 characters).");
            Name = name.Trim();
        }
        if (size is not null) Size = string.IsNullOrWhiteSpace(size) ? null : size.Trim();
        if (categoryCode is not null) CategoryCode = categoryCode;
        Close(CatalogueProductStatus.Approved, reviewer, at, null);
        return Result.Ok();
    }

    public Result Reject(Guid? reviewer = null, DateTimeOffset? at = null, string? reason = null)
    {
        var can = CanReview();
        if (!can.IsSuccess) return can;
        // A reviewer must say why, so the shop that suggested it can be told. Callers with no reviewer (seeding) may omit it.
        if (reviewer is not null && string.IsNullOrWhiteSpace(reason)) return Result.Fail("catalogue.reason_required", "Say why the suggestion is rejected.");
        Close(CatalogueProductStatus.Rejected, reviewer, at, reason?.Trim());
        return Result.Ok();
    }

    /// <summary>This suggestion is the same product as an approved one. Shops that used it are moved across.</summary>
    public Result MergeInto(CatalogueProduct target, Guid reviewer, DateTimeOffset at)
    {
        var can = CanReview();
        if (!can.IsSuccess) return can;
        if (target.Id == Id) return Result.Fail("catalogue.merge_self", "A product cannot be merged into itself.");
        if (target.Status != CatalogueProductStatus.Approved) return Result.Fail("catalogue.merge_target_invalid", "Merge into an approved product.");
        MergedIntoId = target.Id;
        Close(CatalogueProductStatus.Merged, reviewer, at, $"Merged into {target.Barcode}");
        Raise(new CatalogueProductMerged(Id, target.Id));
        return Result.Ok();
    }

    private void Close(CatalogueProductStatus status, Guid? reviewer, DateTimeOffset? at, string? note)
    {
        Status = status;
        ReviewedBy = reviewer;
        ReviewedAt = at;
        ReviewNote = note;
        Touch();
    }
}

using TillKnowIT.SharedKernel;

namespace Marketplace.Domain;

/// <summary>M7-1: a shop's rating of one delivery. On time, complete, and a score of 1 to 5. One rating per order, for good.</summary>
public sealed class Rating : Entity
{
    /// <summary>A supplier's average only counts in rankings and on screen once this many deliveries have been rated.</summary>
    public const int MinRatingsToShow = 3;

    private Rating() { }

    public Guid OrderId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public bool OnTime { get; private set; }
    public bool Complete { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }

    public static Result<Rating> Create(PurchaseOrder order, Guid person, bool onTime, bool complete, int score, string? comment, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.Delivered) return Result<Rating>.Fail("rating.not_delivered", "You can rate an order once it has been delivered.");
        if (score is < 1 or > 5) return Result<Rating>.Fail("rating.score_invalid", "The score is from 1 to 5.");
        var text = comment?.Trim();
        if (text is { Length: > 500 }) return Result<Rating>.Fail("rating.comment_too_long", "Keep the comment under 500 characters.");
        return Result<Rating>.Ok(new Rating
        {
            OrderId = order.Id, ShopId = order.ShopId, SupplierId = order.SupplierId, OnTime = onTime, Complete = complete, Score = score,
            Comment = string.IsNullOrEmpty(text) ? null : text, CreatedBy = person, CreatedAt = now,
        });
    }
}

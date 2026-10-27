using TillKnowIT.SharedKernel;

namespace Identity.Domain;

/// <summary>E13-1: what a field agent does at a new shop before it is verified.</summary>
public static class SetupChecklist
{
    public static readonly string[] Items =
    [
        "device_check", "shop_details", "document_photos", "catalogue_import", "staff_pins", "test_sale", "training_signoff",
    ];

    public static bool IsItem(string? item) => item is not null && Items.Contains(item);

    public static bool IsComplete(IEnumerable<string> doneItems)
    {
        var done = doneItems.ToHashSet();
        return Items.All(done.Contains);
    }
}

public sealed class SetupStep : Entity
{
    private SetupStep() { }

    public SetupStep(Guid shopId, string item, Guid completedBy, string? note, DateTimeOffset now)
    {
        ShopId = shopId;
        Item = item;
        CompletedBy = completedBy;
        CompletedAt = now;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid ShopId { get; private set; }
    public string Item { get; private set; } = default!;
    public Guid CompletedBy { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public string? Note { get; private set; }
}

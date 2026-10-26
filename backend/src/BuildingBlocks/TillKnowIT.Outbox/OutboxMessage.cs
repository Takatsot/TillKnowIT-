namespace TillKnowIT.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Failed deliveries so far. Messages stop being retried at <see cref="OutboxProcessor{TContext}.MaxAttempts"/>.</summary>
    public int Attempts { get; set; }
    public string? Error { get; set; }
}

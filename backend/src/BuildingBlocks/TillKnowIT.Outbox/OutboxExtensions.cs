using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Outbox;

public static class OutboxExtensions
{
    /// <summary>Adds the outbox table to a module's own schema.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(500);
            b.Property(x => x.Error).HasMaxLength(1000);
            b.Property(x => x.Payload).HasColumnType("nvarchar(max)");
            b.HasIndex(x => x.OccurredAt).HasFilter("processed_at IS NULL");
        });
        return modelBuilder;
    }
}

/// <summary>Turns domain events raised on entities into outbox rows in the same transaction as the change.</summary>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return ValueTask.FromResult(result);

        var sources = ctx.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();

        foreach (var source in sources)
        {
            foreach (var e in source.DomainEvents)
                ctx.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Type = e.GetType().AssemblyQualifiedName!,
                    Payload = JsonSerializer.Serialize(e, e.GetType()),
                    OccurredAt = e.OccurredAt,
                });
            source.ClearDomainEvents();
        }
        return ValueTask.FromResult(result);
    }
}

using Messaging.Application.Sending;
using Messaging.Domain;
using Messaging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messaging.Infrastructure;

internal sealed class MessagingStore(MessagingDbContext db) : IMessagingStore
{
    public async Task<MessageTemplate?> FindTemplate(string code, string language, MessageChannel channel, CancellationToken ct)
    {
        var matches = await db.Set<MessageTemplate>().AsNoTracking()
            .Where(t => t.Code == code && t.Channel == channel && (t.Language == language || t.Language == "en"))
            .ToListAsync(ct);
        return matches.FirstOrDefault(t => t.Language == language) ?? matches.FirstOrDefault(t => t.Language == "en");
    }

    public Task<OutboundMessage?> FindByDedupeKey(string dedupeKey, CancellationToken ct) =>
        db.Set<OutboundMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.DedupeKey == dedupeKey, ct);

    public async Task Add(OutboundMessage message, CancellationToken ct) => await db.Set<OutboundMessage>().AddAsync(message, ct);

    public Task<List<OutboundMessage>> Due(DateTimeOffset now, int take, CancellationToken ct) =>
        db.Set<OutboundMessage>()
            .Where(m => m.Status == MessageStatus.Queued && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt).Take(take).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

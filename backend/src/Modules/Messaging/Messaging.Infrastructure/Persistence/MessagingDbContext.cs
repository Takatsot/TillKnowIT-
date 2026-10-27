using Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Messaging.Infrastructure.Persistence;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public const string Schema = "messaging";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<MessageTemplate>(e =>
        {
            e.ToTable("message_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Language).HasMaxLength(5).IsRequired();
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Code, x.Language, x.Channel }).IsUnique();
            e.HasData(MessagingSeed.Rows());
        });

        b.Entity<OutboundMessage>(e =>
        {
            e.ToTable("outbound_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Destination).HasMaxLength(320).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.TemplateCode).HasMaxLength(100).IsRequired();
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.Property(x => x.ProviderRef).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // The same dedupe key can only ever be queued once, even if two workers race.
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });

        b.AddOutbox();
    }
}

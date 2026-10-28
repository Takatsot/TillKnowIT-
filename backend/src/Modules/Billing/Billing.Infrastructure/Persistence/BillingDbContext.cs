using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Billing.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        b.AddOutbox();
    }
}

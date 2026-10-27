using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        b.AddOutbox();
    }
}

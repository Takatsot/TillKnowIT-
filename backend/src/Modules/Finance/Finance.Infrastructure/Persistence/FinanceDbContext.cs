using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
        b.AddOutbox();
    }
}

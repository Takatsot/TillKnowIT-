using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Accounting.Infrastructure.Persistence;

public sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options) : DbContext(options)
{
    public const string Schema = "accounting";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(AccountingDbContext).Assembly);
        b.AddOutbox();
    }
}

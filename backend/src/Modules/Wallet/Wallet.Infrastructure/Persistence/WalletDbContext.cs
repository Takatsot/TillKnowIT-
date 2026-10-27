using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Wallet.Infrastructure.Persistence;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public const string Schema = "wallet";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
        b.AddOutbox();
    }
}

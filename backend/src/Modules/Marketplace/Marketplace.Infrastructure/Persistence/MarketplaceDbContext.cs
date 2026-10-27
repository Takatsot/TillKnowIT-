using Marketplace.Domain;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Marketplace.Infrastructure.Persistence;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : DbContext(options)
{
    public const string Schema = "marketplace";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(MarketplaceDbContext).Assembly);
        b.AddOutbox();
    }
}

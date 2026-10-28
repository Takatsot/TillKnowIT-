using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Insights.Infrastructure.Persistence;

public sealed class InsightsDbContext(DbContextOptions<InsightsDbContext> options) : DbContext(options)
{
    public const string Schema = "insights";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(InsightsDbContext).Assembly);
        b.AddOutbox();
    }
}

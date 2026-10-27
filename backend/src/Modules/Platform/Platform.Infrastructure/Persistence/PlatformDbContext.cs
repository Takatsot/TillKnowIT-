using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
        b.AddOutbox();
    }
}

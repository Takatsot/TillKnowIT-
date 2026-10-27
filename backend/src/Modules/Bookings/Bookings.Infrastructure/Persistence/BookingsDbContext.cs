using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Bookings.Infrastructure.Persistence;

public sealed class BookingsDbContext(DbContextOptions<BookingsDbContext> options) : DbContext(options)
{
    public const string Schema = "bookings";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(BookingsDbContext).Assembly);
        b.AddOutbox();
    }
}

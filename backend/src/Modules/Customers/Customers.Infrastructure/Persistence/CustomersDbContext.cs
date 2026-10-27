using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Customers.Infrastructure.Persistence;

public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public const string Schema = "customers";

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(CustomersDbContext).Assembly);
        b.AddOutbox();
    }
}

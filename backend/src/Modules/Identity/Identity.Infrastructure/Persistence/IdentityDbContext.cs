using Identity.Domain;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<Person> People => Set<Person>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        b.AddOutbox();
    }
}

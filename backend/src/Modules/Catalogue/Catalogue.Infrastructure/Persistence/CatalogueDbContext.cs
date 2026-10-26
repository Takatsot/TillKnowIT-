using Catalogue.Domain;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace Catalogue.Infrastructure.Persistence;

public sealed class CatalogueDbContext(DbContextOptions<CatalogueDbContext> options) : DbContext(options)
{
    public const string Schema = "catalogue";

    public DbSet<CatalogueProduct> Products => Set<CatalogueProduct>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<CatalogueProduct>(e =>
        {
            e.ToTable("catalogue_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Barcode).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Size).HasMaxLength(50);
            e.Property(x => x.CategoryCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Barcode).IsUnique();
        });
        // Platform tables live in the "platform" schema but are owned by this module.
        b.Entity<Lookup>(e =>
        {
            e.ToTable("lookups", "platform");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(50).IsRequired();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Names).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Type, x.Code }).IsUnique();
            e.HasData(CatalogueSeed.Rows());
        });

        b.Entity<FeatureFlag>(e =>
        {
            e.ToTable("feature_flags", "platform");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Key).IsUnique();
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log", "platform");
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.DetailJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.AddOutbox();
    }
}

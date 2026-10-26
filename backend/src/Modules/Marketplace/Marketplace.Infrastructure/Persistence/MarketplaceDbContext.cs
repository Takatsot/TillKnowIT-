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

        b.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Whatsapp).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<SupplierProduct>(e =>
        {
            e.ToTable("supplier_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(50);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
            e.HasIndex(x => new { x.SupplierId, x.Barcode });
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId);
            e.HasIndex(x => x.ShopId);
        });

        b.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("purchase_order_lines");
            e.HasKey(x => new { x.PurchaseOrderId, x.SupplierProductId });
        });

        b.AddOutbox();
    }
}

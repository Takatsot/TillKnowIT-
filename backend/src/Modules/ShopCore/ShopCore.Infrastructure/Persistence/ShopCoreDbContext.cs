using Microsoft.EntityFrameworkCore;
using ShopCore.Domain;
using TillKnowIT.Outbox;

namespace ShopCore.Infrastructure.Persistence;

public sealed class ShopCoreDbContext(DbContextOptions<ShopCoreDbContext> options) : DbContext(options)
{
    public const string Schema = "shopcore";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(50);
            e.Property(x => x.CategoryCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ShopId, x.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        });

        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReasonCode).HasMaxLength(50);
            e.Property(x => x.SupplierName).HasMaxLength(200);
            e.Property(x => x.InvoiceRef).HasMaxLength(100);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
            e.HasIndex(x => new { x.ShopId, x.ProductId });
            e.HasIndex(x => x.SaleId).HasFilter("sale_id IS NOT NULL");
        });

        b.Entity<Sale>(e =>
        {
            e.ToTable("sales");
            e.HasKey(x => x.Id);
            e.Property(x => x.PaymentMethodCode).HasMaxLength(50).IsRequired();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SaleId);
            e.HasIndex(x => new { x.ShopId, x.SoldAt });
        });

        b.Entity<SaleLine>(e =>
        {
            e.ToTable("sale_lines");
            e.HasKey(x => new { x.SaleId, x.ProductId });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<SaleCancellation>(e =>
        {
            e.ToTable("sale_cancellations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId);
            e.HasIndex(x => x.SaleId).IsUnique();
        });

        b.Entity<SlipRequest>(e =>
        {
            e.ToTable("slip_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Destination).HasMaxLength(320);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId);
        });

        b.Entity<CreditAccount>(e =>
        {
            e.ToTable("credit_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.ShopId);
        });

        b.Entity<CreditEntry>(e =>
        {
            e.ToTable("credit_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PaymentMethodCode).HasMaxLength(50);
            e.HasIndex(x => new { x.SaleId, x.Type }).IsUnique().HasFilter("sale_id IS NOT NULL");
            e.HasOne<CreditAccount>().WithMany().HasForeignKey(x => x.AccountId);
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<CashUp>(e =>
        {
            e.ToTable("cash_ups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.ShopId, x.TillDeviceId, x.Day }).IsUnique();
        });

        b.Entity<ComplianceDocument>(e =>
        {
            e.ToTable("compliance_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.TypeCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.FileUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.ExpiresOn);
        });

        b.Entity<ShopSetting>(e =>
        {
            e.ToTable("shop_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Value).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ShopId, x.Key }).IsUnique();
        });

        b.Entity<InvoiceImport>(e =>
        {
            e.ToTable("invoice_imports");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SupplierName).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<ImportMapping>(e =>
        {
            e.ToTable("import_mappings");
            e.HasKey(x => x.Id);
            e.Property(x => x.SupplierKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
            e.Property(x => x.MappingJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ShopId, x.SupplierKey }).IsUnique();
        });

        b.Entity<VasSale>(e =>
        {
            e.ToTable("vas_sales");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PartnerRef).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20);
        });

        // Movements, cancellations and credit entries are a ledger: deleting a product, sale or account must
        // never erase them. Only a sale's own lines go with the sale.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys())
                     .Where(f => f.DeleteBehavior == DeleteBehavior.Cascade && f.DeclaringEntityType.ClrType != typeof(SaleLine)))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        b.AddOutbox();
    }
}

using Marketplace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence;

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> b)
    {
        b.ToTable("deals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.TermsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.AreaCodes).HasMaxLength(500).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Terms);
        b.Ignore(x => x.AreaList);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasIndex(x => new { x.SupplierId, x.StartsAt });
        b.HasIndex(x => new { x.IsCancelled, x.EndsAt });
    }
}

internal sealed class SharingSettingConfiguration : IEntityTypeConfiguration<SharingSetting>
{
    public void Configure(EntityTypeBuilder<SharingSetting> b)
    {
        b.ToTable("sharing_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ShopId).IsUnique();
    }
}

internal sealed class ShopSupplierConfiguration : IEntityTypeConfiguration<ShopSupplier>
{
    public void Configure(EntityTypeBuilder<ShopSupplier> b)
    {
        b.ToTable("shop_suppliers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasIndex(x => new { x.ShopId, x.SupplierId }).IsUnique();
    }
}

internal sealed class LastPriceConfiguration : IEntityTypeConfiguration<LastPrice>
{
    public void Configure(EntityTypeBuilder<LastPrice> b)
    {
        b.ToTable("last_prices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Barcode).HasMaxLength(14).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasIndex(x => new { x.ShopId, x.SupplierId, x.Barcode }).IsUnique();
    }
}

internal sealed class MarketplaceSettingConfiguration : IEntityTypeConfiguration<MarketplaceSetting>
{
    public void Configure(EntityTypeBuilder<MarketplaceSetting> b)
    {
        b.ToTable("marketplace_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Value).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

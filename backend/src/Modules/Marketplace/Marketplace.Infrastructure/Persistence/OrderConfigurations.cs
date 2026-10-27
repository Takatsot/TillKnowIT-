using Marketplace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.SubtotalCents);
        b.Ignore(x => x.VatCents);
        b.Ignore(x => x.TotalCents);
        b.Ignore(x => x.BelowMinimum);
        b.Ignore(x => x.HasChangesFromOrder);
        b.Ignore(x => x.Reference);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.ShopId, x.CreatedAt });
        b.HasIndex(x => new { x.SupplierId, x.Status });
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Barcode).HasMaxLength(14);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Ignore(x => x.EffectiveCases);
    }
}

internal sealed class GroupDealConfiguration : IEntityTypeConfiguration<GroupDeal>
{
    public void Configure(EntityTypeBuilder<GroupDeal> b)
    {
        b.ToTable("group_deals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AreaCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.TotalCases);
        b.Ignore(x => x.ThresholdReached);
        b.HasMany(x => x.Commitments).WithOne().HasForeignKey(c => c.GroupDealId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Commitments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.AreaCode, x.Status });
        b.HasIndex(x => new { x.SupplierId, x.Status });
    }
}

internal sealed class GroupCommitmentConfiguration : IEntityTypeConfiguration<GroupCommitment>
{
    public void Configure(EntityTypeBuilder<GroupCommitment> b)
    {
        b.ToTable("group_commitments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.GroupDealId, x.ShopId }).IsUnique();
    }
}

internal sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        b.ToTable("ratings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Comment).HasMaxLength(500);
        b.HasIndex(x => x.OrderId).IsUnique();   // one rating per order
        b.HasIndex(x => x.SupplierId);
    }
}

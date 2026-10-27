using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopCore.Domain;
using ShopCore.Domain.Selling;

namespace ShopCore.Infrastructure.Persistence;

internal sealed class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> b)
    {
        b.ToTable("modifier_groups");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.ShopId);
    }
}

internal sealed class ModifierOptionRowConfiguration : IEntityTypeConfiguration<ModifierOptionRow>
{
    public void Configure(EntityTypeBuilder<ModifierOptionRow> b)
    {
        b.ToTable("modifier_options");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasIndex(x => new { x.GroupId, x.ProductId }).IsUnique();
    }
}

internal sealed class ProductModifierGroupConfiguration : IEntityTypeConfiguration<ProductModifierGroup>
{
    public void Configure(EntityTypeBuilder<ProductModifierGroup> b)
    {
        b.ToTable("product_modifier_groups");
        b.HasKey(x => x.Id);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne<ModifierGroup>().WithMany().HasForeignKey(x => x.GroupId);
        b.HasIndex(x => new { x.ProductId, x.GroupId }).IsUnique();
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("promotions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ProductIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.ItemsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.ShopId, x.IsActive });
    }
}

internal sealed class FloorTableConfiguration : IEntityTypeConfiguration<FloorTable>
{
    public void Configure(EntityTypeBuilder<FloorTable> b)
    {
        b.ToTable("floor_tables");
        b.HasKey(x => x.Id);
        b.Property(x => x.Area).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.ShopId, x.Area, x.Name }).IsUnique();
    }
}

internal sealed class OpenTicketConfiguration : IEntityTypeConfiguration<OpenTicket>
{
    public void Configure(EntityTypeBuilder<OpenTicket> b)
    {
        b.ToTable("open_tickets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Label).HasMaxLength(40).IsRequired();
        b.Property(x => x.LinesJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Lines);
        b.Ignore(x => x.TotalCents);
        b.HasIndex(x => new { x.ShopId, x.Status });
    }
}

internal sealed class KitchenOrderConfiguration : IEntityTypeConfiguration<KitchenOrder>
{
    public void Configure(EntityTypeBuilder<KitchenOrder> b)
    {
        b.ToTable("kitchen_orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.OrderNo).HasMaxLength(12).IsRequired();
        b.Property(x => x.TableLabel).HasMaxLength(40);
        b.Property(x => x.ItemsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Items);
        b.HasIndex(x => new { x.ShopId, x.OrderNo }).IsUnique();
        b.HasIndex(x => new { x.ShopId, x.Status, x.CreatedAt });
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.ToTable("quotes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200);
        b.Property(x => x.CustomerPhone).HasMaxLength(20);
        b.Property(x => x.LinesJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Lines);
        b.HasIndex(x => new { x.ShopId, x.Number }).IsUnique();
    }
}

internal sealed class LaybyPlanConfiguration : IEntityTypeConfiguration<LaybyPlan>
{
    public void Configure(EntityTypeBuilder<LaybyPlan> b)
    {
        b.ToTable("layby_plans");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(20).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CustomerPhone).HasMaxLength(20);
        b.Property(x => x.LinesJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.Lines);
        b.Ignore(x => x.PaidCents);
        b.Ignore(x => x.BalanceCents);
        b.Ignore(x => x.ReservesStock);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.ShopId, x.Reference }).IsUnique();
        b.HasIndex(x => new { x.Status, x.DueOn });
    }
}

internal sealed class LaybyPaymentConfiguration : IEntityTypeConfiguration<LaybyPayment>
{
    public void Configure(EntityTypeBuilder<LaybyPayment> b)
    {
        b.ToTable("layby_payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.MethodCode).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.DeviceId, x.PaidAt });
    }
}

internal sealed class GiftVoucherConfiguration : IEntityTypeConfiguration<GiftVoucher>
{
    public void Configure(EntityTypeBuilder<GiftVoucher> b)
    {
        b.ToTable("gift_vouchers");
        b.HasKey(x => x.Id);
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CodeLast4).HasMaxLength(4).IsRequired();
        b.Property(x => x.PaidWithMethod).HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.BalanceCents);
        b.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.VoucherId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.ShopId, x.CodeHash }).IsUnique();
    }
}

internal sealed class VoucherEntryConfiguration : IEntityTypeConfiguration<VoucherEntry>
{
    public void Configure(EntityTypeBuilder<VoucherEntry> b)
    {
        b.ToTable("voucher_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.VoucherId, x.Reference, x.Type });
    }
}

internal sealed class SerialNumberRecordConfiguration : IEntityTypeConfiguration<SerialNumberRecord>
{
    public void Configure(EntityTypeBuilder<SerialNumberRecord> b)
    {
        b.ToTable("serial_numbers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Serial).HasMaxLength(50).IsRequired();
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasIndex(x => new { x.ShopId, x.ProductId, x.Serial }).IsUnique();   // a serial is sold once
        b.HasIndex(x => x.SaleId);
    }
}

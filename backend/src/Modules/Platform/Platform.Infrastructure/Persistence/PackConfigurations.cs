using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain;
using TillKnowIT.SharedKernel;

namespace Platform.Infrastructure.Persistence;

/// <summary>The packs every environment starts with. A platform admin can edit them, and add more, in the running system.</summary>
internal static class PackSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] Everyone = [FeatureFlags.SplitPayments, FeatureFlags.Refunds, FeatureFlags.Customers, FeatureFlags.Printer, FeatureFlags.CashDrawer, FeatureFlags.MessageBundles];

    private static readonly (string Code, string Name, string Description, string[] Flags, Dictionary<string, string> Settings)[] Packs =
    [
        ("spaza", "Spaza shop or general dealer", "Sell, count stock, run a credit book and top up airtime. The simplest till.", [], []),
        ("cafe", "Cafe, restaurant or takeaway", "Tables, open tickets, a kitchen screen, add-ons, tips and promotions.",
            [.. Everyone, FeatureFlags.OpenTickets, FeatureFlags.Tables, FeatureFlags.Kitchen, FeatureFlags.Tips, FeatureFlags.Modifiers, FeatureFlags.Promotions, FeatureFlags.Loyalty, FeatureFlags.Vouchers], []),
        ("butchery", "Butchery, deli or fresh produce", "Sell by weight from a scale and scan the labels it prints.",
            [.. Everyone, FeatureFlags.Weight, FeatureFlags.PriceEmbeddedBarcodes, FeatureFlags.Scale, FeatureFlags.Promotions],
            new() { [FeatureFlags.PriceEmbeddedBarcodes] = "{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\",\"decimals\":3}" }),
        ("salon", "Salon, barber or services", "Services with a duration, appointments, reminders and staff commission.",
            [.. Everyone, FeatureFlags.Appointments, FeatureFlags.Commission, FeatureFlags.Tips, FeatureFlags.Loyalty, FeatureFlags.Vouchers], []),
        ("clothing_hardware", "Clothing or hardware shop", "Sizes and colours, quotes, lay-by and serial numbers.",
            [.. Everyone, FeatureFlags.Variants, FeatureFlags.Quotes, FeatureFlags.Layby, FeatureFlags.SerialNumbers, FeatureFlags.Promotions, FeatureFlags.Loyalty, FeatureFlags.Vouchers], []),
    ];

    public static object[] PackRows() => Packs.Select(p => (object)new
    {
        Id = DeterministicGuid.From($"platform.pack:{p.Code}"), Code = p.Code, Name = p.Name, Description = (string?)p.Description,
        SlipTemplateCode = "slip.sale", IsActive = true, Version = 1, CreatedAt = SeededAt,
    }).ToArray();

    public static object[] FeatureRows() => Packs.SelectMany(p => p.Flags.Distinct().Select(flag => (object)new
    {
        Id = DeterministicGuid.From($"platform.feature:{p.Code}:{flag}"), PackId = DeterministicGuid.From($"platform.pack:{p.Code}"), Flag = flag,
        SettingsJson = p.Settings.GetValueOrDefault(flag), CreatedAt = SeededAt,
    })).ToArray();
}

internal sealed class BusinessPackConfiguration : IEntityTypeConfiguration<BusinessPack>
{
    public void Configure(EntityTypeBuilder<BusinessPack> b)
    {
        b.ToTable("business_packs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.SlipTemplateCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasMany(x => x.Features).WithOne().HasForeignKey(f => f.PackId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Features).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasData(PackSeed.PackRows());
    }
}

internal sealed class PackFeatureConfiguration : IEntityTypeConfiguration<PackFeature>
{
    public void Configure(EntityTypeBuilder<PackFeature> b)
    {
        b.ToTable("pack_features");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Flag).HasMaxLength(40).IsRequired();
        b.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.PackId, x.Flag }).IsUnique();
        b.HasData(PackSeed.FeatureRows());
    }
}

internal sealed class ShopPackConfiguration : IEntityTypeConfiguration<ShopPack>
{
    public void Configure(EntityTypeBuilder<ShopPack> b)
    {
        b.ToTable("shop_packs");
        b.HasKey(x => x.Id);
        b.Property(x => x.PackCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ShopId).IsUnique();
    }
}

internal sealed class ShopPackChangeConfiguration : IEntityTypeConfiguration<ShopPackChange>
{
    public void Configure(EntityTypeBuilder<ShopPackChange> b)
    {
        b.ToTable("shop_pack_changes");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromCode).HasMaxLength(30);
        b.Property(x => x.ToCode).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.ShopId, x.CreatedAt });
    }
}

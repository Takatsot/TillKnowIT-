using Billing.Contracts;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TillKnowIT.SharedKernel;

namespace Billing.Infrastructure.Persistence;

/// <summary>
/// The plans every environment starts with. The prices are placeholders until the pilot pricing decision is made: a platform admin changes them in the
/// running system. The free plans are what a business falls back to when a subscription lapses.
/// </summary>
internal static class PlanSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly (string Code, string Audience, string Name, long Price, (string Feature, long? Limit)[] Features)[] Plans =
    [
        ("shop_free", "shop", "Free", 0, [(PlanFeatures.Messages, 50)]),
        ("shop_pro", "shop", "Pro", 19_900, [(PlanFeatures.Messages, 500), (PlanFeatures.MultiBranch, null), (PlanFeatures.AccountingExport, null), (PlanFeatures.InsightReports, null), (PlanFeatures.PayLater, null)]),
        ("supplier_free", "supplier", "Free", 0, []),
        ("supplier_plus", "supplier", "Plus", 29_900, [(PlanFeatures.SupplierAnalytics, null), (PlanFeatures.InsightReports, null)]),
        ("supplier_premium", "supplier", "Premium", 99_900, [(PlanFeatures.SupplierAnalytics, null), (PlanFeatures.InsightReports, null), (PlanFeatures.Sponsored, null), (PlanFeatures.PartnerApi, null)]),
    ];

    private static Guid PlanId(string code) => DeterministicGuid.From($"billing.plan:{code}");

    public static object[] PlanRows() => Plans.Select(p => (object)new
    {
        Id = PlanId(p.Code), Code = p.Code, Audience = p.Audience, Name = p.Name, PriceCents = p.Price, IsActive = true, Version = 1, CreatedAt = SeededAt,
    }).ToArray();

    public static object[] FeatureRows() => Plans.SelectMany(p => p.Features.Select(f => (object)new
    {
        Id = DeterministicGuid.From($"billing.feature:{p.Code}:{f.Feature}"), PlanId = PlanId(p.Code), Feature = f.Feature, Limit = f.Limit, CreatedAt = SeededAt,
    })).ToArray();
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("plans");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Audience).HasMaxLength(10).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsFree);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(PlanSeed.PlanRows());
    }
}

internal sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> b)
    {
        b.ToTable("plan_features");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Feature).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.PlanId, x.Feature }).IsUnique();
        b.HasData(PlanSeed.FeatureRows());
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("subscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PlanCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.GivesPaidFeatures);
        b.HasIndex(x => x.OrganisationId).IsUnique();
        b.HasIndex(x => new { x.Status, x.PeriodEnd });
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.OrganisationId, x.CreatedAt });
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("invoice_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.Property(x => x.Metric).HasMaxLength(40);
        b.HasIndex(x => x.InvoiceId);
    }
}

internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> b)
    {
        b.ToTable("usage_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Metric).HasMaxLength(40).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.OrganisationId, x.Reference }).IsUnique();
        b.HasIndex(x => new { x.OrganisationId, x.InvoiceId, x.OccurredAt });
    }
}

internal sealed class SponsoredCampaignConfiguration : IEntityTypeConfiguration<SponsoredCampaign>
{
    public void Configure(EntityTypeBuilder<SponsoredCampaign> b)
    {
        b.ToTable("sponsored_campaigns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Areas).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.AreaList);
        b.HasIndex(x => x.SupplierOrganisationId);
        b.HasIndex(x => new { x.Status, x.StartsOn, x.EndsOn });
    }
}

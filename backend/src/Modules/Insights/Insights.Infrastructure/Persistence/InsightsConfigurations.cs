using Insights.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Insights.Infrastructure.Persistence;

internal sealed class InsightOptInConfiguration : IEntityTypeConfiguration<InsightOptIn>
{
    public void Configure(EntityTypeBuilder<InsightOptIn> b)
    {
        b.ToTable("insight_opt_ins");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ShopId).IsUnique();
    }
}

internal sealed class AreaAggregateConfiguration : IEntityTypeConfiguration<AreaAggregate>
{
    public void Configure(EntityTypeBuilder<AreaAggregate> b)
    {
        b.ToTable("daily_area_aggregates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Area).HasMaxLength(20).IsRequired();
        b.Property(x => x.Category).HasMaxLength(50).IsRequired();
        // The table itself refuses a figure from fewer than ten shops, as a second line of defence behind the code.
        b.ToTable(t => t.HasCheckConstraint("ck_daily_area_aggregates_min_shops", "[shop_count] >= 10"));
        b.HasIndex(x => new { x.Area, x.Category, x.Day }).IsUnique();
    }
}

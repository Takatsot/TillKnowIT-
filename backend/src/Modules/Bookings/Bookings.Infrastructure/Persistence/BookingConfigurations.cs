using Bookings.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Infrastructure.Persistence;

internal sealed class ServiceItemConfiguration : IEntityTypeConfiguration<ServiceItem>
{
    public void Configure(EntityTypeBuilder<ServiceItem> b)
    {
        b.ToTable("services");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ShopId);
    }
}

internal sealed class StaffServiceConfiguration : IEntityTypeConfiguration<StaffService>
{
    public void Configure(EntityTypeBuilder<StaffService> b)
    {
        b.ToTable("staff_services");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ShopId, x.ServiceId, x.StaffId }).IsUnique();
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ServiceName).HasMaxLength(100).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CustomerPhone).HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.BlocksTime);
        b.HasIndex(x => new { x.ShopId, x.StaffId, x.StartsAt });
        b.HasIndex(x => new { x.ShopId, x.StartsAt });
        b.HasIndex(x => new { x.Status, x.StartsAt });
    }
}

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> b)
    {
        b.ToTable("commission_rules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.ShopId, x.StaffId, x.ServiceId }).IsUnique();
    }
}

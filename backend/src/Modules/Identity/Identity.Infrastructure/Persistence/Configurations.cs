using Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("people");
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.PreferredLanguage).HasMaxLength(5).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Phone).IsUnique();
    }
}

internal sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> b)
    {
        b.ToTable("organisations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PinHash).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId);
        b.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId);
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId);
        b.HasIndex(x => new { x.PersonId, x.OrganisationId }).IsUnique();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Names).HasColumnType("jsonb");
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(IdentitySeed.RoleRows());
        b.OwnsMany(x => x.Permissions, p =>
        {
            p.ToTable("role_permissions");
            p.WithOwner().HasForeignKey(x => x.RoleId);
            p.HasKey(x => new { x.RoleId, x.PermissionCode });
            p.Property(x => x.PermissionCode).HasMaxLength(100);
            p.HasData(IdentitySeed.PermissionRows());
        });
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("devices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Organisation>().WithMany().HasForeignKey(x => x.ShopId);
    }
}

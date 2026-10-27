using Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customers.Infrastructure.Persistence;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.ConsentSource).HasMaxLength(40);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsMerged);
        // One live customer per phone number in a shop; a merged record gives its number up.
        b.HasIndex(x => new { x.ShopId, x.Phone }).IsUnique().HasFilter("[merged_into_id] IS NULL");
    }
}

internal sealed class LoyaltyEntryConfiguration : IEntityTypeConfiguration<LoyaltyEntry>
{
    public void Configure(EntityTypeBuilder<LoyaltyEntry> b)
    {
        b.ToTable("loyalty_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.CustomerId, x.Reference }).IsUnique();   // a repeated event can never earn twice
        b.HasIndex(x => x.SaleId).HasFilter("[sale_id] IS NOT NULL");
    }
}

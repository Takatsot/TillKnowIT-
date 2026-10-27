using Marketplace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RegistrationNo).HasMaxLength(30);
        b.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Whatsapp).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.ChecklistDone).HasMaxLength(500);
        b.Property(x => x.PaymentTerms).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.OrganisationId).IsUnique().HasFilter("organisation_id IS NOT NULL");
        b.HasIndex(x => x.Status);
    }
}

internal sealed class SupplierAreaConfiguration : IEntityTypeConfiguration<SupplierArea>
{
    public void Configure(EntityTypeBuilder<SupplierArea> b)
    {
        b.ToTable("supplier_areas");
        b.HasKey(x => x.Id);
        b.Property(x => x.MunicipalityCode).HasMaxLength(20).IsRequired();
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasIndex(x => new { x.SupplierId, x.MunicipalityCode }).IsUnique();
        b.HasIndex(x => x.MunicipalityCode);
    }
}

internal sealed class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> b)
    {
        b.ToTable("supplier_products");
        b.HasKey(x => x.Id);
        b.Property(x => x.Barcode).HasMaxLength(14);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ExpiryPolicy).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.UnitPriceCents);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        b.HasIndex(x => new { x.SupplierId, x.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        b.HasIndex(x => x.CatalogueProductId);
    }
}

using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence;

internal sealed class CreditConsentConfiguration : IEntityTypeConfiguration<CreditConsent>
{
    public void Configure(EntityTypeBuilder<CreditConsent> b)
    {
        b.ToTable("credit_consents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PartnerCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsActive);
        // One live consent per shop and partner.
        b.HasIndex(x => new { x.ShopId, x.PartnerCode }).IsUnique().HasFilter("[withdrawn_at] IS NULL");
    }
}

internal sealed class DataShareRecordConfiguration : IEntityTypeConfiguration<DataShareRecord>
{
    public void Configure(EntityTypeBuilder<DataShareRecord> b)
    {
        b.ToTable("data_shares");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PartnerCode).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.ShopId, x.CreatedAt });
    }
}

internal sealed class CreditApplicationConfiguration : IEntityTypeConfiguration<CreditApplication>
{
    public void Configure(EntityTypeBuilder<CreditApplication> b)
    {
        b.ToTable("credit_applications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PartnerCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.PartnerReference).HasMaxLength(100);
        b.Property(x => x.DecisionReason).HasMaxLength(300);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.ShopId);
    }
}

internal sealed class CreditDrawConfiguration : IEntityTypeConfiguration<CreditDraw>
{
    public void Configure(EntityTypeBuilder<CreditDraw> b)
    {
        b.ToTable("credit_draws");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
        b.Property(x => x.PartnerReference).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.OutstandingCents);
        b.Ignore(x => x.OutstandingPrincipalCents);
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => x.PartnerReference);
        b.HasIndex(x => x.ShopId);
        b.HasIndex(x => x.ApplicationId);
    }
}

internal sealed class CreditRepaymentConfiguration : IEntityTypeConfiguration<CreditRepayment>
{
    public void Configure(EntityTypeBuilder<CreditRepayment> b)
    {
        b.ToTable("credit_repayments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.DrawId);
    }
}

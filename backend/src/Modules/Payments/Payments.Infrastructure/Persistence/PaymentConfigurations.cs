using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PartnerRef).HasMaxLength(100);
        b.Property(x => x.CheckoutUrl).HasMaxLength(1000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.AmountToRelease);
        b.HasMany(x => x.Audit).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Audit).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.PartnerRef).IsUnique().HasFilter("[partner_ref] IS NOT NULL");
        b.HasIndex(x => new { x.Status, x.ReleaseDueAt });
    }
}

internal sealed class PaymentAuditConfiguration : IEntityTypeConfiguration<PaymentAudit>
{
    public void Configure(EntityTypeBuilder<PaymentAudit> b)
    {
        b.ToTable("payment_audit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    }
}

internal sealed class WebhookReceiptConfiguration : IEntityTypeConfiguration<WebhookReceipt>
{
    public void Configure(EntityTypeBuilder<WebhookReceipt> b)
    {
        b.ToTable("webhook_receipts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Partner).HasMaxLength(50).IsRequired();
        b.Property(x => x.EventId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Type).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.Partner, x.EventId }).IsUnique();
    }
}

internal sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("disputes");
        b.HasKey(x => x.Id);
        b.Property(x => x.RaisedBy).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.OrderId, x.Status });
        b.HasIndex(x => x.Status);
    }
}

internal sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> b)
    {
        b.ToTable("reconciliation_runs");
        b.HasKey(x => x.Id);
        b.Property(x => x.MismatchesJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x => x.Day);
    }
}

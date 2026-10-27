using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Persistence;

internal sealed class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    public void Configure(EntityTypeBuilder<School> b)
    {
        b.ToTable("schools");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.EmisNumber).HasMaxLength(12);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PayoutSchedule).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsTrading);
        b.HasIndex(x => x.ShopId).IsUnique();
    }
}

internal sealed class SchoolAdminConfiguration : IEntityTypeConfiguration<SchoolAdmin>
{
    public void Configure(EntityTypeBuilder<SchoolAdmin> b)
    {
        b.ToTable("school_admins");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SchoolId, x.PersonId }).IsUnique();
    }
}

internal sealed class HolderConfiguration : IEntityTypeConfiguration<Holder>
{
    public void Configure(EntityTypeBuilder<Holder> b)
    {
        b.ToTable("wallet_holders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        b.Property(x => x.Grade).HasMaxLength(20);
        b.Property(x => x.ClassName).HasMaxLength(20);
        b.Property(x => x.PhotoUrl).HasMaxLength(300);
        b.Property(x => x.LinkCodeHash).HasMaxLength(64);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.DisplayName);
        b.Ignore(x => x.Key);
        b.HasIndex(x => x.SchoolId);
        b.HasIndex(x => x.LinkCodeHash).IsUnique().HasFilter("[link_code_hash] IS NOT NULL");
    }
}

internal sealed class HolderTokenConfiguration : IEntityTypeConfiguration<HolderToken>
{
    public void Configure(EntityTypeBuilder<HolderToken> b)
    {
        b.ToTable("holder_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();   // a card belongs to one person
        b.HasIndex(x => x.HolderId);
    }
}

internal sealed class GuardianshipConfiguration : IEntityTypeConfiguration<Guardianship>
{
    public void Configure(EntityTypeBuilder<Guardianship> b)
    {
        b.ToTable("guardianships");
        b.HasKey(x => x.Id);
        b.Property(x => x.ConsentVersion).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.ParentPersonId, x.HolderId }).IsUnique();
        b.HasIndex(x => x.HolderId);
    }
}

internal sealed class SchoolWalletConfiguration : IEntityTypeConfiguration<SchoolWallet>
{
    public void Configure(EntityTypeBuilder<SchoolWallet> b)
    {
        b.ToTable("wallets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Version).IsConcurrencyToken();   // every debit moves it, so two simultaneous spends cannot both win
        b.Ignore(x => x.AccountId);
        b.HasIndex(x => x.HolderId).IsUnique();
        b.HasIndex(x => x.SchoolId);
    }
}

internal sealed class WalletRestrictionConfiguration : IEntityTypeConfiguration<WalletRestriction>
{
    public void Configure(EntityTypeBuilder<WalletRestriction> b)
    {
        b.ToTable("wallet_restrictions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.WalletId);
    }
}

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<LedgerAccount> b)
    {
        b.ToTable("ledger_accounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.HasData(
            new { Id = AccountIds.PartnerFloat, Type = LedgerAccountType.PartnerFloat, CreatedAt = Seeded },
            new { Id = AccountIds.Fees, Type = LedgerAccountType.Fees, CreatedAt = Seeded });
    }
}

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> b)
    {
        b.ToTable("ledger_transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.PartnerRef).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.Reference).IsUnique();   // a repeated request can never move money twice
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries", t => t.HasCheckConstraint("ck_ledger_entries_nonzero", "[amount_cents] <> 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => x.AccountId);
        b.HasIndex(x => x.TransactionId);
    }
}

internal sealed class WalletPurchaseConfiguration : IEntityTypeConfiguration<WalletPurchase>
{
    public void Configure(EntityTypeBuilder<WalletPurchase> b)
    {
        b.ToTable("wallet_purchases");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ItemsJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasIndex(x => new { x.WalletId, x.SoldAt });
        b.HasIndex(x => new { x.ShopId, x.SoldAt });
    }
}

internal sealed class OfflineSaleRecordConfiguration : IEntityTypeConfiguration<OfflineSaleRecord>
{
    public void Configure(EntityTypeBuilder<OfflineSaleRecord> b)
    {
        b.ToTable("offline_sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(100);
        b.HasIndex(x => new { x.WalletId, x.SoldAt });
    }
}

internal sealed class TopUpConfiguration : IEntityTypeConfiguration<TopUp>
{
    public void Configure(EntityTypeBuilder<TopUp> b)
    {
        b.ToTable("top_ups");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PartnerRef).HasMaxLength(100);
        b.Property(x => x.CheckoutUrl).HasMaxLength(1000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.PartnerRef).IsUnique().HasFilter("[partner_ref] IS NOT NULL");
    }
}

internal sealed class WalletWebhookReceiptConfiguration : IEntityTypeConfiguration<WalletWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<WalletWebhookReceipt> b)
    {
        b.ToTable("webhook_receipts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Partner).HasMaxLength(50).IsRequired();
        b.Property(x => x.EventId).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.Partner, x.EventId }).IsUnique();
    }
}

internal sealed class BreakSlotConfiguration : IEntityTypeConfiguration<BreakSlot>
{
    public void Configure(EntityTypeBuilder<BreakSlot> b)
    {
        b.ToTable("break_slots");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.SchoolId, x.Code }).IsUnique();
    }
}

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        b.ToTable("menu_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.ProductId).HasMaxLength(100);
        b.Property(x => x.CategoryCode).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.SchoolId);
    }
}

internal sealed class PreOrderConfiguration : IEntityTypeConfiguration<PreOrder>
{
    public void Configure(EntityTypeBuilder<PreOrder> b)
    {
        b.ToTable("pre_orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.BreakCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LinesJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.SchoolId, x.CollectOn });
        b.HasIndex(x => x.HolderId);
    }
}

internal sealed class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    public void Configure(EntityTypeBuilder<RefundRequest> b)
    {
        b.ToTable("refund_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PartnerRef).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.WalletId);
    }
}

internal sealed class ShopPayoutConfiguration : IEntityTypeConfiguration<ShopPayout>
{
    public void Configure(EntityTypeBuilder<ShopPayout> b)
    {
        b.ToTable("shop_payouts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PartnerRef).HasMaxLength(100);
        b.Property(x => x.Error).HasMaxLength(500);
        b.HasIndex(x => new { x.ShopId, x.Day });
    }
}

internal sealed class WalletReconciliationConfiguration : IEntityTypeConfiguration<WalletReconciliation>
{
    public void Configure(EntityTypeBuilder<WalletReconciliation> b)
    {
        b.ToTable("reconciliations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Day);
        b.HasIndex(x => x.Status);
    }
}

internal sealed class ErasureRequestConfiguration : IEntityTypeConfiguration<ErasureRequest>
{
    public void Configure(EntityTypeBuilder<ErasureRequest> b)
    {
        b.ToTable("erasure_requests");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.HolderId).IsUnique();
    }
}

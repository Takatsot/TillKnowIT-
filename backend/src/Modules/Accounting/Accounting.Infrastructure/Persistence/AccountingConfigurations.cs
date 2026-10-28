using Accounting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence;

internal sealed class AccountingConnectionConfiguration : IEntityTypeConfiguration<AccountingConnection>
{
    public void Configure(EntityTypeBuilder<AccountingConnection> b)
    {
        b.ToTable("accounting_connections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        b.Property(x => x.TokenReference).HasMaxLength(200).IsRequired();   // a reference to the secrets store, never the token
        b.Property(x => x.TenantName).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.IsConnected);
        b.HasIndex(x => x.ShopId).IsUnique();
    }
}

internal sealed class AccountMappingConfiguration : IEntityTypeConfiguration<AccountMapping>
{
    public void Configure(EntityTypeBuilder<AccountMapping> b)
    {
        b.ToTable("account_mappings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Key).HasMaxLength(30).IsRequired();
        b.Property(x => x.AccountCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.ShopId, x.Key }).IsUnique();
    }
}

internal sealed class ExportRunConfiguration : IEntityTypeConfiguration<ExportRun>
{
    public void Configure(EntityTypeBuilder<ExportRun> b)
    {
        b.ToTable("export_runs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LastError).HasMaxLength(300);
        b.Property(x => x.ExternalReference).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.GivenUp);
        b.HasIndex(x => new { x.ShopId, x.Provider, x.Day }).IsUnique();
    }
}

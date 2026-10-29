using System.Security.Cryptography;
using System.Text.Json;
using Accounting.Application;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure;

/// <summary>One package's tokens for one shop, encrypted. The blob is nonce (12) + tag (16) + ciphertext, AES-256-GCM.</summary>
public sealed class ProviderToken
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = default!;
    public byte[] Blob { get; set; } = default!;
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ProviderTokenConfiguration : IEntityTypeConfiguration<ProviderToken>
{
    public void Configure(EntityTypeBuilder<ProviderToken> b)
    {
        b.ToTable("provider_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        b.Property(x => x.Blob).HasMaxLength(4000).IsRequired();
    }
}

/// <summary>Keeps tokens in the accounting schema, encrypted with a key from configuration (Accounting:TokenKey, 32 bytes as base64). A step up from plain text, not a replacement for a secrets store.</summary>
public sealed class EncryptedProviderTokenVault(AccountingDbContext db, byte[] key, TimeProvider clock) : IProviderTokenVault
{
    private const string Prefix = "vault:";

    public async Task<string> Store(string provider, OAuthTokens tokens, CancellationToken ct)
    {
        var row = new ProviderToken { Id = Guid.CreateVersion7(), Provider = provider, Blob = Encrypt(tokens), UpdatedAt = clock.GetUtcNow() };
        db.Set<ProviderToken>().Add(row);
        await db.SaveChangesAsync(ct);
        return Prefix + row.Id.ToString("N");
    }

    public async Task<OAuthTokens?> Load(string reference, CancellationToken ct)
    {
        var row = await Find(reference, ct);
        return row is null ? null : Decrypt(row.Blob);
    }

    public async Task Replace(string reference, OAuthTokens tokens, CancellationToken ct)
    {
        var row = await Find(reference, ct) ?? throw new InvalidOperationException("Token reference not found.");
        row.Blob = Encrypt(tokens);
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task<ProviderToken?> Find(string reference, CancellationToken ct) =>
        reference.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(reference[Prefix.Length..], "N", out var id) ? await db.Set<ProviderToken>().FirstOrDefaultAsync(t => t.Id == id, ct) : null;

    private byte[] Encrypt(OAuthTokens tokens) => TokenCipher.Encrypt(key, tokens);

    private OAuthTokens? Decrypt(byte[] blob) => TokenCipher.Decrypt(key, blob);
}

/// <summary>AES-256-GCM over the token set as JSON. A wrong key or a changed blob gives null, never garbage.</summary>
public static class TokenCipher
{
    public static byte[] Encrypt(byte[] key, OAuthTokens tokens)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    public static OAuthTokens? Decrypt(byte[] key, byte[] blob)
    {
        if (blob.Length < 28) return null;
        var plain = new byte[blob.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        return JsonSerializer.Deserialize<OAuthTokens>(plain);
    }
}

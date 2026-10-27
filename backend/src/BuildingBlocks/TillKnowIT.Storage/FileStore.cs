using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Storage;

public sealed record StoredFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Where uploaded files live: licences, invoice photos, learner photos. The URL it returns is opaque to the caller.
/// Production uses Azure Blob Storage with encryption at rest; development and tests use <see cref="EncryptedLocalFileStore"/>.
/// </summary>
public interface IFileStore
{
    Task<string> Save(string container, string fileName, string contentType, byte[] content, CancellationToken ct);

    /// <summary>Null when the file does not exist or has been tampered with.</summary>
    Task<StoredFile?> Open(string url, CancellationToken ct);

    Task Delete(string url, CancellationToken ct);
}

public static class FileRules
{
    public const int MaxBytes = 10 * 1024 * 1024;

    public static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "application/pdf"];

    /// <summary>The file must be a photo or PDF under 10 MB, and its first bytes must match what it says it is.</summary>
    public static Result Validate(string? fileName, string? contentType, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200)
            return Result.Fail("file.name_invalid", "The file needs a name (up to 200 characters).");
        if (content.Length == 0)
            return Result.Fail("file.empty", "The file is empty.");
        if (content.Length > MaxBytes)
            return Result.Fail("file.too_large", "The file is larger than 10 MB.");
        if (contentType is null || !AllowedTypes.Contains(contentType))
            return Result.Fail("file.type_invalid", "Only JPEG, PNG and PDF files are accepted.");
        if (!MatchesSignature(contentType, content))
            return Result.Fail("file.content_mismatch", "The file is not really a " + contentType + ".");
        return Result.Ok();
    }

    private static bool MatchesSignature(string contentType, byte[] c) => contentType switch
    {
        "image/jpeg" => c.Length > 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF,
        "image/png" => c.Length > 8 && c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47,
        "application/pdf" => c.Length > 4 && c[0] == 0x25 && c[1] == 0x50 && c[2] == 0x44 && c[3] == 0x46,
        _ => false,
    };
}

/// <summary>
/// Files on local disk, encrypted with AES-256-GCM. Each file has its own random nonce, and the file name and type are
/// inside the encrypted part. Any change to the stored bytes makes <see cref="Open"/> return null.
/// File names on disk are random ids, so a caller-supplied name can never reach outside the storage folder.
/// </summary>
public sealed class EncryptedLocalFileStore : IFileStore
{
    private const string Scheme = "tkfile://";
    private readonly string _root;
    private readonly byte[] _key;

    public EncryptedLocalFileStore(string root, byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("The storage key must be 32 bytes.", nameof(key));
        _root = Path.GetFullPath(root);
        _key = key;
    }

    public async Task<string> Save(string container, string fileName, string contentType, byte[] content, CancellationToken ct)
    {
        var id = Guid.CreateVersion7().ToString("N");
        var path = PathFor(container, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var meta = JsonSerializer.SerializeToUtf8Bytes(new Meta(fileName, contentType));
        var plain = new byte[4 + meta.Length + content.Length];
        BitConverter.TryWriteBytes(plain.AsSpan(0, 4), meta.Length);
        meta.CopyTo(plain, 4);
        content.CopyTo(plain, 4 + meta.Length);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(_key, 16)) gcm.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(container));

        await File.WriteAllBytesAsync(path, [.. nonce, .. tag, .. cipher], ct);
        return $"{Scheme}{container}/{id}";
    }

    public async Task<StoredFile?> Open(string url, CancellationToken ct)
    {
        if (!TryParse(url, out var container, out var id)) return null;
        var path = PathFor(container, id);
        if (!File.Exists(path)) return null;

        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length < 28) return null;
        try
        {
            var plain = new byte[bytes.Length - 28];
            using (var gcm = new AesGcm(_key, 16))
                gcm.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(container));

            var metaLength = BitConverter.ToInt32(plain, 0);
            var meta = JsonSerializer.Deserialize<Meta>(plain.AsSpan(4, metaLength));
            return meta is null ? null : new StoredFile(plain[(4 + metaLength)..], meta.ContentType, meta.FileName);
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    public Task Delete(string url, CancellationToken ct)
    {
        if (TryParse(url, out var container, out var id))
        {
            var path = PathFor(container, id);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string PathFor(string container, string id) => Path.Combine(_root, container, id + ".bin");

    /// <summary>Containers are short lowercase names and ids are 32 hex characters, so nothing else can reach the disk.</summary>
    private static bool TryParse(string url, out string container, out string id)
    {
        container = id = "";
        if (!url.StartsWith(Scheme, StringComparison.Ordinal)) return false;
        var parts = url[Scheme.Length..].Split('/');
        if (parts.Length != 2) return false;
        if (parts[0].Length is 0 or > 40 || !parts[0].All(c => c is >= 'a' and <= 'z' or '-')) return false;
        if (parts[1].Length != 32 || !parts[1].All(Uri.IsHexDigit)) return false;
        (container, id) = (parts[0], parts[1]);
        return true;
    }

    private sealed record Meta(string FileName, string ContentType);
}

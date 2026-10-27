using Microsoft.Extensions.DependencyInjection;

namespace TillKnowIT.Storage;

public static class StorageExtensions
{
    /// <summary>
    /// Registers encrypted local-disk storage. The key is 32 random bytes in base64 and comes from configuration
    /// (Key Vault in staging and production). Azure Blob Storage replaces this class behind the same interface.
    /// </summary>
    public static IServiceCollection AddEncryptedLocalFileStore(this IServiceCollection services, string? root, string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Storage:Root is not set.");
        if (string.IsNullOrWhiteSpace(base64Key)) throw new InvalidOperationException("Storage:Key is not set.");

        byte[] key;
        try { key = Convert.FromBase64String(base64Key); }
        catch (FormatException) { throw new InvalidOperationException("Storage:Key must be base64."); }
        if (key.Length != 32) throw new InvalidOperationException("Storage:Key must be 32 bytes (44 base64 characters).");

        services.AddSingleton<IFileStore>(new EncryptedLocalFileStore(root, key));
        return services;
    }
}

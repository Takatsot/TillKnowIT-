using System.Security.Cryptography;
using System.Text;

namespace TillKnowIT.SharedKernel;

public static class DeterministicGuid
{
    /// <summary>Stable id for seed rows, so the same key gives the same id in every environment.</summary>
    public static Guid From(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key))[..16]);
}

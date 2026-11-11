using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAuth.Core;

namespace LocalAuth.App;

// This cache is deliberately limited to names, types, IDs and favorites.
// Passwords, OTP seeds and vault keys never enter the automatically opened index.
internal sealed record PrivateEntryInfo
{
    public Guid Id { get; init; }
    public string Issuer { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "TOTP";
    public bool Favorite { get; init; }

    internal Account ToLockedAccount() => new() { Id = Id, Issuer = Issuer, Name = Name, Kind = Kind, Favorite = Favorite };
}

internal sealed record PrivateEntryIndex
{
    public int Version { get; init; } = 1;
    public string VaultFingerprint { get; init; } = "";
    public List<PrivateEntryInfo> Entries { get; init; } = [];

    internal static PrivateEntryIndex From(IEnumerable<Account> accounts) => new()
    {
        Entries = accounts.Select(a => new PrivateEntryInfo { Id = a.Id, Issuer = a.Issuer, Name = a.Name, Kind = a.Kind, Favorite = a.Favorite }).ToList()
    };

    internal void Validate()
    {
        if (Version != 1 || VaultFingerprint is null || VaultFingerprint.Length != 64 || Entries is null || Entries.Count > 10000 ||
            Entries.Any(e => e is null || e.Id == Guid.Empty || e.Issuer is null || e.Name is null ||
                e.Issuer.Length > 120 || e.Name.Length > 200 || (e.Issuer.Length == 0 && e.Name.Length == 0) || e.Kind is not ("TOTP" or "Password")) ||
            Entries.Select(e => e.Id).Distinct().Count() != Entries.Count)
            throw new FormatException("首页私密列表无效，请解锁后重新同步。");
    }
}

internal sealed class PrivateEntryIndexStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalAuth|private-index|v1");
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal string FilePath { get; } = Path.GetFullPath(path);
    internal static string Fingerprint(byte[] vaultBytes) => Convert.ToHexString(SHA256.HashData(vaultBytes));

    internal PrivateEntryIndex? TryRead(VaultStore vault)
    {
        if (!vault.Exists || !File.Exists(FilePath)) return null;
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(VaultStore.ReadBounded(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var index = JsonSerializer.Deserialize<PrivateEntryIndex>(plaintext, Options);
            if (index is null) return null;
            index.Validate();
            return index.VaultFingerprint == Fingerprint(vault.Read()) ? index : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or JsonException) { return null; }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal void Save(PrivateEntryIndex index)
    {
        index.Validate();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(index, Options);
        try
        {
            var bytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            if (bytes.Length > VaultCipher.MaxFileSize) throw new FormatException("首页私密列表过大。");
            VaultStore.WriteAtomically(FilePath, bytes);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}

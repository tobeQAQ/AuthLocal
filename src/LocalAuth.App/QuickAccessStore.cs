using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAuth.Core;

namespace LocalAuth.App;

// Windows opens this store for the signed-in user; the private vault's key is
// never persisted here. No rolling backup: moving an entry to the private vault
// must not leave an automatically accessible copy in an older ordinary file.
internal sealed class QuickAccessStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalAuth|ordinary|v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal string FilePath { get; } = Path.GetFullPath(path);

    internal VaultDocument Read()
    {
        if (!File.Exists(FilePath)) return new VaultDocument();
        var plaintext = ProtectedData.Unprotect(VaultStore.ReadBounded(FilePath), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<VaultDocument>(plaintext, JsonOptions)?.Validate()
                ?? throw new FormatException("普通区数据为空。");
        }
        catch (JsonException) { throw new FormatException("普通区文件格式无效。"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal void Save(VaultDocument document)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document.Validate(), JsonOptions);
        try
        {
            var bytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            if (bytes.Length > VaultCipher.MaxFileSize) throw new FormatException("普通区数据过大，请减少条目。");
            VaultStore.WriteAtomically(FilePath, bytes);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}

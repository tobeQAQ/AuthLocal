using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalAuth.Core;

public static class VaultCipher
{
    internal const int DefaultIterations = 600_000;
    internal static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("LocalAuth|vault|v1");
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public const int MaxFileSize = 16 * 1024 * 1024;

    public static OpenVault Create(string password, VaultDocument? document = null)
    {
        ValidateNewPassword(password);
        var salt = RandomNumberGenerator.GetBytes(32);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, 32);
        return new OpenVault(document?.Validate() ?? new VaultDocument(), key, salt, DefaultIterations);
    }

    public static OpenVault Open(byte[] encrypted, string password)
    {
        if (encrypted.Length > MaxFileSize) throw new FormatException("文件过大，无法作为备份打开。");
        if (password.Length > 1024) throw new FormatException("密码过长。");
        byte[]? key = null;
        byte[]? plaintext = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(encrypted, JsonOptions) ?? throw new FormatException("备份文件为空。");
            if (envelope.Format != "LocalAuth" || envelope.Version != 1 || envelope.Kdf != "PBKDF2-SHA256" ||
                envelope.Iterations < 100_000 || envelope.Iterations > 2_000_000)
                throw new FormatException("这不是受支持的 LocalAuth 加密备份。");
            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            if (salt.Length != 32 || nonce.Length != 12 || tag.Length != 16)
                throw new FormatException("加密文件格式不完整。");
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
            plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            var document = JsonSerializer.Deserialize<VaultDocument>(plaintext, JsonOptions)?.Validate()
                ?? throw new FormatException("账户数据为空。");
            var vault = new OpenVault(document, key, salt, envelope.Iterations);
            key = null;
            return vault;
        }
        catch (JsonException) { throw new FormatException("文件格式无效，无法读取备份。"); }
        catch (ArgumentException) { throw new FormatException("加密文件字段无效。"); }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static void ValidateNewPassword(string password)
    {
        if (password.Length < 5) throw new FormatException("主密码至少需要 5 个字符（支持纯数字）。");
        if (password.Length > 1024) throw new FormatException("主密码最多支持 1024 个字符。");
    }

    internal sealed record Envelope
    {
        public string Format { get; init; } = "LocalAuth";
        public int Version { get; init; } = 1;
        public string Kdf { get; init; } = "PBKDF2-SHA256";
        public int Iterations { get; init; }
        public string Salt { get; init; } = "";
        public string Nonce { get; init; } = "";
        public string Ciphertext { get; init; } = "";
        public string Tag { get; init; } = "";
    }
}

public sealed class OpenVault : IDisposable
{
    private readonly byte[] _key;
    private readonly byte[] _salt;
    private readonly int _iterations;
    private bool _disposed;
    public VaultDocument Document { get; set; }

    internal OpenVault(VaultDocument document, byte[] key, byte[] salt, int iterations)
    {
        Document = document;
        _key = key;
        _salt = salt;
        _iterations = iterations;
    }

    public byte[] Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(Document.Validate(), VaultCipher.JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using (var aes = new AesGcm(_key, 16)) aes.Encrypt(nonce, plaintext, ciphertext, tag, VaultCipher.AssociatedData);
            return JsonSerializer.SerializeToUtf8Bytes(new VaultCipher.Envelope
            {
                Iterations = _iterations,
                Salt = Convert.ToBase64String(_salt),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            }, VaultCipher.JsonOptions);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        CryptographicOperations.ZeroMemory(_salt);
        Document = new VaultDocument();
        _disposed = true;
    }
}

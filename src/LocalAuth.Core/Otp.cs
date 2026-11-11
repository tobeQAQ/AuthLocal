using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LocalAuth.Core;

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 4096)
            throw new FormatException("请输入有效的 Base32 密钥。");
        var normalized = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        var firstPadding = normalized.IndexOf('=');
        if (firstPadding >= 0)
        {
            if (normalized[firstPadding..].Any(c => c != '=')) throw new FormatException("密钥的填充位置无效。");
            normalized = normalized[..firstPadding];
        }
        if (normalized.Length == 0 || normalized.Any(c => Alphabet.IndexOf(c) < 0))
            throw new FormatException("密钥应只包含字母 A–Z 和数字 2–7；也可以直接粘贴 otpauth 链接。");
        if (normalized.Length % 8 is 1 or 3 or 6) throw new FormatException("密钥长度无效，请检查是否复制完整。");
        return normalized;
    }

    public static byte[] Decode(string input)
    {
        var value = Normalize(input);
        var bytes = new byte[value.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var c in value)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes[index++] = (byte)(buffer >> bits);
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0 && buffer != 0) throw new FormatException("密钥尾部无效，请检查是否复制完整。");
        if (bytes.Length < 10) throw new FormatException("密钥至少需要 80 位，请检查是否复制完整。");
        return bytes;
    }

    public static string Encode(ReadOnlySpan<byte> input)
    {
        var output = new StringBuilder((input.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in input)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(buffer >> bits) & 31]);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
}

public static class Totp
{
    public static string Generate(Account account, DateTimeOffset time)
    {
        if (account.Period < 5 || account.Period > 300 || account.Digits is not (6 or 8))
            throw new ArgumentException("验证码参数无效。");
        var seconds = time.ToUnixTimeSeconds();
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(time));
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, seconds / account.Period);
        var secret = Base32.Decode(account.Secret);
        byte[] digest;
        try
        {
            digest = account.Algorithm switch
            {
                "SHA1" => HMACSHA1.HashData(secret, counter),
                "SHA256" => HMACSHA256.HashData(secret, counter),
                "SHA512" => HMACSHA512.HashData(secret, counter),
                _ => throw new ArgumentException("不支持的验证码算法。")
            };
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
        try
        {
            var offset = digest[^1] & 0x0f;
            var number = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(offset, 4)) & 0x7fffffff;
            return (number % (account.Digits == 8 ? 100_000_000 : 1_000_000)).ToString($"D{account.Digits}");
        }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    public static int Remaining(Account account, DateTimeOffset time) =>
        account.Period - (int)(time.ToUnixTimeSeconds() % account.Period);
}

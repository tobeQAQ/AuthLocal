using System.Security.Cryptography;

namespace LocalAuth.Core;

public sealed record Account
{
    public string Kind { get; init; } = "TOTP";
    public bool IsPassword => Kind == "Password";
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Issuer { get; init; } = "";
    public string Name { get; init; } = "";
    public string Secret { get; init; } = "";
    public string Algorithm { get; init; } = "SHA1";
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;
    public bool Favorite { get; init; }

    public Account Normalize()
    {
        if (Issuer is null || Name is null || Secret is null || Algorithm is null)
            throw new FormatException("账户数据缺少必要字段。");
        if (Kind is not ("TOTP" or "Password")) throw new FormatException("不支持的条目类型。");
        if (Id == Guid.Empty) throw new FormatException("账户标识无效。");
        var issuer = Issuer.Trim();
        var name = Name.Trim();
        if (issuer.Length == 0 && name.Length == 0) throw new FormatException("请填写服务名称或账户名称。");
        if (issuer.Length > 120 || name.Length > 200) throw new FormatException("名称过长，请缩短后重试。");
        if (IsPassword)
        {
            if (Secret.Length == 0 || Secret.Length > 16384 || Secret.Contains('\0')) throw new FormatException("密码不能为空、含空字符或超过 16384 个字符。");
            return this with { Issuer = issuer, Name = name };
        }
        if (Digits is not (6 or 8)) throw new FormatException("验证码位数只支持 6 位或 8 位。");
        if (Period < 5 || Period > 300) throw new FormatException("刷新周期应为 5 至 300 秒。");
        var algorithm = Algorithm.ToUpperInvariant().Replace("-", "");
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512"))
            throw new FormatException("只支持 SHA1、SHA256 和 SHA512 算法。");
        var normalizedSecret = Base32.Normalize(Secret);
        var bytes = Base32.Decode(normalizedSecret);
        CryptographicOperations.ZeroMemory(bytes);
        return this with { Issuer = issuer, Name = name, Secret = normalizedSecret, Algorithm = algorithm };
    }

    public bool SameCredential(Account other) =>
        Kind == other.Kind && (!IsPassword || (Issuer == other.Issuer && Name == other.Name)) && Secret == other.Secret && Algorithm == other.Algorithm && Digits == other.Digits && Period == other.Period;
}

public sealed record VaultDocument
{
    public int Version { get; init; } = 1;
    public List<Account> Accounts { get; init; } = [];

    public VaultDocument Validate()
    {
        if (Version is not (1 or 2)) throw new FormatException("此数据版本暂不支持。");
        if (Accounts is null || Accounts.Count > 10000) throw new FormatException("账户数据无效或数量过多。");
        if (Accounts.Any(a => a is null)) throw new FormatException("账户数据无效。");
        var accounts = Accounts.Select(a => a.Normalize()).ToList();
        if (accounts.Select(a => a.Id).Distinct().Count() != accounts.Count)
            throw new FormatException("账户标识重复，无法打开数据。");
        return this with { Accounts = accounts, Version = accounts.Any(a => a.IsPassword) ? 2 : Version };
    }
}

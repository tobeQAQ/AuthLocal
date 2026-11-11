namespace LocalAuth.Core;

public static class OtpAuth
{
    public static Account Parse(string input)
    {
        if (input.Length > 16384 || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("请输入有效的 otpauth://totp/ 链接。");
        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("当前仅支持按时间变化的 TOTP 验证码，暂不支持 HOTP。");
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            if (!parameters.TryAdd(key, pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : ""))
                throw new FormatException("链接中存在重复参数，请检查来源。");
        }
        if (!parameters.TryGetValue("secret", out var secret)) throw new FormatException("链接中缺少 secret 密钥。");
        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var colon = label.IndexOf(':');
        var issuerInLabel = colon >= 0 ? label[..colon].Trim() : "";
        var issuer = parameters.GetValueOrDefault("issuer", issuerInLabel);
        if (issuerInLabel.Length > 0 && issuer.Length > 0 && issuerInLabel != issuer)
            throw new FormatException("链接中的服务名称不一致，请检查二维码来源。");
        var name = colon >= 0 ? label[(colon + 1)..].Trim() : label;
        return new Account
        {
            Issuer = issuer,
            Name = name,
            Secret = secret,
            Algorithm = parameters.GetValueOrDefault("algorithm", "SHA1"),
            Digits = ParseNumber(parameters, "digits", 6),
            Period = ParseNumber(parameters, "period", 30)
        }.Normalize();
    }

    private static int ParseNumber(Dictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var text)) return fallback;
        if (!int.TryParse(text, out var number)) throw new FormatException($"链接中的 {key} 参数无效。");
        return number;
    }
}

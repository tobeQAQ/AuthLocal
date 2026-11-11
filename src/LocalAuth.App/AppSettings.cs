using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed record AppSettings
{
    public int AutoLockMinutes { get; init; } = 5;
    public string ThemeMode { get; init; } = "System";
    public string Language { get; init; } = "System";
    public int DisclaimerVersion { get; init; }
    internal bool NeedsDisclaimer => DisclaimerVersion < 1;
    private static string FilePath => Path.Combine(Program.DataDirectory, "settings.json");
    public static AppSettings Load(string? path = null)
    {
        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? FilePath));
            if (settings is not null && settings.AutoLockMinutes is 1 or 5 or 15 &&
                settings.ThemeMode is "System" or "Light" or "Dark" &&
                settings.Language is "System" or "zh-CN" or "zh-TW" or "en" or "fr" or "ja") return settings;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }
    public void Save(string? path = null) => VaultStore.WriteAtomically(path ?? FilePath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this));
}

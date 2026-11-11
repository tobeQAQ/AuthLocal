using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalAuth.App;

internal static class UiText
{
    internal static readonly string[] LanguageCodes = ["System", "zh-CN", "zh-TW", "en", "fr", "ja"];
    internal static string LanguageCode { get; private set; } = "zh-CN";
    private static Dictionary<string, string> _strings = [];

    internal static string ResolveLanguage(string preference, string? systemLanguage = null)
    {
        var language = preference == "System" ? systemLanguage ?? CultureInfo.CurrentUICulture.Name : preference;
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return language.Contains("TW", StringComparison.OrdinalIgnoreCase) || language.Contains("HK", StringComparison.OrdinalIgnoreCase) || language.Contains("Hant", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        if (language.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
        if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        return "en";
    }

    internal static void UseLanguage(string preference, string? systemLanguage = null)
    {
        LanguageCode = ResolveLanguage(preference, systemLanguage);
        if (LanguageCode == "zh-CN") { _strings = []; return; }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"LocalAuth.App.Locales.{LanguageCode}.json");
        _strings = stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }

    internal static string T(string source)
    {
        if (_strings.TryGetValue(source, out var translated)) return translated;
        if (source.Contains('\n')) return string.Join('\n', source.Split('\n').Select(T));
        var invalidParameter = Regex.Match(source, "^链接中的 (.+) 参数无效。$");
        if (invalidParameter.Success) return F("链接中的 {0} 参数无效。", invalidParameter.Groups[1].Value);
        return source;
    }

    internal static string F(string template, params object?[] values) =>
        string.Format(CultureInfo.GetCultureInfo(LanguageCode), T(template), values);

    internal static void Apply(Control root)
    {
        // Never translate user-entered text, including passwords and account names.
        if (root is not TextBoxBase) root.Text = T(root.Text);
        if (root is TextBox input) input.PlaceholderText = T(input.PlaceholderText);
        if (root.AccessibleName is { } accessible) root.AccessibleName = T(accessible);
        if (root is ComboBox combo && !Equals(combo.Tag, "LanguageSelector"))
        {
            var selected = combo.SelectedIndex;
            for (var i = 0; i < combo.Items.Count; i++) if (combo.Items[i] is string item) combo.Items[i] = T(item);
            combo.SelectedIndex = selected;
        }
        if (root is ToolStrip strip) ApplyItems(strip.Items);
        foreach (Control child in root.Controls) Apply(child);
    }

    private static void ApplyItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.Text = T(item.Text ?? "");
            if (item is ToolStripDropDownItem dropdown) ApplyItems(dropdown.DropDownItems);
        }
    }

    internal static DialogResult Message(IWin32Window owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) =>
        MessageBox.Show(owner, T(text), T(caption), buttons, icon, defaultButton);

    internal static DialogResult Message(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None) => MessageBox.Show(T(text), T(caption), buttons, icon);
}

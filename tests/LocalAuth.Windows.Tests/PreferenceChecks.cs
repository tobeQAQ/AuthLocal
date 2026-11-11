using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

internal static partial class Program
{
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name, AllInstance)!.GetValue(value)!;
    private static object? Static(Type type, string name, params object?[] arguments) => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, arguments);
    private static System.Windows.Forms.Timer WhenDialog(string type, Action<Form> action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 40 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == type);
            if (dialog is null) return;
            timer.Stop(); action(dialog);
        };
        timer.Start(); return timer;
    }

    private static async Task RunPreferenceChecks()
    {
        var settingsType = App.GetType("LocalAuth.App.AppSettings")!;
        var defaults = Activator.CreateInstance(settingsType, true)!;
        Check(Property<string>(defaults, "ThemeMode") == "System" && Property<string>(defaults, "Language") == "System", "appearance and language default to system");
        Check(Property<bool>(defaults, "NeedsDisclaimer"), "fresh settings require first-use warning");
        var theme = App.GetType("LocalAuth.App.Theme")!;
        bool Dark() => (bool)theme.GetProperty("IsDark", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Static(theme, "SetMode", "System", true);
        Check(Dark(), "system preference follows dark mode");
        Static(theme, "SetMode", "System", false);
        Check(!Dark(), "system preference follows light mode");
        Static(theme, "SetMode", "Dark", false);
        Check(Dark(), "manual dark choice overrides light system");
        Static(theme, "SetMode", "Light", true);
        Check(!Dark(), "manual light choice overrides dark system");

        var text = App.GetType("LocalAuth.App.UiText")!;
        Check((string)Static(text, "ResolveLanguage", "System", "zh-Hant-HK")! == "zh-TW", "system Traditional Chinese is recognized");
        Check((string)Static(text, "ResolveLanguage", "System", "fr-FR")! == "fr", "system French is recognized");
        Check((string)Static(text, "ResolveLanguage", "System", "ja-JP")! == "ja", "system Japanese is recognized");
        Check((string)Static(text, "ResolveLanguage", "System", "ru-RU")! == "en", "unsupported system language falls back to English");
        Check((string)Static(text, "ResolveLanguage", "zh-CN", "fr-FR")! == "zh-CN", "manual language overrides system language");
        foreach (var (language, save) in new[] { ("zh-CN", "保存"), ("en", "Save"), ("fr", "Enregistrer"), ("ja", "保存") })
        {
            Static(text, "UseLanguage", language, null);
            Check((string)Static(text, "T", "保存")! == save, "embedded UI translation works: " + language);
            var type = App.GetType("LocalAuth.App.StoredPasswordDialog")!;
            var original = new LocalAuth.Core.Account { Kind = "Password", Issuer = "保存", Name = "设置", Secret = "保存" };
            using var dialog = (Form)Activator.CreateInstance(type, Flags, null, new object?[] { original, false }, null)!;
            dialog.Show();
            var inputs = dialog.Controls.OfType<TextBox>().Select(i => i.Text).ToArray();
            Check(inputs.SequenceEqual(new[] { "保存", "设置", "保存" }), "language change never translates user credentials: " + language);
            dialog.Close();
        }
        Static(text, "UseLanguage", "zh-TW", null);
        Check((string)Static(text, "T", "设置")! != "设置", "Traditional Chinese resource is embedded");
        Static(text, "UseLanguage", "en", null);

        var directory = Path.Combine(Temporary, "preferences"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var gate = App.GetType("LocalAuth.App.FirstRunGate")!;
        using (var cancel = WhenDialog("FirstRunDialog", dialog =>
        {
            Check(!((Control)dialog.AcceptButton!).Enabled, "first-use warning cannot continue before acknowledgement");
            dialog.DialogResult = DialogResult.Cancel;
        }))
            Check(!(bool)Static(gate, "Ensure", path)!, "declining first-use warning blocks startup");
        Check(!File.Exists(path), "declining warning does not persist acknowledgement");
        using (var accept = WhenDialog("FirstRunDialog", dialog =>
        {
            Get<CheckBox>(dialog, "_acknowledge").Checked = true;
            dialog.AcceptButton!.PerformClick();
        }))
            Check((bool)Static(gate, "Ensure", path)!, "acknowledging first-use warning permits startup");
        var saved = Static(settingsType, "Load", path)!;
        Check(Property<int>(saved, "DisclaimerVersion") == 1, "first-use acknowledgement persists");
        var repeated = false;
        using (var preventHang = WhenDialog("FirstRunDialog", dialog => { repeated = true; dialog.DialogResult = DialogResult.Cancel; }))
            Check((bool)Static(gate, "Ensure", path)! && !repeated, "accepted warning is not repeated on next startup");

        var form = CreateForm(directory); form.Show();
        using (var select = WhenDialog("SettingsDialog", dialog =>
        {
            Get<ComboBox>(dialog, "_appearance").SelectedIndex = 2;
            Get<ComboBox>(dialog, "_language").SelectedIndex = 4; // Français
            Get<ComboBox>(dialog, "_minutes").SelectedIndex = 2;
            dialog.AcceptButton!.PerformClick();
        })) await (Task)Call(form, "ConfigureAsync")!;
        saved = Static(settingsType, "Load", path)!;
        Check(Property<string>(saved, "ThemeMode") == "Dark" && Property<string>(saved, "Language") == "fr" && Property<int>(saved, "AutoLockMinutes") == 15,
            "actual settings dialog persists theme, language, and timeout");
        Check(Property<int>(saved, "DisclaimerVersion") == 1, "saving preferences preserves first-use acknowledgement");
        Check(Dark() && (string)Static(text, "T", "保存")! == "Enregistrer", "saving settings applies dark mode and language immediately");
        Exit(form);
        var reopened = CreateForm(directory); reopened.Show();
        Check(Dark() && (string)Static(text, "T", "保存")! == "Enregistrer", "theme and language survive restart");
        Exit(reopened);

        // Exercise the real registry-backed system appearance notification path,
        // then restore the test user's prior preference.
        const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        var previous = key.GetValue("AppsUseLightTheme");
        var kind = previous is null ? RegistryValueKind.DWord : key.GetValueKind("AppsUseLightTheme");
        var systemDirectory = Path.Combine(Temporary, "system-theme");
        var system = CreateForm(systemDirectory); system.Show();
        try
        {
            key.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
            Call(system, "OnSystemAppearanceChanged", system, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
            await Task.Delay(100);
            Check(Dark(), "system appearance notification changes live app to dark");
            key.SetValue("AppsUseLightTheme", 1, RegistryValueKind.DWord);
            Call(system, "OnSystemAppearanceChanged", system, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
            await Task.Delay(100);
            Check(!Dark(), "system appearance notification changes live app to light");
        }
        finally
        {
            if (previous is null) key.DeleteValue("AppsUseLightTheme", false);
            else key.SetValue("AppsUseLightTheme", previous, kind);
            Exit(system);
            Static(text, "UseLanguage", "System", null);
            Static(theme, "SetMode", "System", null);
        }
    }
}

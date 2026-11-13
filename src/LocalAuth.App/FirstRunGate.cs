namespace LocalAuth.App;

internal static class FirstRunGate
{
    internal static bool Ensure(string settingsPath)
    {
        var settings = AppSettings.Load(settingsPath);
        if (!settings.NeedsDisclaimer) return true;
        using var dialog = new FirstRunDialog();
        using var activation = ExistingInstance.Register(dialog, Path.GetDirectoryName(Path.GetFullPath(settingsPath))!, () => dialog.Activate());
        if (dialog.ShowDialog() != DialogResult.OK) return false;
        (AppSettings.Load(settingsPath) with { DisclaimerVersion = 1 }).Save(settingsPath);
        return true;
    }
}

internal sealed class FirstRunDialog : Form
{
    private readonly CheckBox _acknowledge;
    internal FirstRunDialog()
    {
        Theme.ConfigureDialog(this, "首次使用提醒", new Size(540, 390));
        Controls.Add(Theme.Label("首次使用提醒", 26, 18, 488, 38, 18, Theme.Warning, FontStyle.Bold));
        var warning = Theme.Label("请勿在此保存重要密码。", 26, 76, 488, 76, 16, style: FontStyle.Bold);
        warning.AutoEllipsis = false;
        Controls.Add(warning);
        Controls.Add(Theme.Label("请仅用于测试或低风险账户，并自行保留备份和找回方式。", 26, 158, 488, 58, 10, Theme.Muted));
        _acknowledge = new CheckBox { Text = "我已了解，不在此保存重要密码", Location = new Point(26, 232), Size = new Size(488, 48), Font = Theme.Font(10) };
        Controls.Add(_acknowledge);
        var exit = Theme.Button("退出", 270, 320, 110);
        exit.DialogResult = DialogResult.Cancel;
        var continueButton = Theme.Button("继续使用", 392, 320, 122, true);
        continueButton.Enabled = false;
        _acknowledge.CheckedChanged += (_, _) => continueButton.Enabled = _acknowledge.Checked;
        continueButton.Click += (_, _) => { if (_acknowledge.Checked) DialogResult = DialogResult.OK; };
        Controls.AddRange([exit, continueButton]);
        AcceptButton = continueButton;
        CancelButton = exit;
    }
}

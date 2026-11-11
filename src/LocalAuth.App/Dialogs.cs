using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed class PasswordDialog : Form
{
    private readonly TextBox _password;
    private readonly TextBox? _confirmation;
    internal string Password => _password.Text;

    internal PasswordDialog(string title, string help, bool confirm = false)
    {
        Theme.ConfigureDialog(this, title, new Size(460, confirm ? 370 : 290));
        Controls.Add(Theme.Label(title, 26, 18, 408, 34, 16, style: FontStyle.Bold));
        Controls.Add(Theme.Label(help, 26, 62, 408, 48, 9, Theme.Muted));
        _password = Theme.TextBox(26, 128, 408, true);
        _password.AccessibleName = "主密码";
        Controls.Add(_password);
        if (confirm)
        {
            Controls.Add(Theme.Label("再次输入新主密码", 26, 174, 408, 24, 9));
            _confirmation = Theme.TextBox(26, 205, 408, true);
            _confirmation.AccessibleName = "确认新主密码";
            Controls.Add(_confirmation);
        }
        var error = Theme.Label("", 26, confirm ? 246 : 174, 408, 42, 9, Color.Firebrick);
        Controls.Add(error);
        var cancel = Theme.Button("取消", 214, confirm ? 307 : 227, 102);
        cancel.DialogResult = DialogResult.Cancel;
        var accept = Theme.Button("确定", 328, confirm ? 307 : 227, 106, true);
        accept.Click += (_, _) =>
        {
            try
            {
                if (confirm)
                {
                    VaultCipher.ValidateNewPassword(_password.Text);
                    if (_password.Text != _confirmation!.Text) throw new FormatException("两次输入的密码不一致。");
                }
                else if (_password.Text.Length == 0) throw new FormatException("请输入密码。");
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { error.Text = UiText.T(ex.Message); }
        };
        Controls.Add(cancel);
        Controls.Add(accept);
        AcceptButton = accept;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _password.Clear(); _confirmation?.Clear(); }
        base.Dispose(disposing);
    }
}

internal sealed class EntryDialog : Form
{
    private readonly TextBox _issuer;
    private readonly TextBox _name;
    private readonly TextBox _secret;
    private readonly ComboBox _algorithm;
    private readonly ComboBox _digits;
    private readonly NumericUpDown _period;
    private readonly Label _error;
    private readonly Account? _original;
    private readonly CheckBox _protect;
    internal bool RequireUnlock => _protect.Checked;
    internal Account? Result { get; private set; }

    internal EntryDialog(Account? account = null, bool protect = false)
    {
        _original = account;
        Theme.ConfigureDialog(this, account is null ? "添加账户" : "编辑账户", new Size(560, 642));
        Controls.Add(Theme.Label(account is null ? "添加验证账户" : "编辑验证账户", 28, 18, 504, 36, 18, style: FontStyle.Bold));
        Controls.Add(Theme.Label("填写服务提供的设置密钥，或导入它的二维码图片。", 28, 62, 504, 30, 9, Theme.Muted));
        var qr = Theme.Button("导入二维码图片", 336, 102, 196);
        qr.Click += (_, _) => ReadQr();
        Controls.Add(qr);
        Controls.Add(Theme.Label("服务名称", 28, 112, 240, 26, 9));
        _issuer = Theme.TextBox(28, 148, 504);
        _issuer.MaxLength = 120;
        _issuer.PlaceholderText = "例如：Google、GitHub";
        Controls.Add(_issuer);
        Controls.Add(Theme.Label("账户名称", 28, 196, 504, 26, 9));
        _name = Theme.TextBox(28, 228, 504);
        _name.MaxLength = 200;
        _name.PlaceholderText = "例如：你的邮箱或用户名";
        Controls.Add(_name);
        Controls.Add(Theme.Label("密钥 / otpauth 链接", 28, 276, 504, 26, 9));
        _secret = Theme.TextBox(28, 308, 504, true);
        _secret.MaxLength = 16384;
        _secret.AccessibleName = "Base32 密钥或 otpauth 链接";
        _secret.Leave += (_, _) => ParseLink(silent: true);
        Controls.Add(_secret);
        var show = new CheckBox { Text = "显示密钥", Location = new Point(28, 352), AutoSize = true, Font = Theme.Font(9), ForeColor = Theme.Muted };
        show.CheckedChanged += (_, _) => _secret.UseSystemPasswordChar = !show.Checked;
        Controls.Add(show);
        var parse = Theme.Button("解析链接", 412, 348, 120);
        parse.Click += (_, _) => ParseLink(silent: false);
        Controls.Add(parse);
        Controls.Add(Theme.Label("算法", 28, 405, 150, 26, 9));
        Controls.Add(Theme.Label("位数", 202, 405, 150, 26, 9));
        Controls.Add(Theme.Label("刷新周期（秒）", 376, 405, 156, 26, 9));
        _algorithm = new ComboBox { Location = new Point(28, 437), Size = new Size(156, 32), DropDownStyle = ComboBoxStyle.DropDownList };
        _algorithm.Items.AddRange(["SHA1", "SHA256", "SHA512"]);
        _algorithm.SelectedIndex = 0;
        _digits = new ComboBox { Location = new Point(202, 437), Size = new Size(156, 32), DropDownStyle = ComboBoxStyle.DropDownList };
        _digits.Items.AddRange(["6", "8"]);
        _digits.SelectedIndex = 0;
        _period = new NumericUpDown { Location = new Point(376, 437), Size = new Size(156, 32), Minimum = 5, Maximum = 300, Value = 30 };
        Controls.AddRange([_algorithm, _digits, _period]);
        _error = Theme.Label("通常保持默认参数即可。", 28, 479, 504, 46, 9, Theme.Muted);
        Controls.Add(_error);
        _protect = new CheckBox { Text = "设为私密（首页显示名称，内容需密码）", Checked = protect, Location = new Point(28, 529), AutoSize = true, Font = Theme.Font(9) };
        Controls.Add(_protect);
        var cancel = Theme.Button("取消", 292, 580, 110);
        cancel.DialogResult = DialogResult.Cancel;
        var save = Theme.Button("保存账户", 414, 580, 118, true);
        save.Click += (_, _) => SaveEntry();
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        if (account is not null) Fill(account);
    }

    private void Fill(Account account)
    {
        _issuer.Text = account.Issuer;
        _name.Text = account.Name;
        _secret.Text = account.Secret;
        _algorithm.SelectedItem = account.Algorithm;
        _digits.SelectedItem = account.Digits.ToString();
        _period.Value = account.Period;
        _error.ForeColor = Theme.Accent;
        _error.Text = UiText.T("已读取账户信息，确认后点击保存。");
    }

    private bool ParseLink(bool silent)
    {
        if (!_secret.Text.TrimStart().StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
        {
            if (!silent) { _error.ForeColor = Color.Firebrick; _error.Text = UiText.T("请先在密钥框粘贴 otpauth://totp/ 链接。"); }
            return true;
        }
        try { Fill(OtpAuth.Parse(_secret.Text)); return true; }
        catch (FormatException ex) { _error.ForeColor = Color.Firebrick; _error.Text = UiText.T(ex.Message); return false; }
    }

    private void ReadQr()
    {
        using var picker = new OpenFileDialog { Title = UiText.T("选择验证器设置二维码"), Filter = UiText.T("二维码图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*") };
        if (picker.ShowDialog(this) != DialogResult.OK || IsDisposed) return;
        try
        {
            UseWaitCursor = true;
            Fill(OtpAuth.Parse(QrImport.Read(picker.FileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            _error.ForeColor = Color.Firebrick;
            _error.Text = UiText.T(ex is FormatException ? ex.Message : "无法读取这张图片，请尝试 PNG 或 JPG 格式。");
        }
        finally { UseWaitCursor = false; }
    }

    private void SaveEntry()
    {
        try
        {
            if (!ParseLink(silent: true)) return;
            Result = new Account
            {
                Id = _original?.Id ?? Guid.NewGuid(), Issuer = _issuer.Text, Name = _name.Text,
                Secret = _secret.Text, Algorithm = _algorithm.Text, Digits = int.Parse(_digits.Text),
                Period = (int)_period.Value, Favorite = _original?.Favorite ?? false
            }.Normalize();
            DialogResult = DialogResult.OK;
        }
        catch (FormatException ex) { _error.ForeColor = Color.Firebrick; _error.Text = UiText.T(ex.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _secret.Clear();
        base.Dispose(disposing);
    }
}

internal sealed class SettingsDialog : Form
{
    private readonly ComboBox _minutes;
    private readonly ComboBox _appearance;
    private readonly ComboBox _language;
    internal int AutoLockMinutes => _minutes.SelectedIndex switch { 0 => 1, 2 => 15, _ => 5 };
    internal string ThemeMode => _appearance.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
    internal string Language => UiText.LanguageCodes[Math.Max(0, _language.SelectedIndex)];
    internal bool ChangePassword { get; private set; }

    internal SettingsDialog(AppSettings settings)
    {
        Theme.ConfigureDialog(this, "设置", new Size(500, 522));
        Controls.Add(Theme.Label("设置", 26, 18, 448, 36, 18, style: FontStyle.Bold));
        Controls.Add(Theme.Label("主题", 26, 72, 448, 24));
        _appearance = new ComboBox { Location = new Point(26, 102), Size = new Size(448, 32), DropDownStyle = ComboBoxStyle.DropDownList };
        _appearance.Items.AddRange(["跟随系统", "浅色", "深色"]);
        _appearance.SelectedIndex = settings.ThemeMode switch { "Light" => 1, "Dark" => 2, _ => 0 };
        Controls.Add(_appearance);
        Controls.Add(Theme.Label("语言", 26, 150, 448, 24));
        _language = new ComboBox { Tag = "LanguageSelector", Location = new Point(26, 180), Size = new Size(448, 32), DropDownStyle = ComboBoxStyle.DropDownList };
        _language.Items.AddRange([UiText.T("跟随系统"), "简体中文", "繁體中文", "English", "Français", "日本語"]);
        _language.SelectedIndex = Math.Max(0, Array.IndexOf(UiText.LanguageCodes, settings.Language));
        Controls.Add(_language);
        Controls.Add(Theme.Label("无操作后自动锁定私密区", 26, 228, 448, 24));
        _minutes = new ComboBox { Location = new Point(26, 258), Size = new Size(448, 32), DropDownStyle = ComboBoxStyle.DropDownList };
        _minutes.Items.AddRange(["1 分钟", "5 分钟（默认）", "15 分钟"]);
        _minutes.SelectedIndex = settings.AutoLockMinutes switch { 1 => 0, 15 => 2, _ => 1 };
        Controls.Add(_minutes);
        Controls.Add(Theme.Label("锁屏或最小化会锁定私密内容；已复制的内容保持在剪贴板中，不会自动清空。", 26, 310, 448, 62, 9, Theme.Muted));
        var change = Theme.Button("设置 / 更改主密码", 26, 386, 448);
        change.Click += (_, _) => { ChangePassword = true; DialogResult = DialogResult.OK; };
        var save = Theme.Button("保存", 354, 460, 120, true);
        save.DialogResult = DialogResult.OK;
        Controls.AddRange([change, save]);
        AcceptButton = save;
    }
}

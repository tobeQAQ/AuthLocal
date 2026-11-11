using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed class StoredPasswordDialog : Form
{
    private readonly TextBox _secret;
    private readonly CheckBox _protect;
    internal bool RequireUnlock => _protect.Checked;
    internal Account? Result { get; private set; }

    internal StoredPasswordDialog(Account? original = null, bool protect = false)
    {
        Theme.ConfigureDialog(this, original is null ? "保存密码" : "编辑密码", new Size(520, 464));
        Controls.Add(Theme.Label("保存密码", 26, 18, 468, 36, 18, style: FontStyle.Bold));
        Controls.Add(Theme.Label("名称", 26, 70, 468, 24));
        var title = Theme.TextBox(26, 100, 468);
        title.MaxLength = 120;
        title.Text = original?.Issuer ?? "";
        title.PlaceholderText = "例如：邮箱、网站、Wi-Fi";
        Controls.Add(title);
        Controls.Add(Theme.Label("用户名（可选）", 26, 148, 468, 24));
        var username = Theme.TextBox(26, 178, 468);
        username.MaxLength = 200;
        username.Text = original?.Name ?? "";
        Controls.Add(username);
        Controls.Add(Theme.Label("密码", 26, 226, 468, 24));
        _secret = Theme.TextBox(26, 256, 468, true);
        _secret.MaxLength = 16384;
        _secret.Text = original?.Secret ?? "";
        Controls.Add(_secret);
        var show = new CheckBox { Text = "显示密码", Location = new Point(26, 300), AutoSize = true };
        show.CheckedChanged += (_, _) => _secret.UseSystemPasswordChar = !show.Checked;
        Controls.Add(show);
        var error = Theme.Label("", 26, 326, 468, 28, 9, Color.Firebrick);
        Controls.Add(error);
        _protect = new CheckBox { Text = "设为私密（首页显示名称，内容需密码）", Checked = protect, Location = new Point(26, 356), AutoSize = true, Font = Theme.Font(9) };
        Controls.Add(_protect);
        var cancel = Theme.Button("取消", 260, 410, 108);
        cancel.DialogResult = DialogResult.Cancel;
        var save = Theme.Button("保存", 382, 410, 112, true);
        save.Click += (_, _) =>
        {
            try
            {
                Result = (original ?? new Account()) with { Kind = "Password", Issuer = title.Text, Name = username.Text, Secret = _secret.Text };
                Result = Result.Normalize();
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { Result = null; error.Text = UiText.T(ex.Message); }
        };
        Controls.AddRange([cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => title.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _secret.Clear();
        base.Dispose(disposing);
    }
}

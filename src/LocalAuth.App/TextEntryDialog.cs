using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed class TextEntryDialog : Form
{
    private readonly TextBox _body;
    private readonly CheckBox _protect;
    internal bool RequireUnlock => _protect.Checked;
    internal Account? Result { get; private set; }

    internal TextEntryDialog(Account? original, bool protect)
    {
        Theme.ConfigureDialog(this, original is null ? "新建纯文本" : "编辑纯文本", new Size(820, 590));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(590, 385);
        Controls.Add(Theme.Label("名称", 24, 14, 760, 25));
        var title = Theme.TextBox(24, 42, 772);
        title.Text = original?.Issuer ?? "";
        title.MaxLength = 120;
        title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(title);
        Controls.Add(Theme.Label("正文", 24, 80, 760, 24));
        _body = Theme.TextBox(24, 108, 772);
        _body.MaxLength = Account.MaxTextLength;
        _body.Multiline = true;
        _body.ScrollBars = ScrollBars.Vertical;
        _body.AcceptsReturn = true;
        _body.AcceptsTab = true;
        _body.Size = new Size(772, 365);
        _body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _body.AccessibleName = "正文";
        _body.Text = original?.Secret ?? "";
        Controls.Add(_body);

        _protect = new CheckBox
        {
            Text = "设为私密", Checked = protect,
            Location = new Point(24, 482), Size = new Size(256, 25),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Font = Theme.Font(9)
        };
        var count = Theme.Label("", 552, 482, 244, 25, 9, Theme.Muted);
        count.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        count.TextAlign = ContentAlignment.MiddleRight;
        void UpdateCount() => count.Text = UiText.F("{0} / {1} 字符", _body.TextLength, Account.MaxTextLength);
        _body.TextChanged += (_, _) => UpdateCount();
        UpdateCount();
        var error = Theme.Label("", 24, 519, 510, 44, 9, Color.Firebrick);
        error.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        var cancel = Theme.Button("取消", 563, 519, 108);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        var save = Theme.Button("保存", 684, 519, 112, true);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Click += (_, _) =>
        {
            try
            {
                Result = ((original ?? new Account()) with { Kind = "Text", Issuer = title.Text, Secret = _body.Text }).Normalize();
                DialogResult = DialogResult.OK;
            }
            catch (FormatException ex) { error.Text = UiText.T(ex.Message); }
        };
        Controls.AddRange([_protect, count, error, cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { if (original is null) title.Focus(); else _body.Focus(); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != (Keys.Control | Keys.S)) return base.ProcessCmdKey(ref msg, keyData);
        AcceptButton?.PerformClick();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _body.Clear();
        base.Dispose(disposing);
    }
}

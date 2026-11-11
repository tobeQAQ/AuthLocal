using System.Drawing.Drawing2D;
using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed class CodeCard : Control
{
    internal Account Account { get; }
    private readonly bool _isPrivate;
    internal bool IsLocked { get; }
    internal event Action<Account>? CopyRequested;
    internal event Action<Account>? FavoriteRequested;
    internal event Action<Account, Point>? MenuRequested;
    private string _code = "";
    private int _remaining;
    private long _counter = -1;
    private bool _hover;
    private bool _showCopied;
    private long _copiedUntil;

    internal CodeCard(Account account, bool isPrivate = false, bool isLocked = false)
    {
        Account = account;
        _isPrivate = isPrivate;
        IsLocked = isLocked;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Background;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = UiText.F("{0} {1}，{2}", account.Issuer, account.Name, UiText.T(isLocked ? "已锁定，点击解锁并复制" : "点击复制内容"));
        Tick(DateTimeOffset.UtcNow);
    }

    internal void Tick(DateTimeOffset now)
    {
        if (_showCopied && Environment.TickCount64 >= _copiedUntil)
        {
            _showCopied = false;
            Invalidate();
        }
        if (IsLocked || Account.IsPassword) return;
        var remaining = Totp.Remaining(Account, now);
        var counter = now.ToUnixTimeSeconds() / Account.Period;
        var changed = counter != _counter;
        if (changed) { _counter = counter; _code = Totp.Generate(Account, now); }
        if (_remaining != remaining || changed) { _remaining = remaining; Invalidate(); }
    }

    internal void ShowCopied()
    {
        _showCopied = true;
        _copiedUntil = Environment.TickCount64 + 2_000;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        int S(int value) => (int)(value * scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), S(11));
        using var white = new SolidBrush(Theme.Surface);
        using var border = new Pen(_hover || Focused ? Theme.Accent : Theme.Border);
        g.FillPath(white, outline);
        g.DrawPath(border, outline);
        var initial = (Account.Issuer.Length > 0 ? Account.Issuer : Account.Name)[..1].ToUpperInvariant();
        using var iconPath = Theme.Rounded(new RectangleF(S(16), S(16), S(36), S(36)), S(11));
        using var iconBrush = new SolidBrush(Theme.AccentLight);
        g.FillPath(iconBrush, iconPath);
        using var iconFont = Theme.Font(14, FontStyle.Bold);
        TextRenderer.DrawText(g, initial, iconFont, new Rectangle(S(16), S(16), S(36), S(36)), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var titleFont = Theme.Font(11, FontStyle.Bold);
        using var detailFont = Theme.Font(8.5f);
        var nameWidth = Math.Max(S(30), Width - S(144));
        TextRenderer.DrawText(g, Account.Issuer.Length > 0 ? Account.Issuer : UiText.T("未命名服务"), titleFont,
            new Rectangle(S(64), S(12), nameWidth, S(25)), Theme.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, (_isPrivate ? UiText.T("[私密] ") : "") + Account.Name, detailFont,
            new Rectangle(S(64), S(37), nameWidth, S(23)), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        using var actionFont = Theme.Font(15);
        TextRenderer.DrawText(g, Account.Favorite ? "★" : "☆", actionFont,
            new Rectangle(Width - S(76), S(8), S(32), S(36)), Account.Favorite ? Color.FromArgb(184, 132, 33) : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "⋯", actionFont,
            new Rectangle(Width - S(40), S(8), S(32), S(36)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var codeFont = new Font(IsLocked ? "Microsoft YaHei UI" : "Consolas", IsLocked ? 15 : 26, FontStyle.Bold);
        var grouped = IsLocked ? "私密内容" : Account.IsPassword ? "••••••••" : _code.Insert(_code.Length / 2, " ");
        TextRenderer.DrawText(g, UiText.T(grouped), codeFont,
            new Rectangle(S(16), S(64), Width - S(112), S(43)), IsLocked ? Theme.Muted : !Account.IsPassword && _remaining <= 5 ? Theme.Warning : Theme.Accent,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        using var copyPath = Theme.Rounded(new RectangleF(Width - S(100), S(68), S(80), S(27)), S(6));
        g.FillPath(iconBrush, copyPath);
        TextRenderer.DrawText(g, UiText.T(_showCopied ? "已复制" : IsLocked ? "解锁复制" : "点击复制"), detailFont,
            new Rectangle(Width - S(100), S(68), S(80), S(27)), Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, IsLocked ? UiText.T("已锁定") : Account.IsPassword ? UiText.T(_isPrivate ? "私密密码" : "密码") : UiText.F("{0} 秒", _remaining), detailFont,
            new Rectangle(Width - S(170), S(98), S(147), S(19)), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        if (IsLocked || Account.IsPassword) return;
        using var track = new Pen(Theme.AccentLight, S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progress = new Pen(!Account.IsPassword && _remaining <= 5 ? Color.FromArgb(207, 159, 82) : Color.FromArgb(89, 161, 136), S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(track, S(18), Height - S(12), Width - S(18), Height - S(12));
        var progressWidth = (Width - S(36)) * ((float)_remaining / Account.Period);
        g.DrawLine(progress, S(18), Height - S(12), S(18) + progressWidth, Height - S(12));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();
        var scale = DeviceDpi / 96f;
        if (e.Button == MouseButtons.Right || (e.X >= Width - 42 * scale && e.Y < 58 * scale))
            MenuRequested?.Invoke(Account, PointToScreen(e.Location));
        else if (e.X >= Width - 80 * scale && e.Y < 58 * scale) FavoriteRequested?.Invoke(Account);
        else if (e.Button == MouseButtons.Left) CopyRequested?.Invoke(Account);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { CopyRequested?.Invoke(Account); e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
}

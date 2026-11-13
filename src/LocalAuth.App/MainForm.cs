using System.Security.Cryptography;
using LocalAuth.Core;
using Microsoft.Win32;

namespace LocalAuth.App;

internal sealed class MainForm : Form
{
    private readonly VaultStore _store;
    private readonly QuickAccessStore _ordinaryStore;
    private VaultDocument _ordinary;
    private readonly PrivateEntryIndexStore _indexStore;
    private PrivateEntryIndex? _privateIndex;
    private bool _indexCacheFailed;
    private readonly ClipboardWriter _clipboard = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _copyFeedback = new() { Interval = 2000 };
    private readonly IDisposable _activation;
    private readonly ActivityFilter _activity;
    private OpenVault? _vault;
    private AppSettings _settings;
    private readonly string _settingsPath;
    private Panel _root = null!;
    private FlowLayoutPanel? _cards;
    private TextBox? _search;
    private Label? _subtitle;
    private Label? _status;
    private RoundedButton? _allButton;
    private RoundedButton? _favoriteButton;
    private RoundedButton? _privateButton;
    private RoundedButton? _privateAction;
    private readonly ToolTip _tooltips = new();
    private bool _privateView;
    private bool _favoritesOnly;
    private bool _busy;
    private bool _closing;
    private bool _exitRequested;
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = Theme.Menu();
    private readonly ToolStripMenuItem _quickCopyMenu = new();
    private ContextMenuStrip? _accountMenu;
    private int _generation;
    private int _failedAttempts;
    private long _unlockAfter;
    private long _lastActivity = Environment.TickCount64;
    private long _statusUntil;

    internal MainForm(VaultStore store)
    {
        _store = store;
        _settingsPath = Path.Combine(Path.GetDirectoryName(store.FilePath)!, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        UiText.UseLanguage(_settings.Language);
        Theme.SetMode(_settings.ThemeMode);
        _ordinaryStore = new QuickAccessStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "ordinary.local"));
        _ordinary = _ordinaryStore.Read();
        _indexStore = new PrivateEntryIndexStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "private-index.local"));
        _privateIndex = _indexStore.TryRead(store);
        Text = "AuthLocal for Windows";
        Font = Theme.Font();
        BackColor = Theme.Background;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(S(1080), S(740));
        MinimumSize = new Size(S(900), S(620));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
        _activity = new ActivityFilter(() => _lastActivity = Environment.TickCount64);
        Application.AddMessageFilter(_activity);
        _timer.Tick += (_, _) => Tick();
        _copyFeedback.Tick += (_, _) => { _copyFeedback.Stop(); UpdateTrayLanguage(); };
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
        Resize += (_, _) =>
        {
            UpdateRefreshTimer();
            if (!Visible || WindowState == FormWindowState.Minimized) LockVault("已在最小化时锁定");
        };
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.L) { LockVault("已锁定"); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; await EditAccountAsync(); }
            else if (e.Control && e.KeyCode == Keys.F) { _search?.Focus(); e.SuppressKeyPress = true; }
        };
        _trayMenu.Items.Add("打开首页", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add("锁定私密区", null, (_, _) => LockVault("已锁定"));
        _trayMenu.Items.Add(_quickCopyMenu);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出 AuthLocal", null, (_, _) => { _exitRequested = true; Close(); }).Name = "ExitApplication";
        _trayMenu.Opening += (_, _) => BuildQuickCopyMenu();
        _tray.Icon = Icon ?? SystemIcons.Application;
        _tray.Text = UiText.T("AuthLocal · 单击打开首页");
        UiText.Apply(_trayMenu);
        _tray.ContextMenuStrip = _trayMenu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreWindow(); };
        _tray.Visible = true;
        BuildMain();
        _activation = ExistingInstance.Register(this, Path.GetDirectoryName(store.FilePath)!, RestoreWindow);
    }

    private void RestoreWindow()
    {
        if (_closing) return;
        var modal = OwnedForms.LastOrDefault(form => form.Visible && form.Modal);
        if (modal is not null) { modal.Activate(); return; }
        _privateView = false;
        _favoritesOnly = false;
        _search?.Clear();
        RenderCards();
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void UpdateRefreshTimer()
    {
        if (_closing) return;
        _timer.Enabled = Visible && WindowState != FormWindowState.Minimized;
        if (_timer.Enabled) Tick();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_closing) return;
        if (!Visible && (_vault is not null || _busy)) LockVault("已收起至托盘并锁定");
        UpdateRefreshTimer();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exitRequested)
        {
            e.Cancel = true;
            LockVault("已收起至托盘并锁定");
            // Invalidate an unlock/create operation that may still be running.
            _generation++;
            Hide();
            base.OnFormClosing(e);
            return;
        }
        base.OnFormClosing(e);
    }

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private T Scaled<T>(T control) where T : Control
    {
        if (DeviceDpi != 96) control.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
        return control;
    }

    private void NewRoot()
    {
        _cards = null;
        _search = null;
        _subtitle = null;
        _status = null;
        _allButton = null;
        _favoriteButton = null;
        _privateButton = null;
        _privateAction = null;
        _tooltips.RemoveAll();
        foreach (var control in Controls.Cast<Control>().ToArray()) control.Dispose();
        _root = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        Controls.Add(_root);
    }

    private async Task<bool> UnlockPrivateAsync()
    {
        if (_vault is not null) return true;
        if (_busy || _closing) return false;
        if (Environment.TickCount64 < _unlockAfter) { ShowError("尝试次数较多，请等 15 秒后重试。"); return false; }
        var setup = !_store.Exists;
        var generation = _generation;
        using var prompt = new PasswordDialog(setup ? "设置私密区密码" : "解锁私密区",
            setup ? "仅私密条目需要此密码。至少 5 位，支持纯数字。" : "输入主密码查看和复制私密内容，普通条目可直接使用。", confirm: setup);
        if (prompt.ShowDialog(this) != DialogResult.OK || _closing || generation != _generation) return false;
        var password = prompt.Password;
        _busy = true;
        OpenVault? opened = null;
        try
        {
            opened = await Task.Run(() => setup ? VaultCipher.Create(password) : VaultCipher.Open(_store.Read(), password));
            if (_closing || generation != _generation || !Visible || WindowState == FormWindowState.Minimized) return false;
            if (setup) _store.Save(opened);
            _vault = opened;
            opened = null;
            _failedAttempts = 0;
            _unlockAfter = 0;
            _lastActivity = Environment.TickCount64;
            SyncPrivateIndex();
            RenderCards();
            return true;
        }
        catch (Exception ex) when (Expected(ex))
        {
            if (ex is CryptographicException && ++_failedAttempts >= 5) _unlockAfter = Environment.TickCount64 + 15_000;
            if (!_closing && generation == _generation) ShowError(Friendly(ex));
            return false;
        }
        finally { opened?.Dispose(); password = ""; _busy = false; }
    }

    private void BuildMain()
    {
        NewRoot();
        AcceptButton = null;
        var sidebar = new BufferedPanel { Dock = DockStyle.Left, Width = 184, BackColor = Theme.Navigation };
        sidebar.Controls.Add(new BrandMark { Location = new Point(16, 20), Size = new Size(30, 30) });
        sidebar.Controls.Add(Theme.Label("AuthLocal", 54, 18, 116, 32, 14, Theme.Ink, FontStyle.Bold));
        sidebar.Controls.Add(Theme.Label("Offline Authenticator", 18, 56, 154, 24, 8.5f, Theme.Muted));
        _allButton = Theme.Button("全部条目", 12, 104, 160);
        _privateButton = Theme.Button("私密条目", 12, 150, 160);
        _favoriteButton = Theme.Button("☆  收藏条目", 12, 196, 160);
        foreach (var button in new[] { _allButton, _privateButton, _favoriteButton })
        {
            button.Height = 38;
            button.Font = Theme.Font(10);
            button.BorderColor = Theme.Navigation;
        }
        _allButton.Click += (_, _) => { _privateView = false; _favoritesOnly = false; RenderCards(); };
        _privateButton.Click += (_, _) => { _privateView = true; _favoritesOnly = false; RenderCards(); };
        _favoriteButton.Click += (_, _) => { _favoritesOnly = !_favoritesOnly; RenderCards(); };
        sidebar.Controls.AddRange([_allButton, _privateButton, _favoriteButton]);

        var lower = new BufferedPanel { Dock = DockStyle.Bottom, Height = 154, BackColor = Theme.Navigation };
        var backups = Theme.Button("备份与恢复", 12, 8, 160);
        var settings = Theme.Button("设置", 12, 52, 160);
        foreach (var button in new[] { backups, settings })
        {
            button.Height = 36;
            button.Font = Theme.Font(9.5f);
            button.Fill = Theme.Navigation;
            button.BorderColor = Theme.Navigation;
            lower.Controls.Add(button);
        }
        var backupMenu = Theme.Menu();
        backupMenu.Items.Add("导出普通条目备份", null, async (_, _) => await ExportBackupAsync(false));
        backupMenu.Items.Add("导出私密条目备份", null, async (_, _) => await ExportBackupAsync(true));
        backupMenu.Items.Add(new ToolStripSeparator());
        backupMenu.Items.Add("导入到普通条目", null, async (_, _) => await ImportBackupAsync(false));
        backupMenu.Items.Add("导入到私密条目", null, async (_, _) => await ImportBackupAsync(true));
        backupMenu.Items.Add(new ToolStripSeparator());
        var restore = backupMenu.Items.Add("从备份恢复私密区…", null, async (_, _) => await RestoreBackupAsync());
        backupMenu.Opening += (_, _) => restore.Enabled = _vault is null && !_busy;
        backups.Disposed += (_, _) => backupMenu.Dispose();
        backups.Click += (_, _) => { UiText.Apply(backupMenu); backupMenu.Show(backups, new Point(backups.Width, 0)); };
        settings.Click += async (_, _) => await ConfigureAsync();
        lower.Controls.Add(Theme.Label("●  本地离线", 18, 112, 148, 24, 8, Theme.Muted));
        sidebar.Controls.Add(lower);
        Scaled(sidebar);

        var content = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(S(20), 0, S(18), 0) };
        var header = new BufferedPanel { Dock = DockStyle.Top, Height = S(116), BackColor = Theme.Background };
        var heading = Scaled(Theme.Label("验证码与密码", 0, 16, 300, 36, 16, style: FontStyle.Bold));
        _subtitle = Scaled(Theme.Label("", 306, 20, 160, 28, 9, Theme.Muted));
        var add = Scaled(Theme.Button("＋ 添加条目", 0, 16, 116, true));
        add.Height = S(36);
        _privateAction = Scaled(Theme.Button("解锁私密", 0, 16, 118));
        _privateAction.Height = S(36);
        _privateAction.Font = Theme.Font(9.5f);
        _privateAction.Click += async (_, _) =>
        {
            if (_vault is null) await UnlockPrivateAsync();
            else LockVault("已锁定私密内容");
        };
        var addMenu = Theme.Menu();
        addMenu.Items.Add("动态验证码（手动 / 二维码）", null, async (_, _) => await EditAccountAsync());
        addMenu.Items.Add("保存密码", null, async (_, _) => await EditAccountAsync(passwordEntry: true));
        add.Disposed += (_, _) => addMenu.Dispose();
        add.Click += (_, _) => { UiText.Apply(addMenu); addMenu.Show(add, new Point(0, add.Height)); };
        _search = Scaled(Theme.TextBox(0, 68, 500));
        _search.PlaceholderText = "搜索条目…   Ctrl+F";
        _search.AccessibleName = "搜索账户";
        _search.TextChanged += (_, _) => RenderCards();
        header.Controls.AddRange([heading, _subtitle, add, _privateAction, _search]);
        header.Resize += (_, _) =>
        {
            add.Location = new Point(header.ClientSize.Width - add.Width - S(2), S(16));
            _privateAction.Location = new Point(add.Left - _privateAction.Width - S(10), S(16));
            _search.Width = Math.Max(S(220), header.ClientSize.Width - S(2));
            _subtitle.Width = Math.Max(S(40), _privateAction.Left - _subtitle.Left - S(10));
        };
        var footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = S(30), BackColor = Theme.Background };
        _status = Scaled(Theme.Label(CurrentStatus, 0, 0, 760, 28, 8, Theme.Muted));
        footer.Controls.Add(_status);
        footer.Resize += (_, _) => _status.Width = footer.Width;
        _cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _cards.Resize += (_, _) => SizeCards();
        content.Controls.Add(_cards);
        content.Controls.Add(footer);
        content.Controls.Add(header);
        _root.Controls.Add(content);
        _root.Controls.Add(sidebar);
        RenderCards();
        UiText.Apply(_root);
        UpdateTrayLanguage();
    }

    private void UpdateTrayLanguage()
    {
        Theme.RefreshMenu(_trayMenu);
        _trayMenu.Items[0].Text = UiText.T("打开首页");
        _trayMenu.Items[1].Text = UiText.T("锁定私密区");
        _quickCopyMenu.Text = UiText.T("快速复制收藏");
        _trayMenu.Items["ExitApplication"]!.Text = UiText.T("退出 AuthLocal");
        _tray.Text = UiText.T(_copyFeedback.Enabled ? "AuthLocal · 已复制" : "AuthLocal · 单击打开首页");
    }

    private void BuildQuickCopyMenu()
    {
        foreach (ToolStripItem item in _quickCopyMenu.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
        var favorites = _ordinary.Accounts.Where(a => a.Favorite).Select(a => (Account: a, Private: false)).ToList();
        var privateAccounts = _vault?.Document.Accounts ?? _privateIndex?.Entries.Select(e => e.ToLockedAccount()).ToList() ?? [];
        favorites.AddRange(privateAccounts.Where(a => a.Favorite).Select(a => (Account: a, Private: true)));
        foreach (var (account, isPrivate) in favorites.OrderBy(e => e.Account.Issuer, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(e => e.Account.Name, StringComparer.CurrentCultureIgnoreCase).Take(12))
        {
            var id = account.Id; // Keep only the ID in callbacks, never a captured secret.
            var title = account.Issuer.Length == 0 ? account.Name : account.Name.Length == 0 ? account.Issuer : account.Issuer + " · " + account.Name;
            title = (isPrivate ? UiText.T("[私密] ") : "") + title.Replace('\r', ' ').Replace('\n', ' ');
            var textElements = new System.Globalization.StringInfo(title);
            if (textElements.LengthInTextElements > 70) title = textElements.SubstringByTextElements(0, 70) + "…";
            var item = new ToolStripMenuItem(title.Replace("&", "&&")) { Tag = "UserContent" };
            item.Click += (_, _) => BeginInvoke(async () => await CopyFavoriteAsync(id));
            _quickCopyMenu.DropDownItems.Add(item);
        }
        if (favorites.Count == 0) _quickCopyMenu.DropDownItems.Add(new ToolStripMenuItem(UiText.T("还没有收藏条目")) { Enabled = false });
        else
        {
            _quickCopyMenu.DropDownItems.Add(new ToolStripSeparator());
            _quickCopyMenu.DropDownItems.Add(UiText.T("在首页查看全部收藏"), null, (_, _) =>
            {
                RestoreWindow();
                _favoritesOnly = true;
                RenderCards();
            });
        }
    }

    private async Task CopyFavoriteAsync(Guid id)
    {
        if (_closing || _busy) return;
        if (OwnedForms.Any(form => form.Visible && form.Modal)) { RestoreWindow(); return; }
        var account = FindAvailable(id);
        if (account is null)
        {
            var cached = _privateIndex?.Entries.FirstOrDefault(e => e.Id == id && e.Favorite);
            if (cached is null) return;
            RestoreWindow();
            account = await ResolveCardAsync(cached.ToLockedAccount());
        }
        if (account?.Favorite != true || !CopyCode(account)) return;
        _copyFeedback.Stop();
        _copyFeedback.Start();
        UpdateTrayLanguage();
    }

    private const string DefaultStatus = "单击卡片复制 · 剪贴板由你自行替换 · Ctrl+L 锁定私密内容";
    private bool NeedsPrivateIndex => _store.Exists && _vault is null && _privateIndex is null;
    private string CurrentStatus => NeedsPrivateIndex ? "普通条目可直接复制 · 私密名称待同步，可点击右上角「解锁并同步」" : DefaultStatus;

    private IReadOnlyList<Account> Section(bool isPrivate) =>
        isPrivate ? (IReadOnlyList<Account>?)_vault?.Document.Accounts ?? Array.Empty<Account>() : _ordinary.Accounts;

    private bool IsPrivate(Account account) => _vault?.Document.Accounts.Any(a => a.Id == account.Id) == true;

    private Account? FindAvailable(Guid id) => _ordinary.Accounts.FirstOrDefault(a => a.Id == id)
        ?? _vault?.Document.Accounts.FirstOrDefault(a => a.Id == id);

    private void SyncPrivateIndex()
    {
        if (_vault is null) return;
        _privateIndex = PrivateEntryIndex.From(_vault.Document.Accounts);
        try
        {
            _privateIndex = _privateIndex with { VaultFingerprint = PrivateEntryIndexStore.Fingerprint(_store.Read()) };
            _indexStore.Save(_privateIndex);
            _indexCacheFailed = false;
        }
        catch (Exception ex) when (Expected(ex)) { _indexCacheFailed = true; }
    }

    private void RenderCards()
    {
        if (_cards is null) return;
        var query = _search?.Text.Trim() ?? "";
        var entries = new List<(Account Account, bool Private, bool Locked)>();
        if (!_privateView) entries.AddRange(_ordinary.Accounts.Select(a => (a, false, false)));
        if (_vault is not null) entries.AddRange(_vault.Document.Accounts.Select(a => (a, true, false)));
        else if (_privateIndex is not null) entries.AddRange(_privateIndex.Entries.Select(e => (e.ToLockedAccount(), true, true)));
        var visible = entries.Where(e => (!_favoritesOnly || e.Account.Favorite) &&
                (query.Length == 0 || e.Account.Issuer.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Account.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Account.Favorite).ThenBy(e => e.Account.Issuer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Account.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _subtitle!.Text = UiText.F("{0} · {1} 项", UiText.T(_privateView ? "私密" : _favoritesOnly ? "收藏" : "全部"), visible.Count);
        if (_privateAction is not null)
        {
            _privateAction.Text = UiText.T(_vault is not null ? "锁定私密" : NeedsPrivateIndex ? "解锁并同步" : "解锁私密");
            _privateAction.Enabled = !_busy;
            _tooltips.SetToolTip(_privateAction, UiText.T(_indexCacheFailed ? "私密名称暂未缓存，下次需要再次解锁同步。" : NeedsPrivateIndex
                ? "解锁一次以同步私密条目名称。普通条目可直接使用。" : _vault is null ? "输入主密码查看和复制私密内容。" : "锁定私密内容，普通条目仍可使用。Ctrl+L"));
        }
        if (_status is not null && Environment.TickCount64 >= _statusUntil) _status.Text = UiText.T(CurrentStatus);
        foreach (var (button, active) in new[] { (_allButton!, !_privateView), (_privateButton!, _privateView), (_favoriteButton!, _favoritesOnly) })
        {
            button.Fill = active ? Theme.AccentLight : Theme.Navigation;
            button.ForeColor = active ? Theme.Accent : Theme.Ink;
            button.BorderColor = button.Fill;
            button.Invalidate();
        }
        _cards.SuspendLayout();
        foreach (var control in _cards.Controls.Cast<Control>().ToArray()) control.Dispose();
        if (visible.Count == 0)
        {
            var empty = new BufferedPanel { Size = new Size(S(620), S(130)), Margin = Padding.Empty };
            var title = query.Length > 0 ? "没有找到匹配条目" : NeedsPrivateIndex ? "私密名称待同步" : _favoritesOnly ? "还没有收藏条目" : "还没有条目";
            var help = query.Length > 0 ? "试试服务名称、邮箱或用户名。" : NeedsPrivateIndex
                ? "点击右上角「解锁并同步」即可加载，普通条目无需解锁。" : _privateView
                ? "添加条目时勾选「设为私密」，或从条目菜单移入。" : "点击右上角添加验证码或密码，单击卡片即可复制。";
            empty.Controls.Add(Scaled(Theme.Label(title, 4, 18, 600, 32, 15, style: FontStyle.Bold)));
            empty.Controls.Add(Scaled(Theme.Label(help, 4, 60, 600, 44, 9.5f, Theme.Muted)));
            _cards.Controls.Add(empty);
        }
        foreach (var entry in visible)
        {
            var card = new CodeCard(entry.Account, entry.Private, entry.Locked) { Height = S(132), Margin = new Padding(0, 0, S(12), S(12)) };
            card.CopyRequested += async a => await CopyFromCardAsync(a);
            card.FavoriteRequested += async a => { var available = await ResolveCardAsync(a); if (available is not null) ToggleFavorite(available); };
            card.MenuRequested += async (a, point) => { var available = await ResolveCardAsync(a); if (available is not null) ShowAccountMenu(available, point); };
            _cards.Controls.Add(card);
        }
        _cards.ResumeLayout();
        SizeCards();
    }

    private async Task<Account?> ResolveCardAsync(Account account)
    {
        var available = FindAvailable(account.Id);
        if (available is not null) return available;
        if (_privateIndex?.Entries.Any(e => e.Id == account.Id) != true || !await UnlockPrivateAsync()) return null;
        return FindAvailable(account.Id);
    }

    private async Task CopyFromCardAsync(Account account)
    {
        var available = await ResolveCardAsync(account);
        if (available is not null) CopyCode(available);
    }

    private void SizeCards()
    {
        if (_cards is null) return;
        var available = _cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(2);
        var columns = Math.Max(1, (available + S(12)) / S(332));
        var width = Math.Max(S(300), available / columns - S(12));
        foreach (var card in _cards.Controls.OfType<CodeCard>()) if (card.Width != width) card.Width = width;
    }

    private bool CopyCode(Account account)
    {
        var available = FindAvailable(account.Id);
        if (available is null) return false;
        try
        {
            _clipboard.Copy(available.IsPassword ? available.Secret : Totp.Generate(available, DateTimeOffset.UtcNow), Handle);
            _cards?.Controls.OfType<CodeCard>().FirstOrDefault(c => c.Account.Id == available.Id)?.ShowCopied();
            Toast(available.IsPassword ? "密码已复制，可直接切换窗口粘贴。" : "验证码已复制，可直接切换窗口粘贴。");
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException) { ShowError("剪贴板正被其他程序使用，请稍后再试。"); }
        return false;
    }

    private void ToggleFavorite(Account account)
    {
        if (_busy || FindAvailable(account.Id) is null) return;
        var isPrivate = IsPrivate(account);
        Commit(Section(isPrivate).Select(a => a.Id == account.Id ? a with { Favorite = !a.Favorite } : a).ToList(), isPrivate);
    }

    private void ShowAccountMenu(Account account, Point location)
    {
        if (_busy || FindAvailable(account.Id) is null) return;
        var isPrivate = IsPrivate(account);
        _accountMenu?.Dispose();
        var menu = Theme.Menu();
        _accountMenu = menu;
        menu.Items.Add("编辑条目", null, (_, _) => BeginInvoke(async () => await EditAccountAsync(account)));
        menu.Items.Add(account.Favorite ? "取消收藏" : "收藏条目", null, (_, _) => BeginInvoke(() => ToggleFavorite(account)));
        menu.Items.Add(isPrivate ? "移到普通区（无需密码）" : "设为私密（需要密码）", null, (_, _) => BeginInvoke(async () =>
        {
            if (_busy || FindAvailable(account.Id) is null) return;
            if (!isPrivate && !await UnlockPrivateAsync()) return;
            if (FindAvailable(account.Id) is null) return;
            if (MoveAccount(account, isPrivate))
                Toast(isPrivate ? "已移到普通区，下次打开无需密码。" : "已设为私密，锁定后需密码才能使用。");
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("删除条目", null, (_, _) => BeginInvoke(() =>
        {
            if (_busy || FindAvailable(account.Id) is null) return;
            if (UiText.Message(this, UiText.F("删除「{0} {1}」？\n\n删除后可从此前导出的加密备份恢复。", account.Issuer, account.Name), "删除条目", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes && FindAvailable(account.Id) is not null)
                Commit(Section(isPrivate).Where(a => a.Id != account.Id).ToList(), isPrivate);
        }));
        menu.Closed += (_, _) =>
        {
            // Closed runs before the menu item finishes dispatching its click.
            // Keep the menu alive through this message, then release it.
            void Release()
            {
                menu.Dispose();
                if (ReferenceEquals(_accountMenu, menu)) _accountMenu = null;
            }
            if (IsHandleCreated && !_closing) BeginInvoke(Release);
            else Release();
        };
        UiText.Apply(menu);
        menu.Show(location);
    }

    private async Task EditAccountAsync(Account? account = null, bool passwordEntry = false)
    {
        if (_busy) return;
        if (account is not null)
        {
            account = FindAvailable(account.Id);
            if (account is null) return;
        }
        var generation = _generation;
        var wasPrivate = account is not null && IsPrivate(account);
        Account? result;
        bool protect;
        if (passwordEntry || account?.IsPassword == true)
        {
            using var dialog = new StoredPasswordDialog(account, account is null ? _privateView : wasPrivate);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            result = dialog.Result;
            protect = dialog.RequireUnlock;
        }
        else
        {
            using var dialog = new EntryDialog(account, account is null ? _privateView : wasPrivate);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            result = dialog.Result;
            protect = dialog.RequireUnlock;
        }
        if (result is null || generation != _generation) return;
        if (protect && !await UnlockPrivateAsync()) return;
        if (_closing || generation != _generation || (account is not null && FindAvailable(account.Id) is null)) return;
        if (Section(protect).Any(a => a.Id != result.Id && a.SameCredential(result)))
        {
            ShowError("这个条目已经在目标分区中。");
            return;
        }
        if (account is not null && wasPrivate != protect)
        {
            if (!MoveAccount(result, wasPrivate)) return;
        }
        else
        {
            var accounts = Section(protect).ToList();
            var index = accounts.FindIndex(a => a.Id == result.Id);
            if (index >= 0) accounts[index] = result;
            else accounts.Add(result);
            if (!Commit(accounts, protect)) return;
        }
        _privateView = false;
        RenderCards();
        Toast(protect ? "已加密保存到私密区。" : "已保存到普通区，打开即可直接使用。");
    }

    private bool MoveAccount(Account account, bool fromPrivate)
    {
        if (_vault is null) return false;
        try
        {
            EntryTransfer.Move(account, Section(fromPrivate), Section(!fromPrivate),
                accounts => WriteSection(accounts, fromPrivate), accounts => WriteSection(accounts, !fromPrivate));
            RenderCards();
            return true;
        }
        catch (Exception ex) when (Expected(ex))
        {
            RenderCards();
            ShowError("移动未完成，请检查文件权限后重试。\n" + (ex is IOException { InnerException: AggregateException } ? ex.Message : Friendly(ex)));
            return false;
        }
    }

    private void WriteSection(List<Account> accounts, bool isPrivate)
    {
        if (!isPrivate)
        {
            var next = (_ordinary with { Accounts = accounts }).Validate();
            _ordinaryStore.Save(next);
            _ordinary = next;
            return;
        }
        if (_vault is null) throw new FormatException("私密区已锁定，请重新解锁。");
        var previous = _vault.Document;
        try { _vault.Document = (previous with { Accounts = accounts }).Validate(); _store.Save(_vault); }
        catch { _vault.Document = previous; throw; }
        SyncPrivateIndex();
    }

    private bool Commit(List<Account> accounts, bool isPrivate)
    {
        try { WriteSection(accounts, isPrivate); RenderCards(); return true; }
        catch (Exception ex) when (Expected(ex))
        {
            ShowError("保存未成功，本次修改未应用。\n" + Friendly(ex));
            return false;
        }
    }

    private async Task ExportBackupAsync(bool isPrivate)
    {
        if (_busy) return;
        if (isPrivate && !await UnlockPrivateAsync()) return;
        var generation = _generation;
        using var picker = new SaveFileDialog
        {
            Title = UiText.T(isPrivate ? "导出私密区加密备份" : "导出普通区加密备份"), Filter = UiText.T("AuthLocal 加密备份|*.vault"), AddExtension = true,
            FileName = $"AuthLocal-{(isPrivate ? "private" : "ordinary")}-{DateTime.Now:yyyyMMdd-HHmm}.vault", OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK || generation != _generation) return;
        if (SamePath(picker.FileName, _store.FilePath) || SamePath(picker.FileName, _store.BackupPath) || SamePath(picker.FileName, _ordinaryStore.FilePath))
        {
            ShowError("请选择本机数据目录之外的位置保存备份。");
            return;
        }
        var password = "";
        if (!isPrivate)
        {
            using var prompt = new PasswordDialog("设置备份密码", "只用于这份普通区备份，日常打开软件仍无需密码。至少 5 位。", confirm: true);
            if (prompt.ShowDialog(this) != DialogResult.OK || generation != _generation) return;
            password = prompt.Password;
        }
        _busy = true;
        OpenVault? backup = null;
        try
        {
            byte[] bytes;
            if (isPrivate) bytes = _vault!.Seal();
            else
            {
                var document = _ordinary;
                backup = await Task.Run(() => VaultCipher.Create(password, document));
                if (_closing || generation != _generation) return;
                bytes = backup.Seal();
            }
            VaultStore.WriteAtomically(picker.FileName, bytes);
            Toast(isPrivate ? "已导出私密区。恢复时使用主密码；普通区需单独导出。" : "已导出普通区。恢复时使用刚设置的备份密码；私密区需单独导出。", 12_000);
        }
        catch (Exception ex) when (Expected(ex)) { if (!_closing) ShowError(Friendly(ex)); }
        finally { backup?.Dispose(); password = ""; _busy = false; }
    }

    private async Task ImportBackupAsync(bool isPrivate)
    {
        if (_busy) return;
        if (isPrivate && !await UnlockPrivateAsync()) return;
        using var picker = BackupPicker();
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var generation = _generation;
        using var password = new PasswordDialog("导入加密备份", UiText.F("输入备份密码。新条目将合并到{0}，不会改变另一分区。", UiText.T(isPrivate ? "私密区" : "普通区（无需解锁）")));
        if (password.ShowDialog(this) != DialogResult.OK || generation != _generation) return;
        var enteredPassword = password.Password;
        _busy = true;
        OpenVault? imported = null;
        try
        {
            imported = await Task.Run(() => VaultCipher.Open(VaultStore.ReadBounded(picker.FileName), enteredPassword));
            if (_closing || generation != _generation) return;
            var accounts = Section(isPrivate).ToList();
            var added = 0;
            foreach (var account in imported.Document.Accounts)
            {
                if (accounts.Any(a => a.SameCredential(account))) continue;
                accounts.Add(FindAvailable(account.Id) is not null ? account with { Id = Guid.NewGuid() } : account);
                added++;
            }
            if (added == 0) { Toast("备份条目已存在，没有重复导入。"); return; }
            if (UiText.Message(this, UiText.F("将导入 {0} 个条目到{1}。是否继续？", added, UiText.T(isPrivate ? "私密区" : "普通区，打开即可使用")), "导入备份", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK || generation != _generation) return;
            if (Commit(accounts, isPrivate)) Toast(UiText.F("已导入 {0} 个条目到{1}。", added, UiText.T(isPrivate ? "私密区" : "普通区")));
        }
        catch (Exception ex) when (Expected(ex)) { if (!_closing && generation == _generation) ShowError(Friendly(ex)); }
        finally { imported?.Dispose(); enteredPassword = ""; _busy = false; }
    }

    private async Task RestoreBackupAsync()
    {
        if (_busy || _vault is not null) return;
        using var picker = BackupPicker();
        if (picker.ShowDialog(this) != DialogResult.OK || _vault is not null) return;
        using var password = new PasswordDialog("恢复私密区", "输入备份密码。只恢复私密区，普通区保持原样；恢复后主密码与备份相同。");
        if (password.ShowDialog(this) != DialogResult.OK || _vault is not null) return;
        var enteredPassword = password.Password;
        var generation = _generation;
        _busy = true;
        OpenVault? recovered = null;
        try
        {
            recovered = await Task.Run(() => VaultCipher.Open(VaultStore.ReadBounded(picker.FileName), enteredPassword));
            if (_closing || generation != _generation) return;
            if (UiText.Message(this, UiText.F("将用备份中的 {0} 个条目替换私密区，普通区不受影响。是否继续？", recovered.Document.Accounts.Count), "恢复私密区", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes || generation != _generation) return;
            if (_store.Exists)
                VaultStore.WriteAtomically(Path.Combine(Path.GetDirectoryName(_store.FilePath)!, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.vault"), _store.Read());
            // Avoid ID collisions with the independent ordinary store.
            recovered.Document = recovered.Document with { Accounts = recovered.Document.Accounts.Select(a => _ordinary.Accounts.Any(o => o.Id == a.Id) ? a with { Id = Guid.NewGuid() } : a).ToList() };
            _store.Save(recovered);
            _vault = recovered;
            recovered = null;
            SyncPrivateIndex();
            _generation++;
            _lastActivity = Environment.TickCount64;
            if (!Visible || WindowState == FormWindowState.Minimized) LockVault("已锁定私密区");
            else { RenderCards(); Toast("私密区已恢复，使用备份的密码解锁。", 12_000); }
        }
        catch (Exception ex) when (Expected(ex)) { if (!_closing) ShowError(Friendly(ex)); }
        finally { recovered?.Dispose(); enteredPassword = ""; _busy = false; }
    }

    private static OpenFileDialog BackupPicker() => new()
    {
        Title = UiText.T("选择 AuthLocal 加密备份"), Filter = UiText.T("AuthLocal 加密备份|*.vault;*.bak|所有文件|*.*"), CheckFileExists = true
    };

    private async Task ConfigureAsync()
    {
        if (_busy) return;
        var generation = _generation;
        using var dialog = new SettingsDialog(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK || generation != _generation) return;
        try
        {
            var settings = _settings with { AutoLockMinutes = dialog.AutoLockMinutes, ThemeMode = dialog.ThemeMode, Language = dialog.Language };
            settings.Save(_settingsPath);
            _settings = settings;
            UiText.UseLanguage(settings.Language);
            Theme.SetMode(settings.ThemeMode);
            BuildMain();
            Theme.ApplyTitleBar(this);
        }
        catch (Exception ex) when (Expected(ex)) { ShowError(Friendly(ex)); return; }
        if (dialog.ChangePassword && await UnlockPrivateAsync()) await ChangePasswordAsync();
        else Toast("设置已保存。");
    }

    private async Task ChangePasswordAsync()
    {
        if (_vault is null || _busy) return;
        var generation = _generation;
        using var prompt = new PasswordDialog("更改主密码", "新主密码至少 5 个字符，支持纯数字。此前导出的备份仍使用原来的密码。", confirm: true);
        if (prompt.ShowDialog(this) != DialogResult.OK || _vault is null || generation != _generation) return;
        var enteredPassword = prompt.Password;
        var document = _vault.Document;
        _busy = true;
        OpenVault? replacement = null;
        try
        {
            replacement = await Task.Run(() => VaultCipher.Create(enteredPassword, document));
            if (_closing || _vault is null || generation != _generation) return;
            // Preserve any account changes made while the password was being derived.
            replacement.Document = _vault.Document;
            var bytes = replacement.Seal();
            VaultStore.WriteAtomically(_store.FilePath, bytes);
            _vault.Dispose();
            _vault = replacement;
            replacement = null;
            SyncPrivateIndex();
            try { VaultStore.WriteAtomically(_store.BackupPath, bytes); }
            catch (Exception ex) when (Expected(ex)) { ShowError("主密码已更改，但自动备份更新失败，旧自动备份仍使用旧密码。\n请导出一份新的加密备份。"); }
            Toast("主密码已更改。建议立即导出新的加密备份。", 12_000);
        }
        catch (Exception ex) when (Expected(ex)) { if (!_closing && generation == _generation) ShowError("更改主密码未成功。\n" + Friendly(ex)); }
        finally { replacement?.Dispose(); enteredPassword = ""; _busy = false; }
    }

    private void Tick()
    {
        if (_privateAction is not null) _privateAction.Enabled = !_busy;
        if (_vault is not null && Environment.TickCount64 - _lastActivity >= _settings.AutoLockMinutes * 60_000L)
        {
            LockVault("无操作后已锁定私密区，普通条目仍可使用");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (_cards is not null) foreach (var card in _cards.Controls.OfType<CodeCard>()) card.Tick(now);
        if (_status is not null && Environment.TickCount64 >= _statusUntil) { _status.Text = UiText.T(CurrentStatus); _status.ForeColor = Theme.Muted; }
    }

    private void Toast(string message, int duration = 6500)
    {
        if (_status is null) return;
        _status.Text = UiText.T(message);
        _status.ForeColor = Theme.Accent;
        _statusUntil = Environment.TickCount64 + duration;
    }

    private void LockVault(string reason)
    {
        if (_closing) return;
        _generation++;
        _accountMenu?.Close();
        foreach (var form in OwnedForms) form.Close();
        _vault?.Dispose();
        _vault = null;
        _privateView = false;
        _favoritesOnly = false;
        _search?.Clear();
        RenderCards();
        Toast(UiText.T(reason) + UiText.T(" · 普通条目可直接使用"));
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            QueueLock("Windows 会话已锁定");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume) QueueLock("电脑睡眠或唤醒后已锁定");
    }

    private void QueueLock(string reason)
    {
        if (!_closing && IsHandleCreated) { try { BeginInvoke(() => LockVault(reason)); } catch (InvalidOperationException) { } }
    }

    private void OnSystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeMode != "System" || _closing || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                var changed = Theme.SetMode("System");
                if (!changed || _closing) return;
                BuildMain();
                Theme.ApplyTitleBar(this);
                foreach (var form in OwnedForms) Theme.ApplyTo(form);
            });
        }
        catch (InvalidOperationException) { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(S(900), S(620));
        if (_busy) return;
        BuildMain();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing = true;
        _generation++;
        _activation.Dispose();
        _copyFeedback.Stop();
        _copyFeedback.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _tooltips.Dispose();
        _accountMenu?.Dispose();
        _accountMenu = null;
        _timer.Stop();
        _timer.Dispose();
        Application.RemoveMessageFilter(_activity);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged;
        _vault?.Dispose();
        _vault = null;
        base.OnFormClosed(e);
    }

    private void ShowError(string message)
    {
        if (!_closing) UiText.Message(this, message, "AuthLocal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static bool Expected(Exception ex) => ex is FormatException or CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private static string Friendly(Exception ex) => ex switch
    {
        CryptographicException => "密码不正确，或加密文件已损坏。请检查密码或尝试其他备份。",
        FormatException => UiText.T(ex.Message),
        _ => "文件无法读写。请检查保存位置、磁盘空间和文件访问权限。"
    };
    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}

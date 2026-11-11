using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LocalAuth.Core;

// Run on Windows. Uses an isolated directory and public, synthetic credentials.
// Reflection keeps the production UI surface private while exercising its real
// persistence, lock, copy, restart, and tray handlers on the UI thread.
internal static partial class Program
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly App = Assembly.Load("AuthLocal");
    private static readonly Type MainType = App.GetType("LocalAuth.App.MainForm")!;
    private static int _passed;
    private static readonly List<Form> Forms = [];
    private static readonly string Temporary = Path.Combine(Path.GetTempPath(), "LocalAuth-ui-tests-" + Guid.NewGuid().ToString("N"));

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(Temporary);
        var form = CreateForm(Path.Combine(Temporary, "new"));
        form.Shown += (_, _) => form.BeginInvoke(async () =>
        {
            try { await RunChecks(form); Console.WriteLine($"All {_passed} Windows checks passed."); }
            catch (Exception ex) { Console.WriteLine("FAIL: " + ex); Environment.ExitCode = 1; }
            finally
            {
                foreach (var open in Forms.Where(f => !f.IsDisposed).ToArray()) Exit(open);
                Directory.Delete(Temporary, true);
                Application.Exit();
            }
        });
        Application.Run(form);
    }

    private static async Task RunChecks(Form form)
    {
        var plain = new Account { Kind = "Password", Issuer = "Ordinary sample", Name = "demo", Secret = " space-P@ssword! " };
        var otp = new Account { Issuer = "Private OTP", Name = "demo", Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" };
        Check(Get<OpenVault?>(form, "_vault") is null && !Get<bool>(form, "_privateView"), "new startup opens homepage without asking for a password");
        Check(!Get<VaultStore>(form, "_store").Exists, "startup creates no master-password vault");
        Check((bool)Call(form, "Commit", new List<Account> { plain }, false)!, "ordinary entry can be saved while private vault is locked");
        Check(CardCount(form) == 1, "ordinary card is visible while private vault is locked");
        Call(form, "CopyCode", plain);
        Check(ReadClipboard() == plain.Secret, "one click copies ordinary password without authentication");

        var ordinaryPath = Path.Combine(Temporary, "new", "ordinary.local");
        var bytes = File.ReadAllBytes(ordinaryPath);
        Check(!Encoding.UTF8.GetString(bytes).Contains(plain.Secret) && !Encoding.Unicode.GetString(bytes).Contains(plain.Secret), "ordinary disk file contains no plaintext secret");
        Check(!File.Exists(ordinaryPath + ".bak"), "ordinary store creates no unprotected historical copy");
        var restarted = CreateForm(Path.Combine(Temporary, "new"));
        restarted.Show();
        Check(Get<VaultDocument>(restarted, "_ordinary").Accounts.Single().Secret == plain.Secret && CardCount(restarted) == 1,
            "ordinary entry reopens automatically after restart");
        Exit(restarted);

        var oldDirectory = Path.Combine(Temporary, "legacy");
        var oldStore = new VaultStore(Path.Combine(oldDirectory, "accounts.vault"));
        using (var oldVault = VaultCipher.Create("12345", new VaultDocument { Accounts = [otp] })) oldStore.Save(oldVault);
        var originalBytes = oldStore.Read();
        var legacy = CreateForm(oldDirectory);
        legacy.Show();
        Check(CardCount(legacy) == 0 && originalBytes.SequenceEqual(oldStore.Read()), "legacy entries stay hidden and unchanged on upgrade");
        Set(legacy, "_privateView", true); Call(legacy, "RenderCards");
        Check(CardCount(legacy) == 0 && !ContainsText(legacy, otp.Issuer), "locked private view exposes neither title nor OTP card");
        using (var fill = FillPasswordDialog("12345"))
            Check(await (Task<bool>)Call(legacy, "UnlockPrivateAsync")!, "actual unlock dialog accepts existing five-digit master password");
        Check(CardCount(legacy) == 1, "private content appears only after authentication");
        var indexedRestart = CreateForm(oldDirectory); indexedRestart.Show();
        Check(Cards(indexedRestart).Count() == 1 && Locked(Cards(indexedRestart).Single()), "private card is listed on startup without decrypting content");
        Check(CardAccount(Cards(indexedRestart).Single()).Issuer == otp.Issuer && CardAccount(Cards(indexedRestart).Single()).Secret.Length == 0,
            "homepage private card has a title and no OTP seed");
        Exit(indexedRestart);
        Call(legacy, "ShowAccountMenu", otp, new Point(20, 20));
        var accountMenu = Get<ContextMenuStrip>(legacy, "_accountMenu");
        var moveItem = accountMenu.Items[2];
        accountMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
        Check(!accountMenu.IsDisposed, "closing item menu keeps it alive until click dispatch completes");
        moveItem.PerformClick();
        await Task.Delay(100);
        Check(Get<VaultDocument>(legacy, "_ordinary").Accounts.Any(a => a.Id == otp.Id) &&
            !Get<OpenVault>(legacy, "_vault").Document.Accounts.Any(a => a.Id == otp.Id), "actual account menu moves legacy entry to ordinary storage");
        Call(legacy, "LockVault", "test");
        Check(Get<OpenVault?>(legacy, "_vault") is null && CardCount(legacy) == 1, "locking hides private vault while keeping ordinary entry visible");
        var unlockedCode = Totp.Generate(otp, DateTimeOffset.UtcNow);
        Call(legacy, "CopyCode", otp);
        Check(ReadClipboard() == unlockedCode || ReadClipboard() == Totp.Generate(otp, DateTimeOffset.UtcNow), "ordinary OTP copies while private vault remains locked");
        var freshLegacy = CreateForm(oldDirectory); freshLegacy.Show();
        Check(CardCount(freshLegacy) == 1 && Get<OpenVault?>(freshLegacy, "_vault") is null, "moved legacy entry needs no password on next startup");
        Exit(freshLegacy);
        using (var fill = FillPasswordDialog("12345")) await (Task<bool>)Call(legacy, "UnlockPrivateAsync")!;
        Check((bool)Call(legacy, "MoveAccount", otp, false)!, "ordinary entry can be protected again");
        Check(Get<VaultDocument>(legacy, "_ordinary").Accounts.Count == 0, "protecting removes automatically accessible source");
        Call(legacy, "LockVault", "test");
        var emptyClipboard = ReadClipboard();
        Call(legacy, "CopyCode", otp);
        Check(ReadClipboard() == emptyClipboard && Call(legacy, "FindAvailable", otp.Id) is null, "stale private card cannot copy after lock");
        var openedLockedEditor = false;
        using (var cancel = CancelPasswordDialog("EntryDialog", () => openedLockedEditor = true))
            await (Task)Call(legacy, "EditAccountAsync", otp, false)!;
        Check(!openedLockedEditor, "queued edit of a stale private entry cannot expose contents after locking");
        var protectedRestart = CreateForm(oldDirectory); protectedRestart.Show();
        Check(CardCount(protectedRestart) == 0 && Get<VaultDocument>(protectedRestart, "_ordinary").Accounts.Count == 0, "protected entry stays hidden after restart");
        using (var fill = FillPasswordDialog("12345")) await (Task<bool>)Call(protectedRestart, "UnlockPrivateAsync")!;
        Set(protectedRestart, "_privateView", true); Call(protectedRestart, "RenderCards");
        Check(CardCount(protectedRestart) == 1, "protected entry survives restart and authenticated reopen");

        foreach (var name in new[] { "EntryDialog", "StoredPasswordDialog" })
        {
            var type = App.GetType("LocalAuth.App." + name)!;
            using var normalDialog = (Form)Activator.CreateInstance(type, Flags, null, new object?[] { null, false }, null)!;
            using var privateDialog = (Form)Activator.CreateInstance(type, Flags, null, new object?[] { null, true }, null)!;
            Check(!(bool)type.GetProperty("RequireUnlock", Flags)!.GetValue(normalDialog)! && (bool)type.GetProperty("RequireUnlock", Flags)!.GetValue(privateDialog)!, name + " preserves chosen protection checkbox");
        }
        // A fresh user can create the private vault without changing ordinary data.
        using (var fill = FillPasswordDialog("54321")) Check(await (Task<bool>)Call(form, "UnlockPrivateAsync")!, "private vault can be configured on demand");
        Check(Get<VaultDocument>(form, "_ordinary").Accounts.Single().Secret == plain.Secret, "creating private vault preserves ordinary entry");
        Check((bool)Call(form, "Commit", new List<Account> { otp }, true)!, "private save works alongside ordinary entries");
        Check(Cards(form).Count() == 2 && Cards(form).Select(CardAccount).Any(a => a.IsPassword) && Cards(form).Select(CardAccount).Any(a => !a.IsPassword),
            "homepage shows both saved passwords and two-factor codes together");
        Set(form, "_lastActivity", Environment.TickCount64 - 16 * 60_000L);
        Call(form, "Tick");
        Check(Get<OpenVault?>(form, "_vault") is null && CardCount(form) == 1, "idle timeout locks only private vault");
        Check(Cards(form).Count() == 2 && Cards(form).Count(Locked) == 1, "locking retains all homepage cards and locks only private content");
        var homeRestart = CreateForm(Path.Combine(Temporary, "new")); homeRestart.Show();
        Check(Cards(homeRestart).Count() == 2 && Cards(homeRestart).Count(Locked) == 1 && Get<OpenVault?>(homeRestart, "_vault") is null,
            "restart lists all ordinary and private cards without a login gate");
        Exit(homeRestart);
        var indexPath = Path.Combine(Temporary, "new", "private-index.local");
        var indexBytes = File.ReadAllBytes(indexPath);
        var indexPlain = ProtectedData.Unprotect(indexBytes, Encoding.UTF8.GetBytes("LocalAuth|private-index|v1"), DataProtectionScope.CurrentUser);
        var indexJson = System.Text.Json.Nodes.JsonNode.Parse(indexPlain)!;
        var propertyNames = indexJson["entries"]![0]!.AsObject().Select(pair => pair.Key).OrderBy(k => k).ToArray();
        Check(propertyNames.SequenceEqual(new[] { "favorite", "id", "issuer", "kind", "name" }), "private index schema contains only display metadata");
        Check(!Encoding.UTF8.GetString(indexPlain).Contains(otp.Secret) && !Encoding.UTF8.GetString(indexPlain).Contains(plain.Secret),
            "automatically opened private index stores neither OTP seeds nor passwords");
        CryptographicOperations.ZeroMemory(indexPlain);
        Get<TextBox>(form, "_search").Text = otp.Issuer;
        Check(Cards(form).Count() == 1 && Locked(Cards(form).Single()), "homepage search can find a locked private card by name");
        Get<TextBox>(form, "_search").Clear();
        var lockedAccount = CardAccount(Cards(form).Single(Locked));
        var beforeCancel = ReadClipboard();
        using (var cancel = CancelPasswordDialog()) await (Task)Call(form, "CopyFromCardAsync", lockedAccount)!;
        Check(Get<OpenVault?>(form, "_vault") is null && ReadClipboard() == beforeCancel, "cancelled private-copy prompt leaves vault locked and clipboard unchanged");
        using (var fill = FillPasswordDialog("54321")) await (Task)Call(form, "CopyFromCardAsync", lockedAccount)!;
        Check(ReadClipboard() == Totp.Generate(otp, DateTimeOffset.UtcNow), "clicking a locked homepage card authenticates and copies its live code");
        Check(Cards(form).Count() == 2 && Cards(form).All(c => !Locked(c)), "unlock keeps all entries on the homepage");
        Call(form, "LockVault", "test");
        // Cache corruption is recoverable and never unlocks the private vault.
        File.WriteAllBytes(indexPath, new byte[] { 9, 8, 7 });
        var corruptIndexRestart = CreateForm(Path.Combine(Temporary, "new")); corruptIndexRestart.Show();
        Check(CardCount(corruptIndexRestart) == 1 && Get<OpenVault?>(corruptIndexRestart, "_vault") is null && Get<object?>(corruptIndexRestart, "_privateIndex") is null,
            "corrupt optional index falls back to sync prompt while ordinary entries remain usable");
        Exit(corruptIndexRestart);
        // Re-authentication repairs the index from the encrypted private vault.
        using (var fill = FillPasswordDialog("54321")) await (Task<bool>)Call(form, "UnlockPrivateAsync")!;
        var repaired = CreateForm(Path.Combine(Temporary, "new")); repaired.Show();
        Check(Cards(repaired).Count() == 2 && Cards(repaired).Count(Locked) == 1, "unlock rebuilds a damaged private metadata index");
        Exit(repaired);
        // A changed vault must invalidate the cached names.
        var storedVault = Get<VaultStore>(form, "_store");
        var snapshot = storedVault.Read();
        using (var replacement = VaultCipher.Create("54321", new VaultDocument { Accounts = [otp with { Issuer = "Renamed private OTP" }] })) storedVault.Save(replacement);
        var stale = CreateForm(Path.Combine(Temporary, "new")); stale.Show();
        Check(Get<object?>(stale, "_privateIndex") is null, "vault fingerprint rejects stale private names");
        Exit(stale);
        VaultStore.WriteAtomically(storedVault.FilePath, snapshot);
        Call(form, "LockVault", "test");
        Set(form, "_privateView", true); Call(form, "RenderCards");
        Get<TextBox>(form, "_search").Text = plain.Issuer;
        Check(CardCount(form) == 0, "search cannot expose entries through locked private view");
        Set(form, "_privateView", false); Call(form, "RenderCards");
        form.Close();
        Check(!form.IsDisposed && !form.Visible, "close hides to tray without exiting");
        var tray = Get<NotifyIcon>(form, "_tray");
        Check(tray.Visible && tray.Icon is not null, "background process keeps a visible notification-area icon");
        typeof(NotifyIcon).GetMethod("OnMouseClick", Flags)!.Invoke(tray, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
        Check(form.Visible && !Get<bool>(form, "_privateView") && Cards(form).Count() == 2, "single left click on tray opens homepage with all cards");
        form.Close();
        var menu = Get<ContextMenuStrip>(form, "_trayMenu");
        ((ToolStripMenuItem)menu.Items[0]).PerformClick();
        Check(form.Visible && CardCount(form) == 1, "tray restore opens ordinary cards without authentication");
        Check(!Get<bool>(form, "_privateView") && Cards(form).Count() == 2, "tray restore always opens homepage with all cards");
        await RunClipboardChecks();
        await RunPreferenceChecks();
        // DPAPI corruption must reject without overwriting the original bytes.
        File.WriteAllBytes(ordinaryPath, new byte[] { 0, 1, 2, 3 });
        try
        {
            Call(Get<object>(form, "_ordinaryStore"), "Read");
            throw new Exception("corrupted ordinary store was accepted");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is CryptographicException)
        {
            Check(File.ReadAllBytes(ordinaryPath).SequenceEqual(new byte[] { 0, 1, 2, 3 }), "corrupt ordinary file rejects without overwriting data");
        }
        ((ToolStripMenuItem)menu.Items[3]).PerformClick();
        Check(form.IsDisposed, "tray Exit closes and disposes the window");
    }

    private static Form CreateForm(string directory)
    {
        Directory.CreateDirectory(directory);
        var form = (Form)Activator.CreateInstance(MainType, Flags, null, new object[] { new VaultStore(Path.Combine(directory, "accounts.vault")) }, null)!;
        Forms.Add(form);
        return form;
    }
    private static object? Call(object value, string method, params object[] arguments) => value.GetType().GetMethod(method, Flags)!.Invoke(value, arguments);
    private static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Flags)!.GetValue(value)!;
    private static void Set(object value, string field, object contents) => value.GetType().GetField(field, Flags)!.SetValue(value, contents);
    private static IEnumerable<Control> Cards(Form form) => Get<FlowLayoutPanel>(form, "_cards").Controls.Cast<Control>().Where(c => c.GetType().Name == "CodeCard");
    private static bool Locked(Control card) => (bool)card.GetType().GetProperty("IsLocked", Flags)!.GetValue(card)!;
    private static Account CardAccount(Control card) => (Account)card.GetType().GetProperty("Account", Flags)!.GetValue(card)!;
    private static int CardCount(Form form) => Cards(form).Count(c => !Locked(c));
    private static bool ContainsText(Control parent, string text) => parent.Text.Contains(text) || parent.Controls.Cast<Control>().Any(c => ContainsText(c, text));
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static void Exit(Form form) { Set(form, "_exitRequested", true); form.Close(); }
    private static System.Windows.Forms.Timer FillPasswordDialog(string password)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "PasswordDialog");
            if (dialog is null) return;
            timer.Stop();
            foreach (var input in dialog.Controls.OfType<TextBox>()) input.Text = password;
            dialog.AcceptButton!.PerformClick();
        };
        timer.Start(); return timer;
    }
    private static System.Windows.Forms.Timer CancelPasswordDialog(string dialogType = "PasswordDialog", Action? onOpened = null)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == dialogType);
            if (dialog is null) return;
            timer.Stop(); onOpened?.Invoke(); dialog.DialogResult = DialogResult.Cancel;
        };
        timer.Start(); return timer;
    }
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    private static string ReadClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) throw new Exception("test cannot open clipboard");
        try
        {
            var memory = GetClipboardData(13);
            if (memory == IntPtr.Zero) return "";
            var data = GlobalLock(memory);
            try { return Marshal.PtrToStringUni(data) ?? ""; }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
}

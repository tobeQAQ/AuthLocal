using System.Runtime.InteropServices;
using LocalAuth.Core;

internal static partial class Program
{
    private static async Task RunSearchChecks()
    {
        var source = CreateForm(Path.Combine(Temporary, "search"));
        source.Show();
        var search = Get<TextBox>(source, "_search");
        var filter = Get<Button>(source, "_typeButton").ContextMenuStrip!;
        var password = new Account { Kind = "Password", Issuer = "Café cloud", Name = "alice@lab.test", Secret = "  mIx@! clé  " };
        var otp = new Account { Issuer = "Café cloud", Name = "build@lab.test", Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Favorite = true };
        var privatePassword = new Account { Kind = "Password", Issuer = "資料", Name = "テスト", Secret = "Synthetic-private@1" };
        Check((bool)Call(source, "Commit", new List<Account> { password, otp }, false)!, "search fixtures save to the ordinary store");
        using var receiver = new Form { Text = "Search paste target", Width = 420, Height = 160 };
        using var paste = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        receiver.Controls.Add(paste);
        receiver.Show();

        void FocusSearch()
        {
            source.Activate();
            if (!search.Focus()) throw new Exception("Search could not receive keyboard focus");
        }

        search.Text = "ＡＬＩＣＥ　ｃａｆｅ";
        Check(CardAccount(Cards(source).Single()).Id == password.Id,
            "search matches terms across account and service, regardless of order, accent, case or width");
        FocusSearch();
        PressSearchKey(search, Keys.Enter);
        await DrainSearchCommands(source);
        PasteAndCheck(paste, password.Secret, "Enter on the sole search result pastes the exact password");

        search.Text = password.Secret;
        var passwordHidden = !Cards(source).Any();
        search.Text = otp.Secret;
        Check(passwordHidden && !Cards(source).Any(), "search matches labels without searching stored passwords or OTP seeds");
        FocusSearch();
        PressSearchKey(search, Keys.Enter);
        await DrainSearchCommands(source);
        Check(ReadClipboard() == password.Secret, "Enter with no results leaves the clipboard alone");

        search.Text = "cafe";
        FocusSearch();
        PressSearchKey(search, Keys.Enter);
        await DrainSearchCommands(source);
        Check(Cards(source).Count() == 2 && ReadClipboard() == password.Secret,
            "ambiguous search requires explicit selection before copying");
        PressSearchKey(search, Keys.Down);
        var first = Cards(source).Single(card => card.Focused);
        Check(CardAccount(first).Id == otp.Id, "Down focuses the first visible card in the current sort order");
        PressSearchKey(first, Keys.Down);
        var second = Cards(source).Single(card => card.Focused);
        Check(CardAccount(second).Id == password.Id, "Down moves keyboard focus to the next matching card");
        PressSearchKey(second, Keys.Up);
        var before = Totp.Generate(otp, DateTimeOffset.UtcNow);
        PressSearchKey(first, Keys.Enter);
        await DrainSearchCommands(source);
        var copied = ReadClipboard();
        Check(copied == before || copied == Totp.Generate(otp, DateTimeOffset.UtcNow), "Enter on an explicitly focused card copies a current OTP");
        PressSearchKey(first, Keys.Escape);
        Check(search.TextLength == 0 && search.Focused, "Escape from a card clears the search and focuses its input");

        filter.Items[2].PerformClick();
        search.Text = "cafe";
        Check(CardAccount(Cards(source).Single()).Id == password.Id, "password filter combines with the current search query");
        FocusSearch();
        PressSearchKey(search, Keys.Enter);
        search.Text = "missing";
        await DrainSearchCommands(source);
        Check(ReadClipboard() == copied, "a queued Enter cannot copy a result removed by a new query");

        search.Text = "alice";
        SendSearchMessage(search.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero); // WM_IME_STARTCOMPOSITION
        DispatchSearchKey(search, Keys.Enter);
        DispatchSearchKey(search, Keys.Escape);
        await DrainSearchCommands(source);
        Check(search.Text == "alice" && ReadClipboard() == copied, "IME composition retains Enter and Escape for candidate selection");
        SendSearchMessage(search.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero); // WM_IME_ENDCOMPOSITION

        using (var fill = FillPasswordDialog("13579"))
            Check(await (Task<bool>)Call(source, "UnlockPrivateAsync")!, "search test creates a private vault");
        Check((bool)Call(source, "Commit", new List<Account> { privatePassword }, true)!, "private search fixture saves");
        Call(source, "LockVault", "test");
        search.Text = "テスト 資料";
        filter.Items[1].PerformClick();
        Check(!Cards(source).Any(), "code filter excludes locked passwords");
        filter.Items[2].PerformClick();
        Check(Locked(Cards(source).Single()), "password filter retains a matching locked card");
        FocusSearch();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var cancel = CancelPasswordDialog(onOpened: () => cancelled.TrySetResult()))
        {
            PressSearchKey(search, Keys.Enter);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Check(ReadClipboard() == copied && Get<OpenVault?>(source, "_vault") is null,
            "cancelling the private keyboard copy keeps the vault locked and clipboard unchanged");
        FocusSearch();
        using (var fill = FillPasswordDialog("13579"))
        {
            PressSearchKey(search, Keys.Enter);
            var deadline = Environment.TickCount64 + 5000;
            while (ReadClipboard() == copied && Environment.TickCount64 < deadline) await Task.Delay(50);
        }
        PasteAndCheck(paste, privatePassword.Secret, "keyboard copy of a private search result succeeds after authentication");
        Exit(source);
    }

    private static void PressSearchKey(Control target, Keys key)
    {
        if (!DispatchSearchKey(target, key)) throw new Exception($"Search did not handle {key}");
    }

    private static bool DispatchSearchKey(Control target, Keys key)
    {
        // Use the same WinForms command preprocessing as an actual WM_KEYDOWN.
        var message = Message.Create(target.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
        return target.PreProcessMessage(ref message);
    }

    private static Task DrainSearchCommands(Form source)
    {
        // Queue the marker on the same window as the keyboard copy callback.
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.BeginInvoke(() => drained.SetResult());
        return drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendSearchMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

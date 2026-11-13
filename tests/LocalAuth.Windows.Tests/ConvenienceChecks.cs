using System.Diagnostics;
using System.Reflection;
using LocalAuth.Core;

internal static partial class Program
{
    private static async Task RunConvenienceChecks()
    {
        var directory = Path.Combine(Temporary, "convenience");
        var activation = App.GetType("LocalAuth.App.ExistingInstance")!;
        bool Activate(string path) => (bool)Static(activation, "TryActivate", path)!;
        var source = CreateForm(directory);
        var refresh = Get<System.Windows.Forms.Timer>(source, "_timer");
        Check(!refresh.Enabled, "unshown window does not run a refresh timer");
        source.Show();
        Check(refresh.Enabled && refresh.Interval == 1000, "visible cards refresh once per second");

        var password = new Account { Kind = "Password", Issuer = "设置", Secret = " space-Test@1! Ω ", Favorite = true };
        var otp = new Account { Issuer = "A&B", Name = "OTP sample", Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Favorite = true };
        var other = new Account { Kind = "Password", Issuer = "Not a favorite", Secret = "Synthetic only" };
        Check((bool)Call(source, "Commit", new List<Account> { password, otp, other }, false)!, "quick-copy fixtures save to ordinary storage");
        using (var fill = FillPasswordDialog("13579"))
            Check(await (Task<bool>)Call(source, "UnlockPrivateAsync")!, "quick-copy test creates a five-digit private vault");
        var privatePassword = new Account { Kind = "Password", Issuer = "Private favorite", Secret = "Synthetic-private@1", Favorite = true };
        Check((bool)Call(source, "Commit", new List<Account> { privatePassword }, true)!, "private favorite saves and indexes");
        source.Close();
        Check(!refresh.Enabled && Get<OpenVault?>(source, "_vault") is null, "tray hiding stops refresh and locks private content");

        Call(source, "BuildQuickCopyMenu");
        var quick = Get<ToolStripMenuItem>(source, "_quickCopyMenu");
        ToolStripMenuItem[] Items() => quick.DropDownItems.OfType<ToolStripMenuItem>().Where(i => Equals(i.Tag, "UserContent")).ToArray();
        Check(Items().Length == 3 && !Items().Any(i => (i.Text ?? "").Contains(other.Issuer)), "tray lists only ordinary and private favorites");
        Check(!Items().Any(i => (i.Text ?? "").Contains(password.Secret) || (i.Text ?? "").Contains(privatePassword.Secret) || (i.Text ?? "").Contains(otp.Secret)), "tray menu contains names and no secrets");
        Check(Items().Any(i => (i.Text ?? "").Contains("A&&B")), "tray preserves literal ampersands in account names");
        Static(App.GetType("LocalAuth.App.UiText")!, "UseLanguage", "en", null);
        Static(App.GetType("LocalAuth.App.UiText")!, "Apply", quick.DropDown);
        Check(Items().Any(i => i.Text == "设置"), "tray account names are not translated as UI messages");

        using var receiver = new Form { Text = "Convenience paste target", Width = 440, Height = 160 };
        using var target = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        receiver.Controls.Add(target); receiver.Show();
        Items().Single(i => i.Text == "设置").PerformClick();
        await Task.Delay(100);
        PasteAndCheck(target, password.Secret, "actual ordinary tray menu click pastes exact password");
        Check(!source.Visible, "ordinary tray copy keeps the main window hidden");
        Check(Get<NotifyIcon>(source, "_tray").Text.Contains("Copied"), "successful tray copy has brief tooltip feedback");
        var feedback = Get<System.Windows.Forms.Timer>(source, "_copyFeedback");
        typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Flags)!.Invoke(feedback, new object[] { EventArgs.Empty });
        Check(!feedback.Enabled && !Get<NotifyIcon>(source, "_tray").Text.Contains("Copied"), "copy feedback stops its timer and restores tray tooltip");

        var before = Totp.Generate(otp, DateTimeOffset.UtcNow);
        Items().Single(i => (i.Text ?? "").Contains("A&&B")).PerformClick();
        await Task.Delay(100);
        var after = Totp.Generate(otp, DateTimeOffset.UtcNow);
        Check(ReadClipboard() == before || ReadClipboard() == after, "hidden-window tray copy generates a current OTP");
        Check(!refresh.Enabled, "copying from tray does not restart hidden card refresh");

        var oldPasswordItem = Items().Single(i => i.Text == "设置");
        Check((bool)Call(source, "Commit", new List<Account> { password with { Favorite = false }, otp, other }, false)!, "favorite can change while menu is open");
        var unchanged = ReadClipboard();
        oldPasswordItem.PerformClick();
        await Task.Delay(80);
        Check(ReadClipboard() == unchanged, "stale unfavorited menu item cannot replace the clipboard");
        Check((bool)Call(source, "Commit", new List<Account> { otp, other }, false)!, "ordinary favorite can be removed while menu is open");
        oldPasswordItem.PerformClick();
        await Task.Delay(80);
        Check(ReadClipboard() == unchanged, "stale deleted menu item cannot copy a captured password");

        using (var cancel = CancelPasswordDialog())
            await (Task)Call(source, "CopyFavoriteAsync", privatePassword.Id)!;
        Check(source.Visible && Get<OpenVault?>(source, "_vault") is null && ReadClipboard() == unchanged,
            "cancelled private tray copy opens the window but keeps vault and clipboard unchanged");
        source.Close();
        using (var fill = FillPasswordDialog("13579"))
            await (Task)Call(source, "CopyFavoriteAsync", privatePassword.Id)!;
        PasteAndCheck(target, privatePassword.Secret, "authenticated private tray copy pastes through a separate text box");
        Check(source.Visible && !Get<bool>(source, "_privateView"), "private tray copy opens the unified home");
        Call(source, "BuildQuickCopyMenu");
        var stalePrivate = Items().Single(i => (i.Text ?? "").Contains(privatePassword.Issuer));
        source.Close();
        unchanged = ReadClipboard();
        using (var cancel = CancelPasswordDialog())
        {
            stalePrivate.PerformClick();
            await Task.Delay(150);
        }
        Check(Get<OpenVault?>(source, "_vault") is null && ReadClipboard() == unchanged,
            "a private menu built before locking cannot bypass authentication afterward");

        source.Close();
        Check(!Activate(Path.Combine(Temporary, "different-profile")), "activation cannot target a different data directory");
        using (var child = new Process
        {
            StartInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }
        })
        {
            child.StartInfo.ArgumentList.Add("--activate");
            child.StartInfo.ArgumentList.Add(directory);
            Check(child.Start(), "a second process launches the actual activation client");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Delay(100);
            Check(child.ExitCode == 0 && source.Visible && source.WindowState == FormWindowState.Normal,
                "cross-process activation restores the existing hidden window");
        }
        Check(refresh.Enabled && Get<OpenVault?>(source, "_vault") is null, "activation restarts display updates without unlocking private content");
        Set(source, "_privateView", true); Call(source, "RenderCards");
        using (var modal = new Form { Text = "Unsaved editor sample", Width = 320, Height = 180 })
        {
            var sent = false;
            var preserved = false;
            modal.Shown += (_, _) => modal.BeginInvoke(async () =>
            {
                try
                {
                    sent = Activate(directory);
                    await Task.Delay(80);
                    preserved = modal.Visible && Get<bool>(source, "_privateView");
                }
                finally { modal.DialogResult = DialogResult.Cancel; }
            });
            modal.ShowDialog(source);
            Check(sent, "activation finds a window with an owned modal editor");
            Check(preserved, "activation preserves an open modal editor and its parent view");
        }
        source.WindowState = FormWindowState.Minimized;
        await Task.Delay(50);
        Check(!refresh.Enabled, "minimizing stops all card refresh wakeups");
        Check(Activate(directory), "activation finds minimized windows");
        await Task.Delay(80);
        Check(refresh.Enabled && source.Visible && source.WindowState == FormWindowState.Normal, "activation resumes live cards after minimize");
        Exit(source);
        Check(!Activate(directory), "closed windows remove their activation registration");

        // The first-use warning is also an activation target, without accepting it.
        using var warning = (Form)Activator.CreateInstance(App.GetType("LocalAuth.App.FirstRunDialog")!, true)!;
        var warningDirectory = Path.Combine(Temporary, "activation-warning");
        var count = 0;
        using (var registration = (IDisposable)Static(activation, "Register", warning, warningDirectory, new Action(() => count++))!)
        {
            warning.Show();
            Check(Activate(warningDirectory), "existing first-use warning can be activated");
            await Task.Delay(80);
            Check(count == 1 && !Get<CheckBox>(warning, "_acknowledge").Checked && !((Control)warning.AcceptButton!).Enabled,
                "activation does not acknowledge or bypass the first-use warning");
        }
        Check(!Activate(warningDirectory), "disposing activation registration removes discovery even for a live window");
        warning.Close();
    }
}

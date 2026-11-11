using System.Reflection;
using System.Runtime.InteropServices;
using LocalAuth.Core;
using Microsoft.Win32;

internal static partial class Program
{
    private static void PasteAndCheck(TextBox target, string expected, string name)
    {
        target.Text = "";
        target.Focus();
        target.Paste(); // Native EDIT WM_PASTE, the same control operation used by Ctrl+V.
        Check(target.Text == expected, name);
    }

    private static async Task RunClipboardChecks()
    {
        var source = CreateForm(Path.Combine(Temporary, "clipboard"));
        source.Show();
        using var receiver = new Form { Text = "Separate paste target", Width = 420, Height = 160 };
        using var target = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        receiver.Controls.Add(target);
        receiver.Show();
        var samples = new[]
        {
            "123@1",
            "AbCdEfG123",
            "aA0!@#$%^&*()_+-=[]{};:'\",.<>/?\\|`~",
            "  a@B  ",
            "中文密碼-éà-日本語🔑"
        };
        Account? last = null;
        for (var index = 0; index < samples.Length; index++)
        {
            var password = samples[index];
            var type = App.GetType("LocalAuth.App.StoredPasswordDialog")!;
            using (var dialog = (Form)Activator.CreateInstance(type, Flags, null, new object?[] { null, false }, null)!)
            {
                dialog.Shown += (_, _) => dialog.BeginInvoke(() =>
                {
                    var inputs = dialog.Controls.OfType<TextBox>().ToArray();
                    inputs[0].Text = "Synthetic password " + index;
                    inputs[1].Text = "test-only";
                    inputs[2].Text = password;
                    dialog.AcceptButton!.PerformClick();
                });
                Check(dialog.ShowDialog(source) == DialogResult.OK, $"password sample {index}: actual editor accepts characters");
                last = (Account)type.GetProperty("Result", Flags)!.GetValue(dialog)!;
                Check(last.Secret == password, $"password sample {index}: editor preserves exact value");
            }
            Check((bool)Call(source, "Commit", new List<Account> { last }, false)!, $"password sample {index}: save succeeds");
            var reopened = CreateForm(Path.Combine(Temporary, "clipboard")); reopened.Show();
            Check(Get<VaultDocument>(reopened, "_ordinary").Accounts.Single().Secret == password, $"password sample {index}: persistence preserves all characters");
            Exit(reopened);

            // Exercise the actual card click, rather than merely reading the vault.
            var card = Cards(source).Single();
            card.GetType().GetMethod("OnMouseClick", Flags)!.Invoke(card, new object[] { new MouseEventArgs(MouseButtons.Left, 1, card.Width - 60, 82, 0) });
            Check(Get<bool>(card, "_showCopied"), $"password sample {index}: successful click shows copy feedback");
            receiver.Activate();
            PasteAndCheck(target, password, $"password sample {index}: separate window can paste");
            source.WindowState = FormWindowState.Minimized;
            await Task.Delay(50);
            PasteAndCheck(target, password, $"password sample {index}: minimizing preserves paste");
            Call(source, "RestoreWindow");
            await (Task)Call(source, "CopyFromCardAsync", last)!;
            source.Close();
            PasteAndCheck(target, password, $"password sample {index}: close to tray preserves paste");
            Call(source, "RestoreWindow");
            Call(source, "LockVault", "test");
            PasteAndCheck(target, password, $"password sample {index}: explicit lock does not erase clipboard");
        }

        using (var fill = FillPasswordDialog("12345")) await (Task<bool>)Call(source, "UnlockPrivateAsync")!;
        var privatePassword = new Account { Kind = "Password", Issuer = "Private test only", Secret = "123@1" };
        Check((bool)Call(source, "Commit", new List<Account> { privatePassword }, true)!, "private test password saves");
        await (Task)Call(source, "CopyFromCardAsync", privatePassword)!;
        source.WindowState = FormWindowState.Minimized;
        await Task.Delay(50);
        Check(Get<OpenVault?>(source, "_vault") is null, "minimizing still locks private access");
        PasteAndCheck(target, privatePassword.Secret, "copied private password remains pasteable after minimizing");
        Call(source, "RestoreWindow");
        Call(source, "OnSessionSwitch", source, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
        await Task.Delay(50);
        PasteAndCheck(target, privatePassword.Secret, "session lock does not erase deliberately copied text");
        Call(source, "OnPowerModeChanged", source, new PowerModeChangedEventArgs(PowerModes.Suspend));
        await Task.Delay(50);
        PasteAndCheck(target, privatePassword.Secret, "sleep notification does not erase deliberately copied text");

        // Clipboard contention must report failure rather than returning success.
        var writerType = App.GetType("LocalAuth.App.ClipboardWriter")!;
        var writer = Activator.CreateInstance(writerType, true)!;
        using var clipboardOpened = new ManualResetEventSlim();
        using var releaseClipboard = new ManualResetEventSlim();
        var opened = false;
        var thread = new Thread(() =>
        {
            opened = OpenClipboard(IntPtr.Zero);
            clipboardOpened.Set();
            if (!opened) return;
            releaseClipboard.Wait();
            CloseClipboard();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Check(clipboardOpened.Wait(3000) && opened, "contention test holds clipboard from another thread");
        try
        {
            var failed = false;
            try { writerType.GetMethod("Copy")!.Invoke(writer, new object[] { "not copied", source.Handle }); }
            catch (TargetInvocationException ex) when (ex.InnerException is ExternalException) { failed = true; }
            Check(failed, "busy clipboard rejects copy instead of claiming success");
        }
        finally { releaseClipboard.Set(); thread.Join(); }
        PasteAndCheck(target, privatePassword.Secret, "failed clipboard write preserves previous contents");

        await (Task)Call(source, "CopyFromCardAsync", last!)!;
        var expected = last!.Secret;
        Console.WriteLine("Waiting past former 30-second expiry to verify clipboard persistence...");
        await Task.Delay(31_250);
        PasteAndCheck(target, expected, "clipboard is not automatically erased after 30 seconds");

        // Content subsequently copied in another window is also left alone.
        target.Text = "external replacement";
        target.SelectAll(); target.Copy();
        source.WindowState = FormWindowState.Minimized;
        Call(source, "LockVault", "test");
        PasteAndCheck(target, "external replacement", "application state changes preserve another window's copied text");
        Call(source, "RestoreWindow");
        await (Task)Call(source, "CopyFromCardAsync", last)!;
        Exit(source);
        PasteAndCheck(target, expected, "copied password survives complete application exit");
    }
}

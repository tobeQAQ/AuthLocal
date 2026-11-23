using System.Text;
using LocalAuth.Core;

internal static partial class Program
{
    private static async Task RunLayoutChecks()
    {
        var folder = Path.Combine(Temporary, "entry-layout");
        var form = CreateForm(folder);
        form.Show();
        var a = new Account { Kind = "Password", Issuer = "Alpha", Secret = "  sample@alpha  " };
        var b = new Account { Kind = "Password", Issuer = "Beta", Secret = "example-only-beta" };
        var secret = new Account { Kind = "Password", Issuer = "Locked example", Secret = "example-only-private" };
        Check((bool)Call(form, "Commit", new List<Account> { a, b }, false)!, "layout test saves ordinary fixtures");
        using (var fill = FillPasswordDialog("13579")) await (Task<bool>)Call(form, "UnlockPrivateAsync")!;
        Check((bool)Call(form, "Commit", new List<Account> { secret }, true)!, "layout test saves a private fixture");
        Call(form, "LockVault", "test");
        var privateFile = Path.Combine(folder, "accounts.vault");
        var ordinaryFile = Path.Combine(folder, "ordinary.local");
        var vaultBytes = File.ReadAllBytes(privateFile);
        var ordinaryBytes = File.ReadAllBytes(ordinaryFile);

        Call(form, "CopyCode", a);
        var gripCard = Cards(form).First(c => CardAccount(c).Id == b.Id);
        var dpiScale = gripCard.DeviceDpi / 96f;
        Call(gripCard, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, gripCard.Width - (int)(100 * dpiScale), (int)(25 * dpiScale), 0));
        Check(ReadClipboard() == a.Secret, "clicking the drag grip does not copy an entry");

        var locked = Cards(form).Single(Locked);
        Call(form, "ShowAccountMenu", CardAccount(locked), form.PointToScreen(new Point(150, 70)));
        var menu = Get<ContextMenuStrip>(form, "_accountMenu");
        var editItem = (ToolStripMenuItem)menu.Items[^1];
        using (var edit = WhenDialog("AppearanceDialog", dialog =>
        {
            dialog.Controls.OfType<TextBox>().Single().Text = "Work";
            dialog.Controls.OfType<ComboBox>().Single().SelectedIndex = 2;
            dialog.AcceptButton!.PerformClick();
        }))
        {
            menu.Close(ToolStripDropDownCloseReason.ItemClicked);
            editItem.PerformClick();
            var deadline = Environment.TickCount64 + 5000;
            while (!File.Exists(Path.Combine(folder, "entry-layout.local")) && Environment.TickCount64 < deadline)
                await Task.Delay(50);
        }
        var list = Get<FlowLayoutPanel>(form, "_cards");
        Check(list.Controls.OfType<Label>().Any(l => l.Text == "Work") && Locked(Cards(form).First()),
            "locked entry can receive a group from its actual card menu without unlocking");
        var grouped = Cards(form).First();
        Check(Property<int>(Get<object>(grouped, "_appearance"), "ColorIndex") == 2, "assigned color reaches the locked card");
        Check((bool)Call(form, "ReorderEntries", b.Id, secret.Id, false)!, "card drop can move an entry into another group");
        Check(Cards(form).Select(c => CardAccount(c).Id).SequenceEqual(new[] { b.Id, secret.Id, a.Id }), "group members are rendered in the chosen order");
        Check((bool)Call(form, "ReorderEntries", secret.Id, b.Id, false)!, "card drop can reorder entries within a group");
        Check(ReadClipboard() == a.Secret && vaultBytes.SequenceEqual(File.ReadAllBytes(privateFile)) && ordinaryBytes.SequenceEqual(File.ReadAllBytes(ordinaryFile)),
            "layout changes preserve clipboard and encrypted credential files");
        var storeBytes = File.ReadAllBytes(Path.Combine(folder, "entry-layout.local"));
        Check(!Encoding.UTF8.GetString(storeBytes).Contains("Work"), "local layout is protected at rest");
        Exit(form);

        var restarted = CreateForm(folder);
        restarted.Show();
        var cards = Cards(restarted).ToArray();
        Check(cards.Select(c => CardAccount(c).Id).SequenceEqual(new[] { secret.Id, b.Id, a.Id }), "group layout and manual order persist after restart");
        Check(Locked(cards[0]) && CardAccount(cards[0]).Secret.Length == 0 && Property<int>(Get<object>(cards[0], "_appearance"), "ColorIndex") == 2,
            "layout reload preserves the color and private lock");
        Call(restarted, "CopyCode", b);
        Check(ReadClipboard() == b.Secret, "reordered entry still copies the correct password");
        Exit(restarted);
    }
}

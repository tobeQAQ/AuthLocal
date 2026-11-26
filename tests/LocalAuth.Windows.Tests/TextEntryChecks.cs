using LocalAuth.Core;

internal static partial class Program
{
    private static async Task RunTextEntryChecks()
    {
        var folder = Path.Combine(Temporary, "text-entry");
        var form = CreateForm(folder);
        form.Show();
        var body = "  " + string.Concat(Enumerable.Repeat("示例・Résumé 🌙\r\n\tIndented line  \r\n\r\n", 3900)) + " last  ";
        var resized = false;
        using (var edit = WhenDialog("TextEntryDialog", dialog =>
        {
            var inputs = dialog.Controls.OfType<TextBox>().ToArray();
            var field = inputs.Single(t => t.Multiline);
            inputs.Single(t => !t.Multiline).Text = "Long text fixture";
            field.Text = body;
            var before = field.Size;
            dialog.ClientSize += new Size(110, 85);
            dialog.PerformLayout();
            resized = field.AcceptsReturn && field.AcceptsTab && field.Width > before.Width && field.Height > before.Height;
            dialog.AcceptButton!.PerformClick();
        })) await (Task)Call(form, "EditAccountAsync", null!, "Text")!;
        Check(resized, "multiline text editor expands in both directions");
        var card = Cards(form).Single();
        var note = CardAccount(card);
        Check(note.Kind == "Text" && note.Secret == body && body.Length > 100_000, "real add dialog saves long text verbatim");
        Call(card, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 45, 97, 0));
        Check(ReadClipboard() == body, "one click copies the full multiline note");
        using var receiver = new Form { Text = "TXT paste target", Width = 480, Height = 190 };
        using var target = new TextBox { Multiline = true, Dock = DockStyle.Fill, MaxLength = Account.MaxTextLength };
        receiver.Controls.Add(target);
        receiver.Show();
        receiver.Activate();
        PasteAndCheck(target, body, "long note pastes into another window with whitespace intact");
        Exit(form);
        form = CreateForm(folder);
        form.Show();
        Check(CardAccount(Cards(form).Single()).Secret == body, "long saved text survives restart");

        var updated = body + "\r\n\tEdited  ";
        using (var edit = WhenDialog("TextEntryDialog", dialog =>
        {
            dialog.Controls.OfType<TextBox>().Single(t => t.Multiline).AppendText("\r\n\tEdited  ");
            dialog.Controls.OfType<CheckBox>().Single().Checked = true;
            var msg = Message.Create(dialog.Handle, 0x0100, (IntPtr)Keys.S, IntPtr.Zero);
            Call(dialog, "ProcessCmdKey", msg, Keys.Control | Keys.S);
        }))
        using (var fill = FillPasswordDialog("13579"))
            await (Task)Call(form, "EditAccountAsync", note, "TOTP")!;
        Check(CardAccount(Cards(form).Single()).Id == note.Id && CardAccount(Cards(form).Single()).Secret == updated,
            "editing text with Ctrl+S can move it to private storage while keeping the same ID");
        Call(form, "LockVault", "test");
        card = Cards(form).Single();
        Check(Locked(card) && CardAccount(card).Kind == "Text" && CardAccount(card).Secret.Length == 0,
            "locked text index contains the title and type without any body");
        var filter = Get<Button>(form, "_typeButton").ContextMenuStrip!;
        filter.Items[1].PerformClick();
        Check(!Cards(form).Any(), "code filter excludes plain text");
        filter.Items[3].PerformClick();
        Check(Cards(form).Count() == 1 && Locked(Cards(form).Single()), "text filter includes locked text entries");
        using (var cancel = CancelPasswordDialog())
            await (Task)Call(form, "CopyFromCardAsync", CardAccount(Cards(form).Single()))!;
        Check(ReadClipboard() == body, "cancelling private text unlock keeps the clipboard intact");
        using (var fill = FillPasswordDialog("13579"))
            await (Task)Call(form, "CopyFromCardAsync", CardAccount(Cards(form).Single()))!;
        Check(ReadClipboard() == updated, "unlocking a private note copies its edited contents in full");
        Exit(form);
    }
}

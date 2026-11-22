namespace LocalAuth.App;

internal sealed class SearchBox : TextBox
{
    internal bool IsComposing { get; private set; }

    internal SearchBox()
    {
        Font = Theme.Font(11);
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        MaxLength = 4096;
    }

    protected override void WndProc(ref Message message)
    {
        // Let the IME use arrows, Enter and Escape while choosing input candidates.
        const int startComposition = 0x010D, endComposition = 0x010E, killFocus = 0x0008;
        if (message.Msg == startComposition) IsComposing = true;
        if (message.Msg is endComposition or killFocus) IsComposing = false;
        base.WndProc(ref message);
    }
}

namespace LocalAuth.App;

internal sealed class AppearanceDialog : Form
{
    internal EntryAppearance Result { get; private set; }

    internal AppearanceDialog(EntryAppearance current, IEnumerable<string> groups)
    {
        Result = current;
        Theme.ConfigureDialog(this, "分组和颜色", new Size(460, 302));
        Controls.Add(Theme.Label("分组和颜色", 24, 14, 410, 36, 16, style: FontStyle.Bold));
        Controls.Add(Theme.Label("分组名称", 24, 65, 410, 23));
        var group = Theme.TextBox(24, 91, 410);
        group.Text = current.Group;
        group.MaxLength = 48;
        group.PlaceholderText = "留空表示未分组";
        group.AutoCompleteMode = AutoCompleteMode.Suggest;
        group.AutoCompleteSource = AutoCompleteSource.CustomSource;
        var suggestions = new AutoCompleteStringCollection();
        suggestions.AddRange(groups.ToArray());
        group.AutoCompleteCustomSource = suggestions;
        Controls.Add(group);
        Controls.Add(Theme.Label("条目颜色", 24, 142, 410, 24));
        var color = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, 172), Width = 360 };
        color.Items.AddRange(EntryAppearance.Palette.Select(c => (object)c.Name).ToArray());
        color.SelectedIndex = current.ColorIndex;
        var swatch = new Panel { Location = new Point(402, 172), Size = new Size(32, 25) };
        void UpdateColor() => swatch.BackColor = new EntryAppearance(ColorIndex: Math.Max(0, color.SelectedIndex)).Accent;
        color.SelectedIndexChanged += (_, _) => UpdateColor();
        Shown += (_, _) => UpdateColor();
        var cancel = Theme.Button("取消", 206, 232, 108);
        cancel.DialogResult = DialogResult.Cancel;
        var save = Theme.Button("保存", 326, 232, 108, true);
        save.Click += (_, _) =>
        {
            Result = new EntryAppearance(group.Text.Trim(), color.SelectedIndex);
            if (Result.IsValid) DialogResult = DialogResult.OK;
        };
        Controls.AddRange([color, swatch, cancel, save]);
        AcceptButton = save;
        CancelButton = cancel;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed record EntryAppearance(string Group = "", int ColorIndex = 0)
{
    internal static readonly (string Name, Color Light, Color Dark)[] Palette =
    [
        ("默认颜色", Color.Empty, Color.Empty),
        ("蓝色", Color.FromArgb(29, 91, 173), Color.FromArgb(143, 194, 255)),
        ("紫色", Color.FromArgb(113, 61, 168), Color.FromArgb(204, 174, 253)),
        ("橙色", Color.FromArgb(155, 76, 24), Color.FromArgb(250, 179, 120)),
        ("红色", Color.FromArgb(172, 54, 68), Color.FromArgb(254, 155, 166)),
        ("绿色", Color.FromArgb(40, 112, 66), Color.FromArgb(162, 220, 154)),
        ("灰色", Color.FromArgb(86, 102, 119), Color.FromArgb(182, 199, 216))
    ];

    internal System.Drawing.Color Accent => ColorIndex == 0 ? Theme.Accent : Theme.IsDark ? Palette[ColorIndex].Dark : Palette[ColorIndex].Light;
    internal bool IsValid => Group is { Length: <= 48 } && !Group.Any(char.IsControl) && ColorIndex >= 0 && ColorIndex < Palette.Length;
}

// Only IDs, group labels, colors and display order belong in this file.
internal sealed record EntryLayout
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalAuth|entry-layout|v1");
    public List<Guid> Order { get; init; } = [];
    public Dictionary<Guid, EntryAppearance> Appearance { get; init; } = [];

    internal EntryAppearance For(Guid id) => Appearance.GetValueOrDefault(id) ?? new();

    internal EntryLayout WithAppearance(Guid id, EntryAppearance appearance)
    {
        var next = new Dictionary<Guid, EntryAppearance>(Appearance);
        if (appearance == new EntryAppearance()) next.Remove(id);
        else next[id] = appearance;
        return this with { Appearance = next };
    }

    internal EntryLayout Move(IEnumerable<Guid> displayOrder, Guid source, Guid target, bool after)
    {
        var order = displayOrder.ToList();
        order.Remove(source);
        var index = order.IndexOf(target);
        if (index < 0) return this;
        order.Insert(index + (after ? 1 : 0), source);
        // IDs for entries absent from the current index can return on unlock.
        var known = order.ToHashSet();
        order.AddRange(Order.Where(id => !known.Contains(id)));
        return WithAppearance(source, For(source) with { Group = For(target).Group }) with { Order = order };
    }

    internal static EntryLayout Load(string path)
    {
        if (!File.Exists(path)) return new();
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(VaultStore.ReadBounded(path), Entropy, DataProtectionScope.CurrentUser);
            var layout = JsonSerializer.Deserialize<EntryLayout>(plaintext);
            if (layout is not null && layout.Order is not null && layout.Appearance is not null &&
                layout.Order.Distinct().Count() == layout.Order.Count && layout.Appearance.Values.All(a => a?.IsValid == true))
                return layout;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException) { }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
        return new();
    }

    internal void Save(string path)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(this);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            if (encrypted.Length > VaultCipher.MaxFileSize) throw new FormatException("排列设置过大。");
            VaultStore.WriteAtomically(path, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}

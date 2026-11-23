using LocalAuth.Core;

namespace LocalAuth.App;

internal sealed partial class MainForm
{
    private readonly string _layoutPath;
    private EntryLayout _layout;
    private CodeCard? _dragSource;
    private (CodeCard Card, bool After)? _drop;

    private IEnumerable<(Account Account, bool Private, bool Locked)> OrderedEntries()
    {
        var entries = _ordinary.Accounts.Select(a => (Account: a, Private: false, Locked: false));
        if (_vault is not null)
            entries = entries.Concat(_vault.Document.Accounts.Select(a => (a, true, false)));
        else if (_privateIndex is not null)
            entries = entries.Concat(_privateIndex.Entries.Select(e => (e.ToLockedAccount(), true, true)));
        var ranks = _layout.Order.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        return entries.OrderBy(e => ranks.GetValueOrDefault(e.Account.Id, int.MaxValue))
            .ThenByDescending(e => e.Account.Favorite)
            .ThenBy(e => e.Account.Issuer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Account.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private bool SaveLayout(EntryLayout next)
    {
        try { next.Save(_layoutPath); }
        catch (Exception ex) when (Expected(ex))
        {
            ShowError("无法保存条目排列，请检查文件权限或磁盘空间。");
            return false;
        }
        _layout = next;
        var scroll = _cards?.AutoScrollPosition ?? Point.Empty;
        RenderCards();
        if (_cards is not null) _cards.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        return true;
    }

    private void EditAppearance(Guid id)
    {
        if (_busy || _closing || !OrderedEntries().Any(e => e.Account.Id == id)) return;
        var generation = _generation;
        var groups = _layout.Appearance.Values.Select(a => a.Group).Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase);
        using var dialog = new AppearanceDialog(_layout.For(id), groups);
        if (dialog.ShowDialog(this) == DialogResult.OK && !_closing && generation == _generation)
            SaveLayout(_layout.WithAppearance(id, dialog.Result));
    }

    private void WireReordering(CodeCard card)
    {
        card.AllowDrop = true;
        card.ReorderRequested += StartReorder;
        card.DragEnter += (_, e) => PreviewDrop(card, e);
        card.DragOver += (_, e) => PreviewDrop(card, e);
        card.DragLeave += (_, _) => card.ShowDrop(null);
        card.DragDrop += (_, e) =>
        {
            PreviewDrop(card, e);
            if (e.Effect == DragDropEffects.Move) _drop = (card, card.PointToClient(new Point(e.X, e.Y)).X >= card.Width / 2);
            card.ShowDrop(null);
        };
    }

    private void PreviewDrop(CodeCard card, DragEventArgs e)
    {
        var valid = _dragSource is { IsDisposed: false } source && source.Parent == _cards && card.Parent == _cards &&
            source.Account.Id != card.Account.Id && (e.AllowedEffect & DragDropEffects.Move) != 0;
        e.Effect = valid ? DragDropEffects.Move : DragDropEffects.None;
        card.ShowDrop(valid ? card.PointToClient(new Point(e.X, e.Y)).X >= card.Width / 2 : null);
    }

    private void StartReorder(CodeCard source)
    {
        if (_closing || _busy || _dragSource is not null) return;
        _dragSource = source;
        _drop = null;
        try { source.DoDragDrop(new DataObject("AuthLocal.EntryLayout", "1"), DragDropEffects.Move); }
        finally { _dragSource = null; }
        var drop = _drop;
        _drop = null;
        if (!_closing && !source.IsDisposed && drop is { Card.IsDisposed: false } destination)
            ReorderEntries(source.Account.Id, destination.Card.Account.Id, destination.After);
    }

    private bool ReorderEntries(Guid source, Guid target, bool after)
    {
        var ids = OrderedEntries().Select(e => e.Account.Id).Distinct().ToArray();
        if (source == target || !ids.Contains(source) || !ids.Contains(target)) return false;
        return SaveLayout(_layout.Move(ids, source, target, after));
    }
}

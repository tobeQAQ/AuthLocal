namespace LocalAuth.Core;

public static class EntryTransfer
{
    // Save the destination first so an interrupted move cannot lose the entry.
    // If source removal fails, roll back the destination and report failure.
    public static void Move(Account account, IReadOnlyList<Account> source, IReadOnlyList<Account> destination,
        Action<List<Account>> saveSource, Action<List<Account>> saveDestination)
    {
        if (!source.Any(a => a.Id == account.Id)) throw new FormatException("原条目已不存在，请重新选择。");
        if (destination.Any(a => a.Id == account.Id || a.SameCredential(account)))
            throw new FormatException("目标分区已存在此条目，请先处理重复条目。");
        saveDestination([.. destination, account]);
        try { saveSource(source.Where(a => a.Id != account.Id).ToList()); }
        catch (Exception sourceError)
        {
            try { saveDestination(destination.ToList()); }
            catch (Exception rollbackError)
            {
                throw new IOException("移动未完成，两个分区都保留了副本；请检查文件权限后处理重复条目。", new AggregateException(sourceError, rollbackError));
            }
            throw;
        }
    }
}

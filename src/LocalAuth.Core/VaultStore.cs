namespace LocalAuth.Core;

public sealed class VaultStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);
    public string BackupPath => FilePath + ".bak";
    public bool Exists => File.Exists(FilePath);

    public byte[] Read() => ReadBounded(FilePath);

    public static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > VaultCipher.MaxFileSize) throw new FormatException("文件过大，无法读取。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Save(OpenVault vault) => WriteAtomically(FilePath, vault.Seal(), keepBackup: true);

    public static void WriteAtomically(string path, byte[] bytes, bool keepBackup = false)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, keepBackup ? path + ".bak" : null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

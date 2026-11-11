using System.Runtime.InteropServices;
using LocalAuth.Core;

namespace LocalAuth.App;

internal static class Program
{
    internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalAuth");

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 10F));
        Directory.CreateDirectory(DataDirectory);
        var settingsPath = Path.Combine(DataDirectory, "settings.json");
        var preferences = AppSettings.Load(settingsPath);
        UiText.UseLanguage(preferences.Language);
        Theme.SetMode(preferences.ThemeMode);
        FileStream fileLock;
        try
        {
            fileLock = new FileStream(Path.Combine(DataDirectory, "app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            UiText.Message("AuthLocal 已在运行，请切换到已打开的窗口。", "AuthLocal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (fileLock)
        {
            // A stable installer-visible name survives product/executable renames.
            using var applicationMutex = new Mutex(false, @"Local\AuthLocal.App");
            Application.ThreadException += (_, error) =>
            {
                System.Diagnostics.Debug.WriteLine(error.Exception);
                UiText.Message("操作遇到意外错误，请重试。请勿删除本机数据文件。", "AuthLocal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try
            {
                if (!FirstRunGate.Ensure(settingsPath)) return;
                Application.Run(new MainForm(new VaultStore(Path.Combine(DataDirectory, "accounts.vault"))));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or FormatException)
            {
                UiText.Message("无法读取普通区数据。请在原 Windows 用户下打开，或从导出的备份恢复。\n原文件未被覆盖。\n\n" + ex.Message, "AuthLocal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

internal sealed class ClipboardWriter
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    public void Copy(string text, IntPtr owner)
    {
        var characters = (text + '\0').ToCharArray();
        var memory = GlobalAlloc(0x0002, checked((nuint)(characters.Length * sizeof(char))));
        if (memory == IntPtr.Zero) throw new ExternalException("无法分配剪贴板内存。");
        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) throw new ExternalException("无法访问剪贴板内存。");
            try { Marshal.Copy(characters, 0, pointer, characters.Length); }
            finally { GlobalUnlock(memory); }
            if (!TryOpen(owner)) throw new ExternalException("无法打开剪贴板。");
            try
            {
                if (!EmptyClipboard() || SetClipboardData(13, memory) == IntPtr.Zero)
                    throw new ExternalException("无法写入剪贴板。");
                memory = IntPtr.Zero; // Windows owns the movable allocation after SetClipboardData succeeds.
            }
            finally { CloseClipboard(); }
        }
        finally
        {
            Array.Clear(characters);
            if (memory != IntPtr.Zero) GlobalFree(memory);
        }
    }

    private static bool TryOpen(IntPtr owner)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

}

internal sealed class ActivityFilter(Action activity) : IMessageFilter
{
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is 0x0100 or 0x0200 or 0x0201 or 0x0204 or 0x020A) activity();
        return false;
    }
}

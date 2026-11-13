using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LocalAuth.App;

// Activation messages contain no credentials. Window properties distinguish data
// directories, including isolated test profiles, without polling or worker threads.
internal static class ExistingInstance
{
    private static readonly uint ShowMessage = RegisterWindowMessage("AuthLocal.ShowExistingWindow.v1");
    private delegate bool WindowVisitor(IntPtr window, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visitor, IntPtr state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RemoveProp(IntPtr window, string name);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string PropertyName(string directory) => "AuthLocal.Window." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\', '/').ToUpperInvariant())));

    internal static IDisposable Register(Form form, string directory, Action show) => new Registration(form, PropertyName(directory), show);

    internal static bool TryActivate(string directory)
    {
        if (ShowMessage == 0) return false;
        var key = PropertyName(directory);
        var target = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (GetProp(window, key) == IntPtr.Zero) return true;
            target = window;
            return false;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) return false;
        GetWindowThreadProcessId(target, out var processId);
        AllowSetForegroundWindow(processId);
        return PostMessage(target, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private sealed class Registration : NativeWindow, IDisposable
    {
        private readonly Form _form;
        private readonly string _key;
        private readonly Action _show;

        internal Registration(Form form, string key, Action show)
        {
            _form = form; _key = key; _show = show;
            form.HandleCreated += Attach;
            form.HandleDestroyed += Detach;
            if (form.IsHandleCreated) Attach(form, EventArgs.Empty);
        }

        private void Attach(object? sender, EventArgs e)
        {
            AssignHandle(_form.Handle);
            SetProp(Handle, _key, (IntPtr)1);
        }

        private void Detach(object? sender, EventArgs e)
        {
            if (Handle == IntPtr.Zero) return;
            RemoveProp(Handle, _key);
            ReleaseHandle();
        }

        protected override void WndProc(ref Message message)
        {
            if (ShowMessage != 0 && message.Msg == ShowMessage && !_form.IsDisposed)
            {
                _show();
                message.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            _form.HandleCreated -= Attach;
            _form.HandleDestroyed -= Detach;
            Detach(null, EventArgs.Empty);
        }
    }
}

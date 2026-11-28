using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Proof.Spike.Probe;

/// <summary>
/// The few native calls the probe makes: opening another process (Windows), the clipboard and the
/// desktop's windows (Windows), another process's path (macOS).
/// </summary>
internal static class NativeMethods
{
    #region Constants

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint CF_UNICODETEXT = 13;
    public const uint GW_CHILD = 5;
    public const uint GW_HWNDNEXT = 2;

    #endregion

    #region Windows

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetForegroundWindow();

    #endregion

    #region macOS

    [DllImport("/usr/lib/libproc.dylib", SetLastError = true)]
    public static extern unsafe int proc_pidpath(int pid, byte* buffer, uint size);

    #endregion
}

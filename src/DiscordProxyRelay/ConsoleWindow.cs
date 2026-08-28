using System.Runtime.InteropServices;

namespace DiscordProxyRelay;

public static class ConsoleWindow
{
    public static void Hide()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HideCore(FreeConsole, GetConsoleWindow, ShowWindow);
    }

    internal static void HideCore(
        Func<bool> freeConsole,
        Func<IntPtr> getConsoleWindow,
        Func<IntPtr, int, bool> showWindow)
    {
        if (freeConsole())
        {
            return;
        }

        var window = getConsoleWindow();
        if (window != IntPtr.Zero)
        {
            showWindow(window, 0);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}

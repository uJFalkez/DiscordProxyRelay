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

        var window = GetConsoleWindow();
        if (window != IntPtr.Zero)
        {
            ShowWindow(window, 0);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}

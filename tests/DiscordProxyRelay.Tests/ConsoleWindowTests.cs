namespace DiscordProxyRelay.Tests;

public sealed class ConsoleWindowTests
{
    [Fact]
    public void HideCoreStopsAfterSuccessfulDetach()
    {
        var getWindowCalled = false;
        var showWindowCalled = false;

        ConsoleWindow.HideCore(
            () => true,
            () => { getWindowCalled = true; return new IntPtr(1); },
            (_, _) => { showWindowCalled = true; return true; });

        Assert.False(getWindowCalled);
        Assert.False(showWindowCalled);
    }

    [Fact]
    public void HideCoreUsesLegacyFallbackWhenDetachFails()
    {
        var window = new IntPtr(123);
        IntPtr shownWindow = IntPtr.Zero;
        var shownCommand = -1;

        ConsoleWindow.HideCore(
            () => false,
            () => window,
            (handle, command) =>
            {
                shownWindow = handle;
                shownCommand = command;
                return true;
            });

        Assert.Equal(window, shownWindow);
        Assert.Equal(0, shownCommand);
    }

    [Fact]
    public void HideCoreDoesNothingWhenDetachFailsWithoutConsoleWindow()
    {
        var showWindowCalled = false;

        ConsoleWindow.HideCore(
            () => false,
            () => IntPtr.Zero,
            (_, _) => { showWindowCalled = true; return true; });

        Assert.False(showWindowCalled);
    }
}

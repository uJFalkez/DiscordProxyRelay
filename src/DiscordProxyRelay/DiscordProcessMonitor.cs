using System.Diagnostics;

namespace DiscordProxyRelay;

internal enum DiscordProcessState
{
    Running,
    Stopped,
    Unknown,
}

internal interface IDiscordProcessSnapshot : IDisposable
{
    int SessionId { get; }
    bool HasExited { get; }
}

internal static class DiscordProcessMonitor
{
    internal static DiscordProcessState Inspect() =>
        InspectProcesses(GetCurrentSessionId, GetDiscordProcesses);

    internal static DiscordProcessState InspectProcesses(
        Func<int> currentSessionSource,
        Func<IReadOnlyList<IDiscordProcessSnapshot>> processSource)
    {
        int currentSessionId;
        IReadOnlyList<IDiscordProcessSnapshot> processes;
        try
        {
            currentSessionId = currentSessionSource();
            processes = processSource();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return DiscordProcessState.Unknown;
        }

        if (processes is null)
        {
            return DiscordProcessState.Unknown;
        }

        var running = false;
        var unknown = false;
        OperationCanceledException? cancellation = null;
        foreach (var process in processes)
        {
            try
            {
                var sessionId = process.SessionId;
                var hasExited = process.HasExited;
                running |= sessionId == currentSessionId && !hasExited;
            }
            catch (OperationCanceledException exception)
            {
                cancellation ??= exception;
            }
            catch
            {
                unknown = true;
            }

            try
            {
                process.Dispose();
            }
            catch (OperationCanceledException exception)
            {
                cancellation ??= exception;
            }
            catch
            {
                unknown = true;
            }
        }

        if (cancellation is not null)
        {
            throw cancellation;
        }

        return running ? DiscordProcessState.Running : unknown ? DiscordProcessState.Unknown : DiscordProcessState.Stopped;
    }

    internal static Task WaitUntilStoppedAsync(CancellationToken cancellationToken) =>
        WaitUntilStoppedAsync(Inspect, () => DateTimeOffset.UtcNow, Task.Delay, cancellationToken);

    internal static async Task WaitUntilStoppedAsync(
        Func<DiscordProcessState> inspect,
        Func<DateTimeOffset> getUtcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? firstStopped = null;
        while (true)
        {
            var state = inspect();
            if (state == DiscordProcessState.Stopped)
            {
                firstStopped ??= getUtcNow();
                if (getUtcNow() - firstStopped >= TimeSpan.FromSeconds(2))
                {
                    return;
                }
            }
            else
            {
                firstStopped = null;
            }

            await delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static int GetCurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static IReadOnlyList<IDiscordProcessSnapshot> GetDiscordProcesses()
    {
        var processes = Process.GetProcessesByName("Discord");
        return processes.Select(process => (IDiscordProcessSnapshot)new PhysicalProcessSnapshot(process)).ToArray();
    }

    private sealed class PhysicalProcessSnapshot(Process process) : IDiscordProcessSnapshot
    {
        public int SessionId => process.SessionId;
        public bool HasExited => process.HasExited;
        public void Dispose() => process.Dispose();
    }
}

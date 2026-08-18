namespace DiscordProxyRelay.Tests;

public sealed class DiscordProcessMonitorTests
{
    [Fact]
    public void InspectProcessesReturnsUnknownForUnexpectedFailure()
    {
        var result = DiscordProcessMonitor.InspectProcesses(
            () => 1,
            () => throw new InvalidOperationException("inspection failed"));

        Assert.Equal(DiscordProcessState.Unknown, result);
    }

    [Fact]
    public void InspectProcessesDoesNotMaskCancellation()
    {
        Assert.Throws<OperationCanceledException>(() =>
            DiscordProcessMonitor.InspectProcesses(
                () => throw new OperationCanceledException(),
                () => []));
    }

    [Fact]
    public void CurrentSessionFailureIsUnknown()
    {
        var result = DiscordProcessMonitor.InspectProcesses(
            () => throw new InvalidOperationException(),
            () => []);

        Assert.Equal(DiscordProcessState.Unknown, result);
    }

    [Fact]
    public void RunningDiscordInAnotherSessionIsIgnoredAndDisposed()
    {
        var process = new FakeProcessSnapshot(sessionId: 2, hasExited: false);

        var result = DiscordProcessMonitor.InspectProcesses(() => 1, () => [process]);

        Assert.Equal(DiscordProcessState.Stopped, result);
        Assert.True(process.Disposed);
    }

    [Fact]
    public void RunningDiscordInCurrentSessionIsRunning()
    {
        var process = new FakeProcessSnapshot(sessionId: 7, hasExited: false);

        var result = DiscordProcessMonitor.InspectProcesses(() => 7, () => [process]);

        Assert.Equal(DiscordProcessState.Running, result);
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CandidateInspectionFailureIsUnknownAndDisposed(bool failSession, bool failExitStatus)
    {
        var process = new FakeProcessSnapshot(1, false, failSession, failExitStatus);

        var result = DiscordProcessMonitor.InspectProcesses(() => 1, () => [process]);

        Assert.Equal(DiscordProcessState.Unknown, result);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task UnknownObservationResetsStoppedGracePeriod()
    {
        var states = new Queue<DiscordProcessState>(
        [DiscordProcessState.Stopped, DiscordProcessState.Stopped, DiscordProcessState.Unknown, DiscordProcessState.Stopped]);
        var now = DateTimeOffset.UnixEpoch;
        var delays = 0;
        await DiscordProcessMonitor.WaitUntilStoppedAsync(
            () => states.TryDequeue(out var state) ? state : DiscordProcessState.Stopped,
            () => now,
            (duration, _) =>
            {
                delays++;
                now += duration;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(delays >= 7);
    }

    private sealed class FakeProcessSnapshot(
        int sessionId,
        bool hasExited,
        bool failSession = false,
        bool failExitStatus = false) : IDiscordProcessSnapshot
    {
        public bool Disposed { get; private set; }

        public int SessionId => failSession ? throw new InvalidOperationException() : sessionId;
        public bool HasExited => failExitStatus ? throw new InvalidOperationException() : hasExited;

        public void Dispose() => Disposed = true;
    }
}

namespace Haruspex.Tests.Cli;

/// <summary>
/// A clock whose time moves only when a test moves it, and whose timers fire then, in order: so a test of what a run shows
/// after half a second or after three takes no time, and gives the same result on a slow machine as on a fast one.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<Timer> timers = [];
    private long now;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp() => now;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(now);

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state, period) { Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : now + dueTime.Ticks };
        timers.Add(timer);
        return timer;
    }

    /// <summary>Moves time on by <paramref name="by"/>, firing each timer that falls due on the way, at its time.</summary>
    public void Advance(TimeSpan by)
    {
        var end = now + by.Ticks;
        while (timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault() is { } next)
        {
            now = next.Due;
            next.Due = next.Period <= TimeSpan.Zero || next.Period == Timeout.InfiniteTimeSpan ? long.MaxValue : now + next.Period.Ticks;
            next.Fire();
        }

        now = end;
    }

    private sealed class Timer(ManualClock clock, TimerCallback callback, object? state, TimeSpan period) : ITimer
    {
        public long Due { get; set; }

        public TimeSpan Period { get; private set; } = period;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.now + dueTime.Ticks;
            Period = period;
            return true;
        }

        public void Dispose() => clock.timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

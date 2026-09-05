using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace TimeProviderTestingDemo;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also counts the timers created
/// through it, so a test can wait until the code under test has actually
/// registered its next timer before moving the clock.
///
/// Advancing a fake clock only fires the timers that exist at that moment.
/// A chain of awaited delays creates each timer only after the previous one's
/// continuation has run, and that continuation runs on the thread pool, not
/// inside <c>Advance</c>. Without this counter a test either races the
/// continuation or sprinkles real sleeps around and hopes.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly FakeTimeProvider _fake;
    private int _timersCreated;

    public TestClock() => _fake = new FakeTimeProvider();

    public TestClock(DateTimeOffset start) => _fake = new FakeTimeProvider(start);

    /// <summary>How many timers have been created through this provider.</summary>
    public int TimersCreated => Volatile.Read(ref _timersCreated);

    /// <summary>Moves simulated time forward, firing whatever is already due.</summary>
    public void Advance(TimeSpan delta) => _fake.Advance(delta);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Count only after the inner provider has the timer on its books, so a
        // test that sees the count is guaranteed the timer is really armed.
        var timer = _fake.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timersCreated);
        return timer;
    }

    public override DateTimeOffset GetUtcNow() => _fake.GetUtcNow();

    public override long GetTimestamp() => _fake.GetTimestamp();

    public override TimeZoneInfo LocalTimeZone => _fake.LocalTimeZone;

    public override long TimestampFrequency => _fake.TimestampFrequency;

    /// <summary>
    /// Blocks until <paramref name="count"/> timers have been created, or
    /// throws after <paramref name="timeout"/> of real time. The wait is real
    /// but tiny: it is waiting for a thread-pool continuation, not for the
    /// delay itself.
    /// </summary>
    public async Task WaitForTimersAsync(int count, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        var watch = Stopwatch.StartNew();

        while (TimersCreated < count)
        {
            if (watch.Elapsed > limit)
            {
                throw new TimeoutException(
                    $"waited {watch.Elapsed.TotalMilliseconds:F0} ms for timer {count}; " +
                    $"only {TimersCreated} timers were created");
            }

            await Task.Delay(1);
        }
    }
}

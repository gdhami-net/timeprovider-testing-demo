using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace TimeProviderTestingDemo;

/// <summary>
/// The schedule is 5s + 10s + 15s. Four attempts, thirty seconds of waiting,
/// none of it real.
/// </summary>
public class RetryScheduleTests(ITestOutputHelper output)
{
    private static Func<int, CancellationToken, Task> FailsUntilAttempt(int successAt) =>
        (attempt, _) => attempt < successAt
            ? Task.FromException(new IOException($"attempt {attempt} failed"))
            : Task.CompletedTask;

    /// <summary>
    /// Walk the clock through the schedule one delay at a time, waiting for
    /// each timer to exist before moving. Thirty seconds of backoff, a few
    /// milliseconds of wall clock.
    /// </summary>
    [Fact]
    public async Task Whole_schedule_runs_in_milliseconds_of_real_time()
    {
        var clock = new TestClock();
        var policy = new RetryPolicy(clock);
        var startedAt = clock.GetUtcNow();
        var watch = Stopwatch.StartNew();

        var run = policy.ExecuteAsync(FailsUntilAttempt(4));

        for (var step = 0; step < policy.Backoff.Count; step++)
        {
            await clock.WaitForTimersAsync(step + 1);
            clock.Advance(policy.Backoff[step]);
        }

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Stop();

        Assert.Equal(4, outcome.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(30), outcome.TotalDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.GetUtcNow() - startedAt);
        Assert.Equal(3, clock.TimersCreated);

        // A generous ceiling, not a benchmark: this run is dominated by the
        // thread-pool hops between delays, and on my machine it lands in the
        // low tens of milliseconds.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.ElapsedMilliseconds} ms of real time");
        output.WriteLine($"simulated 30s of backoff in {watch.ElapsedMilliseconds} ms of real time");
    }

    /// <summary>
    /// The schedule is proven by the boundary, not by a log line: one tick
    /// short of the last delay the operation is still pending, and the tick
    /// that closes the gap is the one that completes it.
    /// </summary>
    [Fact]
    public async Task Still_pending_one_tick_before_the_last_delay_elapses()
    {
        var clock = new TestClock();
        var policy = new RetryPolicy(clock);
        var run = policy.ExecuteAsync(FailsUntilAttempt(4));

        await clock.WaitForTimersAsync(1);
        clock.Advance(TimeSpan.FromSeconds(5));

        await clock.WaitForTimersAsync(2);
        clock.Advance(TimeSpan.FromSeconds(10));

        await clock.WaitForTimersAsync(3);
        clock.Advance(TimeSpan.FromSeconds(15) - TimeSpan.FromTicks(1));

        // Nothing else can complete this task: the only timer outstanding is
        // the last delay, and it is 100 nanoseconds short of due.
        Assert.False(run.IsCompleted);

        clock.Advance(TimeSpan.FromTicks(1));

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, outcome.Attempts);
    }

    /// <summary>A run that never succeeds gives its last exception back.</summary>
    [Fact]
    public async Task Exhausted_schedule_rethrows_the_last_failure()
    {
        var clock = new TestClock();
        var policy = new RetryPolicy(clock);
        var startedAt = clock.GetUtcNow();
        var run = policy.ExecuteAsync(FailsUntilAttempt(99));

        for (var step = 0; step < policy.Backoff.Count; step++)
        {
            await clock.WaitForTimersAsync(step + 1);
            clock.Advance(policy.Backoff[step]);
        }

        var error = await Assert.ThrowsAsync<IOException>(() => run);
        Assert.Equal("attempt 4 failed", error.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.GetUtcNow() - startedAt);
    }

    /// <summary>
    /// The same schedule behind a hand-rolled IClock. The timestamps are
    /// fakeable; the waiting is not. Two 100 ms delays cost 100 ms each,
    /// every run, and the frozen clock never notices.
    /// </summary>
    [Fact]
    public async Task A_hand_rolled_clock_does_not_reach_Task_Delay()
    {
        var frozen = new FrozenClock(DateTimeOffset.UnixEpoch);
        var policy = new HandRolledRetryPolicy(frozen, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();

        var outcome = await policy.ExecuteAsync(FailsUntilAttempt(3));
        watch.Stop();

        Assert.Equal(3, outcome.Attempts);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(190), $"only {watch.ElapsedMilliseconds} ms elapsed");
        Assert.Equal(DateTimeOffset.UnixEpoch, frozen.UtcNow);
        output.WriteLine($"hand-rolled clock: 200 ms of backoff took {watch.ElapsedMilliseconds} ms of real time");
    }
}

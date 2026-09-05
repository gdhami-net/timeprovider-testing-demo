using Xunit;
using Xunit.Abstractions;

namespace TimeProviderTestingDemo;

/// <summary>
/// The afternoon-eating part. A fake clock fires the timers that exist when
/// you advance it; it does not fire the ones the code has not reached yet.
/// Each test here asserts the failure mode rather than reproducing it, so the
/// whole file passes.
/// </summary>
public class EarlyAdvanceTests(ITestOutputHelper output)
{
    /// <summary>Real time each test allows a wrongly-ordered run to finish in.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

    private static Func<int, CancellationToken, Task> FailsUntilAttempt(int successAt) =>
        (attempt, _) => attempt < successAt
            ? Task.FromException(new IOException($"attempt {attempt} failed"))
            : Task.CompletedTask;

    /// <summary>
    /// Move the clock before starting the operation and nothing fires, because
    /// no timer existed yet. The delay the policy registers afterwards is
    /// measured from the new now, so the jump bought the test nothing.
    /// </summary>
    [Fact]
    public async Task Advancing_before_the_operation_starts_fires_nothing()
    {
        var clock = new TestClock();
        var startedAt = clock.GetUtcNow();

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, clock.TimersCreated);

        var policy = new RetryPolicy(clock);
        var run = policy.ExecuteAsync(FailsUntilAttempt(4));

        await Task.Delay(Grace);

        Assert.False(run.IsCompleted);
        Assert.Equal(1, clock.TimersCreated);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.GetUtcNow() - startedAt);
        output.WriteLine($"advanced 30s before the first attempt; still pending after {Grace.TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// One jump past the whole schedule does not work either, for the same
    /// reason. The second delay's timer does not exist during the jump, and
    /// when it is created it is created relative to the new now — so it is
    /// ten seconds in the future all over again.
    /// </summary>
    [Fact]
    public async Task One_big_jump_does_not_walk_a_chain_of_delays()
    {
        var clock = new TestClock();
        var policy = new RetryPolicy(clock);
        var run = policy.ExecuteAsync(FailsUntilAttempt(4));

        await clock.WaitForTimersAsync(1);

        clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(Grace);
        Assert.False(run.IsCompleted);
        Assert.Equal(2, clock.TimersCreated);

        clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(Grace);
        Assert.False(run.IsCompleted);
        Assert.Equal(3, clock.TimersCreated);

        output.WriteLine($"ten simulated minutes, three timers, still pending at {clock.GetUtcNow():HH:mm:ss}");
    }

    /// <summary>
    /// The corrected ordering: start the operation, wait until each timer is
    /// on the clock's books, then advance. Same policy, same schedule, done.
    /// </summary>
    [Fact]
    public async Task Advancing_once_each_timer_exists_completes_the_run()
    {
        var clock = new TestClock();
        var policy = new RetryPolicy(clock);
        var startedAt = clock.GetUtcNow();
        var run = policy.ExecuteAsync(FailsUntilAttempt(4));

        await clock.WaitForTimersAsync(1);
        clock.Advance(TimeSpan.FromSeconds(5));

        await clock.WaitForTimersAsync(2);
        clock.Advance(TimeSpan.FromSeconds(10));

        await clock.WaitForTimersAsync(3);
        clock.Advance(TimeSpan.FromSeconds(15));

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, outcome.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.GetUtcNow() - startedAt);
    }

    /// <summary>
    /// A timer that already exists does get fired by a single advance. The
    /// difference is not the size of the jump, it is whether the thing you are
    /// waiting for was registered before you jumped.
    /// </summary>
    [Fact]
    public async Task A_timer_that_already_exists_fires_on_one_advance()
    {
        var clock = new TestClock();
        var waiting = Task.Delay(TimeSpan.FromSeconds(30), clock);
        Assert.Equal(1, clock.TimersCreated);

        clock.Advance(TimeSpan.FromSeconds(30));

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

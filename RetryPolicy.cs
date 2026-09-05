namespace TimeProviderTestingDemo;

/// <summary>What a completed run reports back: how many attempts it took and
/// how much delay the policy asked the clock for in total.</summary>
public sealed record RetryOutcome(int Attempts, TimeSpan TotalDelay);

/// <summary>
/// Retry with a fixed backoff schedule. Every wait goes through the injected
/// <see cref="TimeProvider"/>, so a test can drive the schedule with a fake
/// clock instead of waiting through it.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>5s, 10s, 15s — four attempts, thirty seconds of waiting.</summary>
    public static readonly TimeSpan[] DefaultBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
    ];

    private readonly TimeProvider _time;
    private readonly IReadOnlyList<TimeSpan> _backoff;

    public RetryPolicy(TimeProvider time, params TimeSpan[] backoff)
    {
        _time = time;
        _backoff = backoff.Length == 0 ? DefaultBackoff : backoff;
    }

    /// <summary>The whole schedule laid end to end.</summary>
    public TimeSpan ScheduledTotal
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var step in _backoff)
            {
                total += step;
            }

            return total;
        }
    }

    public IReadOnlyList<TimeSpan> Backoff => _backoff;

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying on failure with the backoff
    /// schedule. The attempt number is 1-based. Throws the last exception once
    /// the schedule is exhausted.
    /// </summary>
    public async Task<RetryOutcome> ExecuteAsync(
        Func<int, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var total = TimeSpan.Zero;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation(attempt, cancellationToken).ConfigureAwait(false);
                return new RetryOutcome(attempt, total);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt <= _backoff.Count)
            {
                var delay = _backoff[attempt - 1];
                total += delay;

                // The only line that matters for testability: the delay is the
                // clock's, not the thread pool's.
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

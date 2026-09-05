namespace TimeProviderTestingDemo;

/// <summary>The clock interface most codebases wrote for themselves.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>A fake for <see cref="IClock"/>: the reading never changes.</summary>
public sealed class FrozenClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>
/// The same retry policy, waiting through a hand-rolled clock abstraction.
/// The clock is injectable and the timestamps are fakeable, but the wait is
/// still <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, which knows
/// nothing about <see cref="IClock"/> and sleeps for real.
/// </summary>
public sealed class HandRolledRetryPolicy(IClock clock, params TimeSpan[] backoff)
{
    private readonly IReadOnlyList<TimeSpan> _backoff = backoff;

    public DateTimeOffset StartedAt { get; private set; }

    public async Task<RetryOutcome> ExecuteAsync(
        Func<int, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        StartedAt = clock.UtcNow;
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
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

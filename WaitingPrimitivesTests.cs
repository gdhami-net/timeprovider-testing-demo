using Xunit;

namespace TimeProviderTestingDemo;

/// <summary>
/// The waiting primitives that take a TimeProvider, and one that does not
/// unless you ask for it. Overloads confirmed against .NET 10; the docs list
/// each of them as .NET 8 and later.
/// </summary>
public class WaitingPrimitivesTests
{
    [Fact]
    public async Task WaitAsync_times_out_on_the_fake_clock()
    {
        var clock = new TestClock();
        var neverFinishes = new TaskCompletionSource().Task;

        var waiting = neverFinishes.WaitAsync(TimeSpan.FromSeconds(30), clock);
        Assert.Equal(1, clock.TimersCreated);

        clock.Advance(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<TimeoutException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// The TimeProvider goes in through the constructor. There is no
    /// CancelAfter overload that takes one, but a later CancelAfter on a
    /// source built this way resets the delay against the same provider.
    /// </summary>
    [Fact]
    public void CancellationTokenSource_takes_the_provider_in_its_constructor()
    {
        var clock = new TestClock();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(cts.IsCancellationRequested);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void CancelAfter_on_that_source_uses_the_same_provider()
    {
        var clock = new TestClock();
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(1), clock);

        cts.CancelAfter(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.True(cts.IsCancellationRequested);
    }

    /// <summary>
    /// A source built with the parameterless constructor is on the real system
    /// clock, and the fake one cannot touch it.
    /// </summary>
    [Fact]
    public void A_plain_source_ignores_the_fake_clock()
    {
        var clock = new TestClock();
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.False(cts.IsCancellationRequested);
    }

    /// <summary>
    /// PeriodicTimer registers its timer in the constructor, so it exists
    /// before the first advance. Ask for the tick, then move the clock.
    /// </summary>
    [Fact]
    public async Task PeriodicTimer_ticks_on_the_fake_clock()
    {
        var clock = new TestClock();
        var startedAt = clock.GetUtcNow();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        Assert.Equal(1, clock.TimersCreated);

        for (var tick = 0; tick < 3; tick++)
        {
            var next = timer.WaitForNextTickAsync().AsTask();
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        Assert.Equal(TimeSpan.FromSeconds(15), clock.GetUtcNow() - startedAt);
    }

    /// <summary>
    /// The fake clock starts at a fixed instant rather than at whatever the
    /// machine's clock says, which is what makes a timestamp assertion worth
    /// writing.
    /// </summary>
    [Fact]
    public void The_fake_clock_starts_at_a_fixed_instant()
    {
        var clock = new TestClock();
        Assert.Equal(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.GetUtcNow());
        Assert.Equal(TimeZoneInfo.Utc, clock.LocalTimeZone);
    }
}

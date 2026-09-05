# timeprovider-testing-demo

Companion repo for **"The test that stopped sleeping"**
([gdhami.net](https://gdhami.net) — link added when the post is live).

A retry policy with a 5s + 10s + 15s backoff schedule, driven by
`TimeProvider`. Under `FakeTimeProvider` the whole thirty seconds of waiting
happens in about 13 ms of wall clock, and the schedule is asserted at the
boundary rather than read out of a log line.

## What each test proves

| Test | Claim |
|---|---|
| `RetryScheduleTests.Whole_schedule_runs_in_milliseconds_of_real_time` | four attempts, 30s of simulated backoff, three timers, real elapsed under 2s (measured: 13–14 ms) |
| `RetryScheduleTests.Still_pending_one_tick_before_the_last_delay_elapses` | at 29.9999999s the run is still pending; the 100 ns tick that closes the gap completes it |
| `RetryScheduleTests.Exhausted_schedule_rethrows_the_last_failure` | a run that never succeeds throws attempt 4's exception after exactly 30s of simulated time |
| `RetryScheduleTests.A_hand_rolled_clock_does_not_reach_Task_Delay` | an `IClock` interface fakes the timestamps and nothing else: 200 ms of backoff still costs 200 ms (measured: 208–222 ms) |
| `EarlyAdvanceTests.Advancing_before_the_operation_starts_fires_nothing` | advancing 30s before the first attempt fires nothing, and the run is still pending 250 ms later |
| `EarlyAdvanceTests.One_big_jump_does_not_walk_a_chain_of_delays` | ten simulated minutes across two jumps, three timers created, still pending |
| `EarlyAdvanceTests.Advancing_once_each_timer_exists_completes_the_run` | the corrected ordering: start, wait for the timer, advance, repeat |
| `EarlyAdvanceTests.A_timer_that_already_exists_fires_on_one_advance` | the jump size was never the problem |
| `WaitingPrimitivesTests.*` | `Task.WaitAsync`, the `CancellationTokenSource` constructor, `CancelAfter` on such a source, `PeriodicTimer`, and a plain `CancellationTokenSource` that the fake clock cannot touch |

Both failure modes are **asserted, not reproduced**: every test in the repo
passes.

## Run it

```bash
dotnet test
```

Or `./check.sh` (bash) / `./check.ps1` (PowerShell), which run the same suite
and print what each group covers.

Built and measured on .NET SDK 10.0.201, targeting `net10.0`, with
`Microsoft.Extensions.TimeProvider.Testing` 10.9.0. The `TimeProvider`
overloads used here (`Task.Delay`, `Task.WaitAsync`, the `PeriodicTimer`
constructor, the `CancellationTokenSource` constructor) are documented as
.NET 8 and later. I have not run this on Linux or macOS; nothing in it is
platform-specific, but the millisecond numbers above are from Windows 11.

## The part that costs an afternoon

`FakeTimeProvider.Advance` fires the timers that exist at the moment you call
it. A chain of awaited delays does not have all its timers up front: each
`Task.Delay` registers its timer only after the previous one's continuation
has run, and that continuation runs on the thread pool, not inside `Advance`.

So a single jump past the whole schedule leaves the run pending, and so does
advancing before the first attempt. `TestClock` in this repo is a
`FakeTimeProvider` wrapper that counts the timers created through it, so a
test can wait for the timer to exist and then advance, instead of racing the
continuation.

MIT licensed. Argue with it.

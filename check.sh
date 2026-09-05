#!/usr/bin/env bash
# Runs the suite that backs the post's claims. Exits non-zero if any fail.
set -euo pipefail
cd "$(dirname "$0")"

echo "timeprovider-testing-demo — checking the post's claims"
echo "  RetryScheduleTests    5s+10s+15s schedule, the one-tick boundary, the hand-rolled clock"
echo "  EarlyAdvanceTests     advancing before the timer exists, and the ordering that works"
echo "  WaitingPrimitivesTests  Task.WaitAsync, CancellationTokenSource, PeriodicTimer"
echo

dotnet test --logger "console;verbosity=normal"

# Runs the suite that backs the post's claims. Exits non-zero if any fail.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host 'timeprovider-testing-demo - checking the post''s claims'
Write-Host '  RetryScheduleTests      5s+10s+15s schedule, the one-tick boundary, the hand-rolled clock'
Write-Host '  EarlyAdvanceTests       advancing before the timer exists, and the ordering that works'
Write-Host '  WaitingPrimitivesTests  Task.WaitAsync, CancellationTokenSource, PeriodicTimer'
Write-Host ''

dotnet test --logger 'console;verbosity=normal'
exit $LASTEXITCODE

<#
.SYNOPSIS
    Resilient Watchdog Runner for Pmad.AI Benchmark Suite.

.DESCRIPTION
    Runs the benchmark suite with automatic crash recovery, system sleep prevention,
    and automatic checkpoint resumption (--resume). If the underlying process crashes
    (e.g., due to unmanaged native exceptions or driver faults), the watchdog waits briefly
    for GPU memory to clear and automatically restarts execution from the latest checkpoint.

.PARAMETER MaxRetries
    Maximum number of crash recovery restarts before giving up. Default: 100.

.PARAMETER CooldownSeconds
    Seconds to wait after a crash before relaunching. Default: 5.

.EXAMPLE
    # Run the default benchmark matrix for Qwen 3.5 4B:
    .\run-benchmark-resilient.ps1

    # Run with custom arguments:
    .\run-benchmark-resilient.ps1 --model qwen3.5-4b --gpu 99 --backend vulkan --scenario all-small --opt-matrix --output docs/benchmark-baseline-qwen35-4b-report.md
#>

[CmdletBinding()]
param (
    [int]$MaxRetries = 100,
    [int]$CooldownSeconds = 5,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CustomArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Win32 Sleep Blocker
$SleepBlockerType = @"
using System;
using System.Runtime.InteropServices;

public static class BenchmarkSleepBlocker
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;
    public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    public static void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);
    }

    public static void AllowSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }
}
"@

try {
    Add-Type -TypeDefinition $SleepBlockerType -ErrorAction SilentlyContinue
} catch {
    # Type may already be loaded
}

# Resolve benchmark CLI arguments
$cliArgs = @()
if ($CustomArgs -and $CustomArgs.Length -gt 0) {
    $cliArgs = @($CustomArgs)
} else {
    # Default benchmark configuration matching the user's run
    $cliArgs = @(
        "--model", "qwen3.5-4b",
        "--gpu", "99",
        "--backend", "vulkan",
        "--config", "baseline",
        "--scenario", "all-small",
        "--opt-matrix",
        "--output", "docs/benchmark-baseline-qwen35-4b-report.md"
    )
}

# Always ensure --resume is present so progress is never lost
if ($cliArgs -notcontains "--resume") {
    $cliArgs += "--resume"
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "         PMAD.AI RESILIENT BENCHMARK RUNNER & WATCHDOG               " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "Started at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "Benchmark arguments: $($cliArgs -join ' ')" -ForegroundColor Gray
Write-Host "Max retries: $MaxRetries | Cooldown: ${CooldownSeconds}s" -ForegroundColor Gray
Write-Host ""

# Pre-build benchmark in Release configuration
Write-Host "[Watchdog] Pre-building benchmark project in Release mode..." -ForegroundColor Cyan
dotnet build benchmarks/Pmad.AI.Benchmark/Pmad.AI.Benchmark.csproj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Watchdog] Build failed with exit code $LASTEXITCODE. Aborting." -ForegroundColor Red
    exit $LASTEXITCODE
}

$benchmarkDll = (Resolve-Path "benchmarks/Pmad.AI.Benchmark/bin/Release/net10.0/Pmad.AI.Benchmark.dll").Path
Write-Host "  -> Binary ready: $benchmarkDll`n" -ForegroundColor Green

# Activate Windows sleep prevention
try {
    [BenchmarkSleepBlocker]::PreventSleep()
    Write-Host "[Watchdog] Windows system sleep prevention activated." -ForegroundColor Green
} catch {
    Write-Host "[Watchdog] Warning: Could not set sleep prevention: $_" -ForegroundColor Yellow
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$attempt = 0
$success = $false

try {
    while ($attempt -le $MaxRetries -and -not $success) {
        $attempt++

        Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkCyan
        Write-Host "[Watchdog] Run Attempt $attempt / $($MaxRetries + 1) at $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Cyan
        Write-Host "Command: dotnet `"$benchmarkDll`" $($cliArgs -join ' ')" -ForegroundColor DarkGray
        Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkCyan

        & dotnet $benchmarkDll @cliArgs
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            $success = $true
            Write-Host "`n[Watchdog] Benchmark completed successfully with exit code 0." -ForegroundColor Green
            break
        } else {
            # Format exit code in hex for typical fatal errors (e.g. 0xC0000005)
            $hexExit = '0x{0:X8}' -f [int]$exitCode
            Write-Host "`n[Watchdog] Benchmark process crashed or terminated with code $exitCode ($hexExit)." -ForegroundColor Red

            if ($attempt -le $MaxRetries) {
                Write-Host "[Watchdog] Waiting ${CooldownSeconds}s for GPU memory recovery before auto-resuming from checkpoint..." -ForegroundColor Yellow
                Start-Sleep -Seconds $CooldownSeconds
            }
        }
    }
} finally {
    try {
        [BenchmarkSleepBlocker]::AllowSleep()
        Write-Host "`n[Watchdog] Windows system sleep prevention released." -ForegroundColor Gray
    } catch {
        # Best effort restore
    }
}

$stopwatch.Stop()
$elapsed = $stopwatch.Elapsed

if ($success) {
    Write-Host "`n======================================================================" -ForegroundColor Green
    Write-Host " BENCHMARK COMPLETED SUCCESSFULLY! Total time: $($elapsed.ToString('hh\:mm\:ss'))" -ForegroundColor Green
    Write-Host " Total execution attempts: $attempt" -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Green
    try {
        [System.Console]::Beep(880, 200)
        [System.Console]::Beep(1320, 300)
    } catch { }
    exit 0
} else {
    Write-Host "`n[Watchdog] Benchmark halted after $MaxRetries crash recoveries. Partial progress preserved in checkpoint." -ForegroundColor Red
    exit 1
}

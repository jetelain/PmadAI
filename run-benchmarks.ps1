<#
.SYNOPSIS
    Automated watchdog and execution harness for the Pmad.AI Public Benchmark Suite.

.DESCRIPTION
    Runs BFCL (Berkeley Function Calling Leaderboard) and RULER (Needle In A Haystack)
    evaluations across all target models (local Ollama + in-process GGUF presets).
    Prevents Windows system sleep, manages Ollama preload/offload lifecycles, and
    automatically resumes execution if any target crashes or is interrupted.

.PARAMETER Scenario
    Scenario filter ("all", "bfcl", "ruler", "deep", or specific scenario id). Default: "all".

.PARAMETER Optimization
    Optimization configuration ("all", "baseline", "compact", "dispatch", "breaker", "transcript"). Default: "all".

.PARAMETER OllamaModels
    Array of designated Ollama model tags to benchmark.
    Default: @("Qwen3.8-27B:latest", "jetelain/Qwen3.8-27B:iq3").

.PARAMETER AllTargets
    Whether to evaluate both Ollama models and in-process presets. Default: $true.

.PARAMETER Mock
    Runs in mock simulation mode without downloading or loading models. Default: $false.

.PARAMETER Download
    Automatically download missing GGUF presets from HuggingFace. Default: $true.

.PARAMETER Resume
    Resume from existing progress saved in benchmark-progress.json. Default: $true.

.PARAMETER MaxRetries
    Maximum number of crash-recovery restarts before aborting. Default: 3.

.PARAMETER ReportPath
    Path where the generated Markdown summary report is written. Default: "docs/benchmark-public-report.md".

.PARAMETER TranscriptPath
    Path where the markdown transcript is written. Default: "docs/benchmark-public-transcript.md".

.PARAMETER JsonPath
    Path where the machine-readable JSON results are written. Default: "docs/benchmark-public-results.json".

.EXAMPLE
    .\run-benchmarks.ps1
    .\run-benchmarks.ps1 -Mock
    .\run-benchmarks.ps1 -Scenario "bfcl" -OllamaModels @("Qwen3.8-27B:latest")
#>

[CmdletBinding()]
param (
    [string]$Model = "",
    [int]$Gpu = 0,
    [string]$Backend = "",
    [switch]$OptMatrix = $false,
    [string]$Config = "",
    [string]$Scenario = "all",
    [string]$Optimization = "all",
    [string[]]$OllamaModels = @("Qwen3.8-27B:latest", "jetelain/Qwen3.8-27B:iq3"),
    [switch]$AllTargets = $false,
    [switch]$Mock = $false,
    [switch]$Download = $true,
    [switch]$Resume = $true,
    [int]$MaxRetries = 50,
    [string]$ReportPath = "docs/benchmark-public-report.md",
    [string]$TranscriptPath = "docs/benchmark-public-transcript.md",
    [string]$JsonPath = "docs/benchmark-public-results.json",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$PassthroughArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ==============================================================================
# 1. Windows Sleep Prevention (Win32 P/Invoke)
# ==============================================================================
$SleepBlockerType = @"
using System;
using System.Runtime.InteropServices;

public static class SleepBlocker
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
    # Type may already be loaded in this PowerShell session
}

# ==============================================================================
# 2. Ollama Daemon Health Check
# ==============================================================================
function Test-OllamaHealth {
    Write-Host "[Watchdog] Checking Ollama daemon connectivity on http://localhost:11434..." -ForegroundColor Cyan
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 3 -ErrorAction Stop
        Write-Host "  -> Ollama is online. ($($response.models.Count) model(s) available)" -ForegroundColor Green

        if (-not $Mock -and $OllamaModels.Length -gt 0) {
            $availableNames = $response.models | ForEach-Object { $_.name }
            foreach ($target in $OllamaModels) {
                if ($availableNames -contains $target) {
                    Write-Host "  -> Found target model: $target" -ForegroundColor DarkGreen
                } else {
                    Write-Host "  -> WARNING: Model '$target' not listed in local Ollama! (Will attempt eval or fail if absent)" -ForegroundColor Yellow
                }
            }
        }
        return $true
    } catch {
        Write-Host "  -> Ollama is NOT reachable on http://localhost:11434." -ForegroundColor Yellow
        $ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
        if ($ollamaCmd) {
            Write-Host "  -> Attempting to start Ollama background service..." -ForegroundColor Cyan
            Start-Process ollama -ArgumentList "serve" -WindowStyle Minimized
            Start-Sleep -Seconds 4
            try {
                $check = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 5 -ErrorAction Stop
                Write-Host "  -> Ollama started successfully!" -ForegroundColor Green
                return $true
            } catch {
                Write-Host "  -> Ollama start timed out. Ollama models may be skipped." -ForegroundColor Yellow
                return $false
            }
        } else {
            Write-Host "  -> 'ollama' CLI not found in PATH. Ollama targets will be unavailable." -ForegroundColor Yellow
            return $false
        }
    }
}

# ==============================================================================
# 3. Notification Helper
# ==============================================================================
function Send-BenchmarkNotification {
    param (
        [string]$Title,
        [string]$Message
    )

    Write-Host "`n[$Title] $Message" -ForegroundColor Magenta

    try {
        [System.Console]::Beep(880, 150)
        [System.Console]::Beep(1320, 250)
    } catch {
        # Audio device not available
    }
}

# ==============================================================================
# 4. Main Execution Watchdog
# ==============================================================================
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "       PMAD.AI AUTOMATED PUBLIC BENCHMARK SUITE WATCHDOG             " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "Started at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host "Scenario: $Scenario | Optimization: $Optimization | Mock: $Mock" -ForegroundColor Gray
Write-Host "AllTargets: $AllTargets | Resume: $Resume | Download: $Download" -ForegroundColor Gray
Write-Host "Designated Ollama Models: $($OllamaModels -join ', ')" -ForegroundColor Gray
Write-Host ""

if (-not $Mock) {
    Test-OllamaHealth | Out-Null
}

Write-Host "[Watchdog] Building benchmark suite in Release configuration..." -ForegroundColor Cyan
dotnet build benchmarks/Pmad.AI.Benchmark -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Watchdog] Build failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}

$benchmarkDll = (Resolve-Path "benchmarks/Pmad.AI.Benchmark/bin/Release/net10.0/Pmad.AI.Benchmark.dll").Path
Write-Host "  -> Built binary ready: $benchmarkDll" -ForegroundColor Green

# Prevent OS Sleep
[SleepBlocker]::PreventSleep()
Write-Host "[Watchdog] Windows system sleep prevention activated (ES_AWAYMODE_REQUIRED)." -ForegroundColor Green

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$attempt = 0
$completedSuccess = $false

try {
    while ($attempt -le $MaxRetries -and -not $completedSuccess) {
        $attempt++

        # Build CLI arguments
        $cliArgs = @()

        if (-not [string]::IsNullOrWhiteSpace($Model)) {
            $cliArgs += "--model"
            $cliArgs += $Model
        }

        if ($Gpu -gt 0) {
            $cliArgs += "--gpu"
            $cliArgs += $Gpu
        }

        if (-not [string]::IsNullOrWhiteSpace($Backend)) {
            $cliArgs += "--backend"
            $cliArgs += $Backend
        }

        if ($OptMatrix) {
            $cliArgs += "--opt-matrix"
        }

        if (-not [string]::IsNullOrWhiteSpace($Config)) {
            $cliArgs += "--config"
            $cliArgs += $Config
        }

        if ($Mock) {
            $cliArgs += "--mock"
        }

        if ($AllTargets) {
            $cliArgs += "--all-targets"
        }

        if ($Download) {
            $cliArgs += "--download"
        }

        if ($Resume) {
            $cliArgs += "--resume"
        }

        if (-not [string]::IsNullOrWhiteSpace($Scenario)) {
            $cliArgs += "-s"
            $cliArgs += $Scenario
        }

        if (-not [string]::IsNullOrWhiteSpace($Optimization)) {
            $cliArgs += "--opt"
            $cliArgs += $Optimization
        }

        if ($OllamaModels.Length -gt 0 -and [string]::IsNullOrWhiteSpace($Model)) {
            $cliArgs += "--ollama-models"
            $cliArgs += ($OllamaModels -join ",")
        }

        if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
            $cliArgs += "-o"
            $cliArgs += $ReportPath
        }

        if (-not [string]::IsNullOrWhiteSpace($TranscriptPath)) {
            $cliArgs += "-t"
            $cliArgs += $TranscriptPath
        }

        if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
            $cliArgs += "-j"
            $cliArgs += $JsonPath
        }

        if ($PassthroughArgs -and $PassthroughArgs.Length -gt 0) {
            $cliArgs += $PassthroughArgs
        }

        Write-Host "`n[Watchdog: Run Attempt $attempt/$($MaxRetries + 1)] Invoking benchmark binary directly..." -ForegroundColor Cyan
        Write-Host "dotnet $benchmarkDll $($cliArgs -join ' ')" -ForegroundColor DarkGray

        & dotnet $benchmarkDll @cliArgs
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            $completedSuccess = $true
            Write-Host "`n[Watchdog] Benchmark completed successfully with exit code 0." -ForegroundColor Green
            break
        } else {
            $hexExit = '0x{0:X8}' -f [int]$exitCode
            Write-Host "`n[Watchdog] Benchmark process exited with non-zero code $exitCode ($hexExit)." -ForegroundColor Red
            if ($attempt -le $MaxRetries) {
                Write-Host "[Watchdog] Cooling down 10 seconds before auto-resuming from checkpoint..." -ForegroundColor Yellow
                Start-Sleep -Seconds 10
            }
        }
    }
}
finally {
    # Always restore sleep state
    [SleepBlocker]::AllowSleep()
    Write-Host "[Watchdog] Windows system sleep prevention released." -ForegroundColor Gray
}

$sw.Stop()
$elapsed = $sw.Elapsed

if ($completedSuccess) {
    Send-BenchmarkNotification -Title "Pmad.AI Benchmarks Complete" -Message "All scenarios completed in $($elapsed.ToString('hh\:mm\:ss')). Results saved to $ReportPath."
    Write-Host "`n======================================================================" -ForegroundColor Green
    Write-Host " BENCHMARK SUITE COMPLETE! Total time: $($elapsed.ToString('hh\:mm\:ss'))" -ForegroundColor Green
    Write-Host " Report:     $ReportPath" -ForegroundColor White
    Write-Host " JSON Data:  $JsonPath" -ForegroundColor White
    Write-Host " Transcript: $TranscriptPath" -ForegroundColor White
    Write-Host "======================================================================" -ForegroundColor Green
} else {
    Send-BenchmarkNotification -Title "Pmad.AI Benchmarks Interrupted" -Message "Benchmark stopped after $MaxRetries retries. Partial progress saved in benchmark-progress.json."
    Write-Host "`n[Watchdog] Benchmark run ended with failures after $MaxRetries retries." -ForegroundColor Red
    exit 1
}

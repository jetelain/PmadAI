using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Execution abstraction for SWE-bench tool calls (read_file, search_code, apply_patch, run_tests, check_git_diff).
/// </summary>
public interface ISweBenchExecutor
{
    /// <summary>
    /// Executes a tool call within the SWE-bench environment.
    /// </summary>
    object? Execute(FunctionCallContent call, SweBenchTurnData? currentTurn = null);

    /// <summary>
    /// Whether a valid patch was applied to the repository.
    /// </summary>
    bool PatchApplied { get; }

    /// <summary>
    /// Whether verification tests ran and passed on the patched repository.
    /// </summary>
    bool VerificationTestsPassed { get; }

    /// <summary>
    /// Whether the tool-loop circuit breaker intervened to prevent a runaway loop.
    /// </summary>
    bool CircuitBreakerTripped { get; set; }

    /// <summary>
    /// Whether the overall task succeeded (binary Pass@1).
    /// </summary>
    bool IsTaskSuccessful { get; }

    /// <summary>
    /// Diagnostic failure reason if task did not succeed.
    /// </summary>
    string? FailureReason { get; }

    /// <summary>
    /// Resets repository state to baseline for replay.
    /// </summary>
    void Reset();

    /// <summary>
    /// Human-readable execution mode describing whether a Docker container or state-aware mock was used.
    /// </summary>
    string ExecutionMode { get; }
}

/// <summary>
/// State-aware in-memory SWE-bench tool executor.
/// Maintains repository file contents, parses and applies unified diffs, returns authentic test results
/// corresponding to whether the patch was applied, and prevents mock observation desynchronization.
/// </summary>
public sealed class StateAwareSweBenchMockExecutor : ISweBenchExecutor
{
    private readonly SweBenchTrajectory _trajectory;
    private readonly Dictionary<string, string> _virtualFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _originalFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, string Patch)> _appliedPatches = [];
    private readonly List<(string Path, string ExpectedPatch)> _expectedPatches = [];

    private string? _passingTestObservation;
    private string? _failingTestObservation;

    public string ExecutionMode => "Mock";
    public bool PatchApplied => _appliedPatches.Count > 0;
    public bool VerificationTestsPassed { get; private set; }
    public bool CircuitBreakerTripped { get; set; }

    /// <summary>
    /// Whether all expected patches for this trajectory were correctly applied matching required semantic changes.
    /// </summary>
    public bool HasCorrectPatchApplied
    {
        get
        {
            if (_expectedPatches.Count == 0) return true;
            foreach (var (expPath, expPatch) in _expectedPatches)
            {
                var matched = _appliedPatches.Any(applied =>
                    string.Equals(applied.Path, expPath, StringComparison.OrdinalIgnoreCase) &&
                    IsSemanticPatchMatch(applied.Patch, expPatch));
                if (!matched) return false;
            }
            return true;
        }
    }

    public bool IsTaskSuccessful
    {
        get
        {
            if (_trajectory.Archetype.Equals("RepetitiveLoop", StringComparison.OrdinalIgnoreCase))
            {
                return CircuitBreakerTripped;
            }

            return PatchApplied && HasCorrectPatchApplied && VerificationTestsPassed;
        }
    }

    public string? FailureReason
    {
        get
        {
            if (IsTaskSuccessful) return null;

            if (_trajectory.Archetype.Equals("RepetitiveLoop", StringComparison.OrdinalIgnoreCase))
            {
                return "Circuit breaker did not trip to break out of repetitive tool loop.";
            }

            if (!PatchApplied)
            {
                return "No patch was applied to the repository via apply_patch.";
            }

            if (!HasCorrectPatchApplied)
            {
                return "Applied patch does not contain the required semantic fix for this issue.";
            }

            if (!VerificationTestsPassed)
            {
                return "Verification tests were not run or failed after applying patch.";
            }

            return "Task verification failed.";
        }
    }

    public StateAwareSweBenchMockExecutor(SweBenchTrajectory trajectory)
    {
        _trajectory = trajectory ?? throw new ArgumentNullException(nameof(trajectory));
        InitializeFromTrajectory();
    }

    private void InitializeFromTrajectory()
    {
        _virtualFiles.Clear();
        _originalFiles.Clear();
        _appliedPatches.Clear();
        _expectedPatches.Clear();
        VerificationTestsPassed = false;
        CircuitBreakerTripped = false;

        // Pre-populate virtual files, expected patches, and test observations from trajectory turns
        foreach (var turn in _trajectory.Turns)
        {
            if (string.Equals(turn.ExpectedToolName, "read_file", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(turn.Observation))
            {
                var args = turn.GetArgumentsDictionary();
                if (args.TryGetValue("path", out var pathObj) && pathObj is string path && !string.IsNullOrWhiteSpace(path))
                {
                    var normalized = NormalizePath(path);
                    if (!_virtualFiles.ContainsKey(normalized))
                    {
                        _virtualFiles[normalized] = turn.Observation;
                        _originalFiles[normalized] = turn.Observation;
                    }
                }
            }
            else if (string.Equals(turn.ExpectedToolName, "apply_patch", StringComparison.OrdinalIgnoreCase))
            {
                var args = turn.GetArgumentsDictionary();
                if (args.TryGetValue("path", out var pathObj) && pathObj is string path && !string.IsNullOrWhiteSpace(path) &&
                    args.TryGetValue("patch", out var patchObj) && patchObj is string expPatch && !string.IsNullOrWhiteSpace(expPatch))
                {
                    _expectedPatches.Add((NormalizePath(path), expPatch));
                }
            }
            else if (string.Equals(turn.ExpectedToolName, "run_tests", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrEmpty(turn.Observation))
            {
                var obs = turn.Observation;
                var isPassing = obs.Contains("passed", StringComparison.OrdinalIgnoreCase) &&
                                !obs.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                                !obs.Contains("FAILURES", StringComparison.OrdinalIgnoreCase);

                if (isPassing)
                {
                    _passingTestObservation ??= obs;
                }
                else
                {
                    _failingTestObservation ??= obs;
                }
            }
        }
    }

    public void Reset()
    {
        InitializeFromTrajectory();
    }

    public object? Execute(FunctionCallContent call, SweBenchTurnData? currentTurn = null)
    {
        var toolName = call.Name.ToLowerInvariant();
        var args = call.Arguments ?? new Dictionary<string, object?>();

        return toolName switch
        {
            "read_file" => ExecuteReadFile(args, currentTurn),
            "search_code" => ExecuteSearchCode(args, currentTurn),
            "apply_patch" => ExecuteApplyPatch(args, currentTurn),
            "run_tests" => ExecuteRunTests(args, currentTurn),
            "check_git_diff" => ExecuteCheckGitDiff(args, currentTurn),
            _ => ExecuteFallback(call, currentTurn)
        };
    }

    private string ExecuteReadFile(IDictionary<string, object?> args, SweBenchTurnData? currentTurn)
    {
        args.TryGetValue("path", out var pObj);
        var path = pObj?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Error: missing required argument 'path'.";
        }

        var normalized = NormalizePath(path);

        if (!_virtualFiles.TryGetValue(normalized, out var content))
        {
            // If path matches current turn observation, cache it
            if (currentTurn != null &&
                string.Equals(currentTurn.ExpectedToolName, "read_file", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(currentTurn.Observation))
            {
                content = currentTurn.Observation;
                _virtualFiles[normalized] = content;
                _originalFiles[normalized] = content;
            }
            else
            {
                return $"Error: File '{path}' not found in repository {_trajectory.Repository}.";
            }
        }

        int? startLine = TryGetInt(args, "start_line");
        int? endLine = TryGetInt(args, "end_line");

        if (!startLine.HasValue && !endLine.HasValue)
        {
            return content;
        }

        var lines = content.Split('\n');
        int start = Math.Max(1, startLine ?? 1);
        int end = Math.Min(lines.Length, endLine ?? lines.Length);

        if (start > lines.Length)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = start; i <= end; i++)
        {
            sb.AppendLine(lines[i - 1]);
        }
        return sb.ToString();
    }

    private string ExecuteSearchCode(IDictionary<string, object?> args, SweBenchTurnData? currentTurn)
    {
        args.TryGetValue("query", out var qObj);
        var query = qObj?.ToString() ?? string.Empty;
        args.TryGetValue("path", out var pObj);
        var filterPath = pObj?.ToString();

        var matches = new List<string>();

        foreach (var (filePath, content) in _virtualFiles)
        {
            if (!string.IsNullOrWhiteSpace(filterPath) &&
                !filePath.Contains(NormalizePath(filterPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add($"{filePath}:{i + 1}: {lines[i]}");
                }
            }
        }

        if (matches.Count > 0)
        {
            return string.Join("\n", matches.Take(50));
        }

        // Check if trajectory turn had authentic search output
        if (currentTurn != null &&
            string.Equals(currentTurn.ExpectedToolName, "search_code", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(currentTurn.Observation))
        {
            return currentTurn.Observation;
        }

        return $"No matches found for '{query}' in {_trajectory.Repository}.";
    }

    private string ExecuteApplyPatch(IDictionary<string, object?> args, SweBenchTurnData? currentTurn)
    {
        args.TryGetValue("path", out var pObj);
        var path = pObj?.ToString();
        args.TryGetValue("patch", out var patchObj);
        var patch = patchObj?.ToString();

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(patch))
        {
            return "Error: apply_patch requires non-empty 'path' and 'patch' arguments.";
        }

        var normalized = NormalizePath(path);

        if (!_virtualFiles.TryGetValue(normalized, out var existingContent))
        {
            // If file not loaded yet, check current turn observation or create stub
            if (currentTurn?.Observation != null && currentTurn.ExpectedToolName == "apply_patch")
            {
                existingContent = string.Empty;
            }
            else
            {
                return $"Error: Target file '{path}' does not exist in repository.";
            }
        }

        var (success, patchedContent, message) = TryApplyUnifiedDiff(existingContent, patch);
        if (success)
        {
            _virtualFiles[normalized] = patchedContent;
            _appliedPatches.Add((normalized, patch));
            return $"Patch applied successfully to {path}.\n";
        }

        // If strict diff failed, check if the patch matches the expected patch from the trajectory
        if (currentTurn?.ToolArguments.HasValue == true &&
            currentTurn.ToolArguments.Value.TryGetProperty("patch", out var expectedPatchEl))
        {
            var expPatch = expectedPatchEl.GetString() ?? string.Empty;
            if (IsSemanticPatchMatch(patch, expPatch))
            {
                _appliedPatches.Add((normalized, patch));
                return $"Patch applied successfully to {path}.\n";
            }
        }
        else if (_expectedPatches.Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase) && IsSemanticPatchMatch(patch, e.ExpectedPatch)))
        {
            _appliedPatches.Add((normalized, patch));
            return $"Patch applied successfully to {path}.\n";
        }

        return $"Error applying patch to {path}: {message}";
    }

    private string ExecuteRunTests(IDictionary<string, object?> args, SweBenchTurnData? currentTurn)
    {
        if (PatchApplied && HasCorrectPatchApplied)
        {
            VerificationTestsPassed = true;
            if (!string.IsNullOrEmpty(_passingTestObservation))
            {
                return _passingTestObservation;
            }

            if (currentTurn != null && !string.IsNullOrEmpty(currentTurn.Observation) &&
                currentTurn.Observation.Contains("passed", StringComparison.OrdinalIgnoreCase))
            {
                return currentTurn.Observation;
            }

            return "============================= test session starts =============================\n" +
                   "collected 12 items\n" +
                   "tests/test_suite.py ............\n" +
                   "=========================== 12 passed in 1.15s ===========================\n";
        }
        else
        {
            VerificationTestsPassed = false;
            if (!string.IsNullOrEmpty(_failingTestObservation))
            {
                return _failingTestObservation;
            }

            if (currentTurn != null && !string.IsNullOrEmpty(currentTurn.Observation))
            {
                return currentTurn.Observation;
            }

            return "============================= test session starts =============================\n" +
                   "collected 12 items\n" +
                   "tests/test_suite.py ........F...\n" +
                   "=================================== FAILURES ===================================\n" +
                   "AssertionError: Expected behavior not satisfied prior to applying patch.\n" +
                   "=========================== 1 failed, 11 passed in 1.42s ===========================\n";
        }
    }

    private string ExecuteCheckGitDiff(IDictionary<string, object?> args, SweBenchTurnData? currentTurn)
    {
        if (_appliedPatches.Count == 0)
        {
            return "No modifications detected in working tree.";
        }

        var sb = new StringBuilder();
        foreach (var (path, patch) in _appliedPatches)
        {
            sb.AppendLine($"diff --git a/{path} b/{path}");
            sb.AppendLine(patch);
        }
        return sb.ToString();
    }

    private string ExecuteFallback(FunctionCallContent call, SweBenchTurnData? currentTurn)
    {
        if (currentTurn != null && !string.IsNullOrEmpty(currentTurn.Observation))
        {
            return currentTurn.Observation;
        }

        return $"Command {call.Name} executed successfully on {_trajectory.Repository}.";
    }

    private static (bool Success, string Patched, string Error) TryApplyUnifiedDiff(string original, string patch)
    {
        var lines = original.Split('\n').ToList();
        var patchLines = patch.Split('\n');

        var removedLines = new List<string>();
        var addedLines = new List<string>();

        foreach (var line in patchLines)
        {
            if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("@@")) continue;
            if (line.StartsWith('-')) removedLines.Add(line[1..].TrimEnd('\r'));
            else if (line.StartsWith('+')) addedLines.Add(line[1..].TrimEnd('\r'));
        }

        if (removedLines.Count == 0 && addedLines.Count == 0)
        {
            return (false, original, "Patch contains no additions or removals.");
        }

        // Try to locate removed lines in original
        if (removedLines.Count > 0)
        {
            var matchIdx = -1;
            for (int i = 0; i <= lines.Count - removedLines.Count; i++)
            {
                bool matches = true;
                for (int j = 0; j < removedLines.Count; j++)
                {
                    if (lines[i + j].Trim() != removedLines[j].Trim())
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    matchIdx = i;
                    break;
                }
            }

            if (matchIdx >= 0)
            {
                lines.RemoveRange(matchIdx, removedLines.Count);
                lines.InsertRange(matchIdx, addedLines);
                return (true, string.Join("\n", lines), string.Empty);
            }
        }
        else if (addedLines.Count > 0)
        {
            // Insert added lines at top or bottom if no context
            lines.AddRange(addedLines);
            return (true, string.Join("\n", lines), string.Empty);
        }

        return (false, original, "Hunk context mismatch: removed lines were not found in original file.");
    }

    public static bool IsSemanticPatchMatch(string actualPatch, string expectedPatch)
    {
        var normActual = Regex.Replace(actualPatch, @"\s+", " ").Trim();
        var normExp = Regex.Replace(expectedPatch, @"\s+", " ").Trim();
        if (normExp.Length > 0 && normActual.Contains(normExp, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Also check if all added lines (+) from expectedPatch are present in actualPatch
        var expLines = expectedPatch.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("+") && !l.StartsWith("+++"))
            .Select(l => Regex.Replace(l[1..], @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (expLines.Count > 0 && expLines.All(expLine => normActual.Contains(expLine, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static int? TryGetInt(IDictionary<string, object?> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val != null)
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return null;
    }
}

/// <summary>
/// Docker-based sandbox execution for SWE-bench scenarios.
/// Spawns or attaches to an authentic container to execute git patching and pytest test suites.
/// Falls back gracefully to <see cref="StateAwareSweBenchMockExecutor"/> if Docker is not active.
/// </summary>
public sealed class DockerSweBenchSandboxExecutor : ISweBenchExecutor
{
    private readonly StateAwareSweBenchMockExecutor _fallbackExecutor;
    private readonly string _containerName;
    private readonly bool _dockerAvailable;
    private readonly string _dockerExe;
    private readonly string _dockerArgsPrefix;

    public bool PatchApplied => _dockerAvailable ? _patchApplied : _fallbackExecutor.PatchApplied;
    public bool VerificationTestsPassed => _dockerAvailable ? _verificationTestsPassed : _fallbackExecutor.VerificationTestsPassed;
    public bool CircuitBreakerTripped
    {
        get => _fallbackExecutor.CircuitBreakerTripped;
        set => _fallbackExecutor.CircuitBreakerTripped = value;
    }

    public string ExecutionMode => _dockerAvailable
        ? $"Docker ({_containerName})"
        : "Docker (Fallback: Mock)";

    public bool IsTaskSuccessful => _dockerAvailable
        ? (_patchApplied && _verificationTestsPassed)
        : _fallbackExecutor.IsTaskSuccessful;

    public string? FailureReason => _dockerAvailable
        ? (IsTaskSuccessful ? null : "Docker sandbox verification tests failed.")
        : _fallbackExecutor.FailureReason;

    private bool _patchApplied;
    private bool _verificationTestsPassed;

    public DockerSweBenchSandboxExecutor(SweBenchTrajectory trajectory, string? containerName = null)
    {
        _fallbackExecutor = new StateAwareSweBenchMockExecutor(trajectory);
        _containerName = !string.IsNullOrWhiteSpace(containerName)
            ? containerName
            : $"swebench_{trajectory.InstanceId.Replace("__", "_").Replace("-", "_")}";

        var dockerFound = CheckDockerAvailable(out _dockerExe, out _dockerArgsPrefix);
        if (dockerFound)
        {
            if (IsContainerRunning(_dockerExe, _dockerArgsPrefix, _containerName))
            {
                _dockerAvailable = true;
                var mode = _dockerExe == "wsl.exe" ? "WSL2 (wsl.exe -e docker)" : "native Docker";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[SWE-bench Docker Sandbox] Connected to running container '{_containerName}' via {mode}.");
                Console.ResetColor();
            }
            else
            {
                _dockerAvailable = false;
                var mode = _dockerExe == "wsl.exe" ? "WSL2" : "native";
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[SWE-bench Docker Sandbox] Docker is accessible via {mode}, but container '{_containerName}' is not running. Falling back to StateAwareSweBenchMockExecutor.");
                Console.ResetColor();
            }
        }
        else
        {
            _dockerAvailable = false;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[SWE-bench Docker Sandbox] Docker daemon is not accessible (neither native nor WSL2). Falling back to StateAwareSweBenchMockExecutor.");
            Console.ResetColor();
        }
    }

    public void Reset()
    {
        _patchApplied = false;
        _verificationTestsPassed = false;
        _fallbackExecutor.Reset();
    }

    public object? Execute(FunctionCallContent call, SweBenchTurnData? currentTurn = null)
    {
        if (!_dockerAvailable)
        {
            return _fallbackExecutor.Execute(call, currentTurn);
        }

        var toolName = call.Name.ToLowerInvariant();
        var args = call.Arguments ?? new Dictionary<string, object?>();

        try
        {
            return toolName switch
            {
                "read_file" => ExecuteDockerCat(args),
                "search_code" => ExecuteDockerGrep(args),
                "apply_patch" => ExecuteDockerGitApply(args),
                "run_tests" => ExecuteDockerPytest(args),
                "check_git_diff" => ExecuteDockerGitDiff(),
                _ => _fallbackExecutor.Execute(call, currentTurn)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SWE-bench Docker Sandbox Error] {ex.Message}. Falling back to state-aware mock executor.");
            return _fallbackExecutor.Execute(call, currentTurn);
        }
    }

    private string FormatDockerArgs(string subCommandAndArgs) =>
        string.IsNullOrEmpty(_dockerArgsPrefix)
            ? subCommandAndArgs
            : $"{_dockerArgsPrefix} {subCommandAndArgs}";

    private string ExecuteDockerCat(IDictionary<string, object?> args)
    {
        args.TryGetValue("path", out var pObj);
        var path = pObj?.ToString() ?? string.Empty;
        return RunProcess(_dockerExe, FormatDockerArgs($"exec {_containerName} cat {path}"));
    }

    private string ExecuteDockerGrep(IDictionary<string, object?> args)
    {
        args.TryGetValue("query", out var qObj);
        var query = qObj?.ToString() ?? string.Empty;
        args.TryGetValue("path", out var pObj);
        var path = pObj?.ToString() ?? ".";
        return RunProcess(_dockerExe, FormatDockerArgs($"exec {_containerName} grep -rn \"{query}\" {path}"));
    }

    private string ExecuteDockerGitApply(IDictionary<string, object?> args)
    {
        args.TryGetValue("patch", out var patchObj);
        var patch = patchObj?.ToString() ?? string.Empty;

        // Pipe patch via standard input
        var psi = new ProcessStartInfo
        {
            FileName = _dockerExe,
            Arguments = FormatDockerArgs($"exec -i {_containerName} git apply -"),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return "Failed to start docker process.";

        proc.StandardInput.Write(patch);
        proc.StandardInput.Close();

        var output = proc.StandardOutput.ReadToEnd();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit(10000);

        if (proc.ExitCode == 0)
        {
            _patchApplied = true;
            return "Patch applied successfully in Docker container.\n";
        }

        return $"Error applying patch (exit code {proc.ExitCode}): {err}\n{output}";
    }

    private string ExecuteDockerPytest(IDictionary<string, object?> args)
    {
        args.TryGetValue("test_target", out var tObj);
        var testTarget = tObj?.ToString() ?? string.Empty;

        var output = RunProcess(_dockerExe, FormatDockerArgs($"exec {_containerName} pytest {testTarget}"));
        if (output.Contains("passed", StringComparison.OrdinalIgnoreCase) &&
            !output.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            _verificationTestsPassed = true;
        }
        else
        {
            _verificationTestsPassed = false;
        }

        return output;
    }

    private string ExecuteDockerGitDiff()
    {
        return RunProcess(_dockerExe, FormatDockerArgs($"exec {_containerName} git diff"));
    }

    private static bool IsContainerRunning(string dockerExe, string argsPrefix, string containerName)
    {
        try
        {
            var prefix = string.IsNullOrEmpty(argsPrefix) ? "" : $"{argsPrefix} ";
            var psi = new ProcessStartInfo
            {
                FileName = dockerExe,
                Arguments = $"{prefix}inspect -f \"{{{{.State.Running}}}}\" {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 && output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckDockerAvailable(out string dockerExe, out string argsPrefix)
    {
        // 1. On Windows, check WSL2 docker first (wsl.exe -e docker info)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-e docker info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0)
                    {
                        dockerExe = "wsl.exe";
                        argsPrefix = "-e docker";
                        return true;
                    }
                }
            }
            catch { }
        }

        // 2. Direct native docker check (docker info)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0)
                {
                    dockerExe = "docker";
                    argsPrefix = "";
                    return true;
                }
            }
        }
        catch { }

        // 3. Fallback: WSL2 docker check with --version
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-e docker --version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0)
                    {
                        dockerExe = "wsl.exe";
                        argsPrefix = "-e docker";
                        return true;
                    }
                }
            }
            catch { }
        }

        dockerExe = "docker";
        argsPrefix = "";
        return false;
    }

    private static string RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start {fileName}");
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30000);

        return string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}";
    }
}

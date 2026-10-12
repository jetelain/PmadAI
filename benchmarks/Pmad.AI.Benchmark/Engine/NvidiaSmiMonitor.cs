using System.Diagnostics;
using System.Globalization;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Queries NVIDIA GPU VRAM metrics via nvidia-smi.
/// </summary>
public static class NvidiaSmiMonitor
{
    private static readonly Lazy<string?> _smiPath = new(ResolveNvidiaSmiPath);

    /// <summary>
    /// Whether nvidia-smi is present and usable on the system.
    /// </summary>
    public static bool IsAvailable => _smiPath.Value != null;

    private static string? ResolveNvidiaSmiPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var direct = Path.Combine(system32, "nvidia-smi.exe");
            if (File.Exists(direct)) return direct;

            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var nvsmi = Path.Combine(progFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (File.Exists(nvsmi)) return nvsmi;
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "-h",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(1000);
            if (proc?.ExitCode == 0) return "nvidia-smi";
        }
        catch
        {
            // Not available
        }

        return null;
    }

    /// <summary>
    /// Gets currently used VRAM in MiB from the primary NVIDIA GPU.
    /// Returns null if nvidia-smi is unavailable or query fails.
    /// </summary>
    public static double? GetUsedVramMb()
    {
        var exe = _smiPath.Value;
        if (exe == null) return null;

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--query-gpu=memory.used --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1500);

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (line != null && double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mib))
                {
                    return mib;
                }
            }
        }
        catch
        {
            // Graceful fallback
        }

        return null;
    }
}

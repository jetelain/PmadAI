using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Detects host system hardware specifications (CPU, GPU, VRAM, and OS) for benchmark reporting.
/// </summary>
public sealed record SystemHardwareInfo(
    string CpuName,
    int LogicalCores,
    string? GpuName,
    string? GpuDriver,
    double? GpuTotalVramMb,
    string OsDescription)
{
    private static readonly Lazy<SystemHardwareInfo> _current = new(DetectInternal);

    /// <summary>
    /// Gets the detected hardware specifications of the current machine.
    /// </summary>
    public static SystemHardwareInfo Current => _current.Value;

    public string CpuSummary => $"{CpuName} ({LogicalCores} logical cores)";

    public string GpuSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GpuName))
            {
                return "Not detected / CPU only";
            }

            var parts = new List<string> { GpuName };
            if (GpuTotalVramMb.HasValue && GpuTotalVramMb.Value > 0)
            {
                parts.Add(BenchmarkRunner.FormatMemory(GpuTotalVramMb.Value) + " VRAM");
            }
            if (!string.IsNullOrWhiteSpace(GpuDriver))
            {
                parts.Add($"Driver: {GpuDriver}");
            }

            return string.Join(", ", parts);
        }
    }

    private static SystemHardwareInfo DetectInternal()
    {
        var cpu = DetectCpuName();
        var cores = Environment.ProcessorCount;
        var os = RuntimeInformation.OSDescription.Trim();
        var (gpuName, gpuDriver, gpuVram) = DetectGpuInfo();

        return new SystemHardwareInfo(cpu, cores, gpuName, gpuDriver, gpuVram, os);
    }

    private static string DetectCpuName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var regVal = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString",
                    null);

                if (regVal is string str && !string.IsNullOrWhiteSpace(str))
                {
                    return str.Trim();
                }
            }
            catch
            {
                // Fall through
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                if (File.Exists("/proc/cpuinfo"))
                {
                    foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = line.IndexOf(':');
                            if (idx >= 0 && idx + 1 < line.Length)
                            {
                                return line[(idx + 1)..].Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n machdep.cpu.brand_string",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                var output = proc?.StandardOutput.ReadToEnd();
                proc?.WaitForExit(1000);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Trim();
                }
            }
            catch
            {
                // Fall through
            }
        }

        var env = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return $"{RuntimeInformation.ProcessArchitecture} Processor";
    }

    private static (string? GpuName, string? GpuDriver, double? GpuTotalVramMb) DetectGpuInfo()
    {
        // 1. Try nvidia-smi if available
        if (NvidiaSmiMonitor.IsAvailable)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=name,driver_version,memory.total --format=csv,noheader,nounits",
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
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var parts = line.Split(',');
                        var name = parts.Length > 0 ? parts[0].Trim() : null;
                        var driver = parts.Length > 1 ? parts[1].Trim() : null;
                        double? vram = null;
                        if (parts.Length > 2 && double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mib))
                        {
                            vram = mib;
                        }

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return (name, driver, vram);
                        }
                    }
                }
            }
            catch
            {
                // Fall through
            }
        }

        return (null, null, null);
    }
}

namespace Pmad.AI.DiffGuard.Cli;

public static class ExitCodes
{
    /// <summary>Scan completed with no findings at or above MinimumRiskSeverity.</summary>
    public const int Success = 0;

    /// <summary>Invalid arguments, configuration error, git execution failure, or unhandled error.</summary>
    public const int Error = 1;

    /// <summary>One or more findings met or exceeded MinimumRiskSeverity.</summary>
    public const int RisksDetected = 2;

    /// <summary>Operation canceled by user (Ctrl+C / SIGINT).</summary>
    public const int Canceled = 130;
}


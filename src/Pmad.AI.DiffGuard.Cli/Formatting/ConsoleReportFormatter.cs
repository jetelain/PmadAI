using System.Text;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Formatting;

public sealed class ConsoleReportFormatter : IReportFormatter
{
    public static readonly ConsoleReportFormatter Instance = new();

    public Task FormatAsync(DiffGuardReportModel report, TextWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var sb = new StringBuilder();

        // 1. Header
        sb.AppendLine("🛡️  DiffGuard Security Analysis");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Repository   : {report.RepositoryPath}");
        sb.AppendLine($"Target       : {report.ComparisonDescription}");
        if (!string.IsNullOrEmpty(report.BaseCommitSha) || !string.IsNullOrEmpty(report.HeadCommitSha))
        {
            sb.AppendLine($"Commit Range : {report.BaseCommitSha?[..Math.Min(8, report.BaseCommitSha.Length)]} ... {report.HeadCommitSha?[..Math.Min(8, report.HeadCommitSha.Length)]}");
        }

        if (report.LoadedConfigPaths.Count > 0)
        {
            sb.AppendLine($"Config Files : {string.Join(", ", report.LoadedConfigPaths)}");
        }

        string scannersDesc = (report.DeterministicScannerEnabled, report.LlmJudgeEnabled) switch
        {
            (true, true) => $"Deterministic + LLM Judge ({report.LlmProvider} / {report.LlmModelId}, {report.LlmChunksEvaluated} chunk{(report.LlmChunksEvaluated == 1 ? "" : "s")})",
            (true, false) => "Deterministic only",
            (false, true) => $"LLM Judge only ({report.LlmProvider} / {report.LlmModelId}, {report.LlmChunksEvaluated} chunk{(report.LlmChunksEvaluated == 1 ? "" : "s")})",
            _ => "None"
        };
        sb.AppendLine($"Scanners     : {scannersDesc}");
        sb.AppendLine($"Elapsed Time : {report.ElapsedMilliseconds} ms");
        sb.AppendLine(new string('-', 60));

        // 2. Metrics Summary
        var res = report.ScanResult;
        string filesImpactStr = res.FileImpactPercent.HasValue
            ? $" ({res.FileImpactPercent.Value:0.##}% of {report.BaselineStats?.TotalTrackedFiles ?? 0} tracked files)"
            : string.Empty;
        string linesImpactStr = res.CodebaseImpactPercent.HasValue
            ? $", {res.CodebaseImpactPercent.Value:0.##}% of {report.BaselineStats?.TotalTrackedLines ?? 0} tracked lines"
            : string.Empty;

        sb.AppendLine($"Changed Files: {res.ChangedFilesCount}{filesImpactStr}");
        sb.AppendLine($"Changed Lines: +{res.AddedLinesCount} / -{res.DeletedLinesCount} ({res.TotalChangedLines} total{linesImpactStr})");

        if (report.BaselineStats != null)
        {
            sb.AppendLine($"Tracked Files: {report.BaselineStats.TotalTrackedFiles} (Baseline)");
            sb.AppendLine($"Tracked Lines: {report.BaselineStats.TotalTrackedLines} (Baseline)");
        }

        int criticalCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Critical);
        int highCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.High);
        int mediumCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Medium);
        int lowCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Low);

        sb.AppendLine($"Findings     : {res.Findings.Count} total (Critical: {criticalCount}, High: {highCount}, Medium: {mediumCount}, Low: {lowCount})");
        sb.AppendLine($"Min Severity : {report.MinimumRiskSeverity}");
        sb.AppendLine(new string('-', 60));

        // 3. Findings Details
        if (res.Findings.Count > 0)
        {
            int index = 1;
            foreach (var finding in res.Findings)
            {
                string badge = FormatSeverityBadge(finding.Severity);
                sb.AppendLine($"{index++}. {badge} [{finding.RuleId}] {finding.Title} ({finding.Category})");

                if (!string.IsNullOrEmpty(finding.FilePath))
                {
                    string loc = finding.LineNumber.HasValue
                        ? $"{finding.FilePath}:{finding.LineNumber.Value}:{(finding.Column ?? 1)}"
                        : finding.FilePath;
                    sb.AppendLine($"   Location: {loc}");
                }

                sb.AppendLine($"   Details : {finding.Description}");

                if (!string.IsNullOrWhiteSpace(finding.Snippet))
                {
                    sb.AppendLine("   Snippet :");
                    foreach (var line in finding.Snippet.Trim().Split('\n'))
                    {
                        sb.AppendLine($"     | {line.TrimEnd('\r')}");
                    }
                }
                sb.AppendLine();
            }
        }

        // 4. Verdict Footer
        sb.AppendLine(new string('=', 60));
        if (res.HasRisks)
        {
            sb.AppendLine($"VERDICT: FAIL - Security risks detected at or above {report.MinimumRiskSeverity} severity.");
        }
        else
        {
            sb.AppendLine("VERDICT: PASS - No security risks detected at or above configured threshold.");
        }
        sb.AppendLine(new string('=', 60));

        writer.Write(sb.ToString());
        return Task.CompletedTask;
    }

    private static string FormatSeverityBadge(DiffRiskSeverity severity) => severity switch
    {
        DiffRiskSeverity.Critical => "[CRITICAL]",
        DiffRiskSeverity.High => "[HIGH]",
        DiffRiskSeverity.Medium => "[MEDIUM]",
        DiffRiskSeverity.Low => "[LOW]",
        _ => $"[{severity}]"
    };
}


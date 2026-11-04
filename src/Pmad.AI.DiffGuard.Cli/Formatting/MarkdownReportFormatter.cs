using System.Text;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Formatting;

public sealed class MarkdownReportFormatter : IReportFormatter
{
    public static readonly MarkdownReportFormatter Instance = new();

    public async Task FormatAsync(DiffGuardReportModel report, TextWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var sb = new StringBuilder();
        var res = report.ScanResult;

        string titleEmoji = res.HasRisks ? "❌" : "✅";
        string titleStatus = res.HasRisks ? "RISKS DETECTED" : "CLEAN";

        sb.AppendLine($"# 🛡️ DiffGuard Security Report — {titleEmoji} {titleStatus}");
        sb.AppendLine();

        // 1. Metric Table
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Repository** | `{report.RepositoryPath}` |");
        sb.AppendLine($"| **Target** | `{report.ComparisonDescription}` |");

        if (!string.IsNullOrEmpty(report.BaseCommitSha) || !string.IsNullOrEmpty(report.HeadCommitSha))
        {
            string baseShort = report.BaseCommitSha?[..Math.Min(8, report.BaseCommitSha.Length)] ?? "-";
            string headShort = report.HeadCommitSha?[..Math.Min(8, report.HeadCommitSha.Length)] ?? "-";
            sb.AppendLine($"| **Commit Range** | `{baseShort}` ... `{headShort}` |");
        }

        string filesImpactStr = res.FileImpactPercent.HasValue
            ? $" ({res.FileImpactPercent.Value:0.##}% of {report.BaselineStats?.TotalTrackedFiles ?? 0:N0} tracked files)"
            : string.Empty;
        string linesImpactStr = res.CodebaseImpactPercent.HasValue
            ? $", {res.CodebaseImpactPercent.Value:0.##}% of {report.BaselineStats?.TotalTrackedLines ?? 0:N0} tracked lines"
            : string.Empty;

        sb.AppendLine($"| **Changed Files** | {res.ChangedFilesCount:N0}{filesImpactStr} |");
        sb.AppendLine($"| **Changed Lines** | +{res.AddedLinesCount:N0} / -{res.DeletedLinesCount:N0} ({res.TotalChangedLines:N0} total{linesImpactStr}) |");

        if (report.BaselineStats != null)
        {
            sb.AppendLine($"| **Tracked Files (Baseline)** | {report.BaselineStats.TotalTrackedFiles:N0} |");
            sb.AppendLine($"| **Tracked Lines (Baseline)** | {report.BaselineStats.TotalTrackedLines:N0} |");
        }

        string scannersDesc = (report.DeterministicScannerEnabled, report.LlmJudgeEnabled) switch
        {
            (true, true) => $"Deterministic + LLM Judge (`{report.LlmProvider}` / `{report.LlmModelId}`, {report.LlmChunksEvaluated} chunk{(report.LlmChunksEvaluated == 1 ? "" : "s")})",
            (true, false) => "Deterministic only",
            (false, true) => $"LLM Judge only (`{report.LlmProvider}` / `{report.LlmModelId}`, {report.LlmChunksEvaluated} chunk{(report.LlmChunksEvaluated == 1 ? "" : "s")})",
            _ => "None"
        };
        sb.AppendLine($"| **Scanners Active** | {scannersDesc} |");
        sb.AppendLine($"| **Minimum Risk Severity** | `{report.MinimumRiskSeverity}` |");

        var highest = res.Findings.Count > 0 ? res.Findings.Max(f => f.Severity).ToString() : "None";
        sb.AppendLine($"| **Highest Severity Found** | `{highest}` |");
        sb.AppendLine($"| **Execution Time** | {report.ElapsedMilliseconds} ms |");
        sb.AppendLine();

        // 2. Summary Table
        int criticalCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Critical);
        int highCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.High);
        int mediumCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Medium);
        int lowCount = res.Findings.Count(f => f.Severity == DiffRiskSeverity.Low);

        sb.AppendLine("## 📊 Findings Summary");
        sb.AppendLine();
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("| :--- | ---: |");
        sb.AppendLine($"| 🔴 **Critical** | {criticalCount} |");
        sb.AppendLine($"| 🟠 **High** | {highCount} |");
        sb.AppendLine($"| 🟡 **Medium** | {mediumCount} |");
        sb.AppendLine($"| 🔵 **Low** | {lowCount} |");
        sb.AppendLine();

        // 3. Detailed Findings
        if (res.Findings.Count > 0)
        {
            sb.AppendLine("## 🚨 Detailed Findings");
            sb.AppendLine();

            int index = 1;
            foreach (var finding in res.Findings)
            {
                string emoji = SeverityEmoji(finding.Severity);
                sb.AppendLine($"### {index++}. {emoji} [{finding.Severity}] `{finding.RuleId}` — {EscapeMarkdown(finding.Title)}");
                sb.AppendLine($"- **Category:** `{finding.Category}`");

                if (!string.IsNullOrEmpty(finding.FilePath))
                {
                    string loc = finding.LineNumber.HasValue
                        ? $"`{finding.FilePath}:{finding.LineNumber.Value}:{(finding.Column ?? 1)}`"
                        : $"`{finding.FilePath}`";
                    sb.AppendLine($"- **Location:** {loc}");
                }

                sb.AppendLine($"- **Description:** {EscapeMarkdown(finding.Description)}");

                if (!string.IsNullOrWhiteSpace(finding.Snippet))
                {
                    sb.AppendLine("- **Snippet:**");
                    sb.AppendLine("  ```text");
                    foreach (var line in finding.Snippet.Trim().Split('\n'))
                    {
                        sb.AppendLine($"  {line.TrimEnd('\r')}");
                    }
                    sb.AppendLine("  ```");
                }
                sb.AppendLine();
            }
        }

        await writer.WriteAsync(sb.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static string SeverityEmoji(DiffRiskSeverity severity) => severity switch
    {
        DiffRiskSeverity.Critical => "🔴",
        DiffRiskSeverity.High => "🟠",
        DiffRiskSeverity.Medium => "🟡",
        DiffRiskSeverity.Low => "🔵",
        _ => "⚪"
    };

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}


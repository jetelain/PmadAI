using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Formatting.Sarif;

public sealed class SarifReportFormatter : IReportFormatter
{
    public static readonly SarifReportFormatter Instance = new();

    public Task FormatAsync(DiffGuardReportModel report, TextWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var log = BuildSarifLog(report);
        string json = JsonSerializer.Serialize(log, SarifJsonContext.Default.SarifLog);
        writer.WriteLine(json);
        return Task.CompletedTask;
    }

    public static SarifLog BuildSarifLog(DiffGuardReportModel report)
    {
        var driverVersion = typeof(SarifReportFormatter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.1.0-preview";

        var run = new SarifRun
        {
            Tool = new SarifTool
            {
                Driver = new SarifDriver
                {
                    Name = "Pmad.AI.DiffGuard",
                    Version = driverVersion,
                    InformationUri = "https://github.com/jetelain/PmadAI",
                    Rules = [.. SarifRules.AllRules]
                }
            },
            Results = []
        };

        // Fallback file path for repository-wide blast radius findings
        string fallbackPath = "README.md";
        var firstFile = report.ScanResult.Findings.FirstOrDefault(f => !string.IsNullOrEmpty(f.FilePath));
        if (firstFile != null && !string.IsNullOrEmpty(firstFile.FilePath))
        {
            fallbackPath = firstFile.FilePath;
        }
        else if (!string.IsNullOrEmpty(report.FirstChangedFilePath))
        {
            fallbackPath = report.FirstChangedFilePath;
        }

        foreach (var finding in report.ScanResult.Findings)
        {
            string rawPath = !string.IsNullOrEmpty(finding.FilePath) ? finding.FilePath : fallbackPath;
            string normalizedPath = ReportPathHelper.NormalizeRelativePath(rawPath);
            int startLine = finding.LineNumber ?? 1;
            int? column = finding.Column;

            string level = MapSeverityToSarifLevel(finding.Severity);

            string correlationPayload = $"{finding.RuleId}:{normalizedPath}:{startLine}:{finding.Snippet ?? finding.Title}";
            string correlationGuid = DeterministicGuid.GenerateCorrelationGuid(correlationPayload).ToString("D");

            string hashSource = finding.Snippet ?? finding.Description;
            string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashSource)))[..16];

            var result = new SarifResult
            {
                RuleId = finding.RuleId,
                Level = level,
                CorrelationGuid = correlationGuid,
                PartialFingerprints = new Dictionary<string, string>
                {
                    ["ruleId/v1"] = finding.RuleId,
                    ["locationKey/v1"] = $"{normalizedPath}:{startLine}:{column ?? 1}",
                    ["contentHash/v1"] = contentHash
                },
                Message = new SarifMessage
                {
                    Text = finding.Description,
                    Markdown = FormatMessageMarkdown(finding)
                },
                Locations =
                [
                    new SarifLocation
                    {
                        PhysicalLocation = new SarifPhysicalLocation
                        {
                            ArtifactLocation = new SarifArtifactLocation
                            {
                                Uri = normalizedPath,
                                UriBaseId = "%SRCROOT%"
                            },
                            Region = new SarifRegion
                            {
                                StartLine = startLine,
                                EndLine = startLine,
                                StartColumn = column,
                                EndColumn = column
                            }
                        }
                    }
                ]
            };

            run.Results.Add(result);
        }

        return new SarifLog
        {
            Runs = [run]
        };
    }

    private static string MapSeverityToSarifLevel(DiffRiskSeverity severity) => severity switch
    {
        DiffRiskSeverity.Critical => "error",
        DiffRiskSeverity.High => "error",
        DiffRiskSeverity.Medium => "warning",
        DiffRiskSeverity.Low => "note",
        _ => "warning"
    };

    private static string FormatMessageMarkdown(DiffRiskFinding finding)
    {
        var sb = new StringBuilder();
        sb.Append($"**[{finding.RuleId}] {finding.Title}** ({finding.Category})\n\n{finding.Description}");
        if (!string.IsNullOrWhiteSpace(finding.Snippet))
        {
            sb.Append($"\n\n```text\n{finding.Snippet.Trim()}\n```");
        }
        return sb.ToString();
    }
}


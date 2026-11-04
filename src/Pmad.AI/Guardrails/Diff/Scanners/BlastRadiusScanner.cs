namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Evaluates git diff blast radius against absolute thresholds (max changed files, max changed lines)
/// and relative codebase impact percentages when baseline statistics are provided.
/// </summary>
public sealed class BlastRadiusScanner
{
    private readonly DiffRiskScannerOptions _options;

    public BlastRadiusScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(
        IReadOnlyList<FileDiff> files,
        RepositoryBaselineStats? baseline = null)
    {
        if (!_options.EnableBlastRadiusRules || files.Count == 0)
        {
            return [];
        }

        var findings = new List<DiffRiskFinding>();

        int changedFilesCount = files.Count;
        int addedLinesCount = files.Sum(f => f.AddedLines.Count);
        int deletedLinesCount = files.Sum(f => f.DeletedLines.Count);
        int totalChangedLines = addedLinesCount + deletedLinesCount;

        // 1. Absolute Changed Files Check (SCOPE-FILES-001)
        if (changedFilesCount > _options.MaxChangedFiles)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "SCOPE-FILES-001",
                Title: "Excessive Changed File Count",
                Category: DiffRiskCategory.ExcessiveBlastRadius,
                Severity: DiffRiskSeverity.Low,
                FilePath: null,
                LineNumber: null,
                Column: null,
                Description: $"Diff modifies {changedFilesCount} files, exceeding the configured limit of {_options.MaxChangedFiles} files."));
        }

        // 2. Absolute Changed Lines Check (SCOPE-LINES-001)
        if (totalChangedLines > _options.MaxChangedLines)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "SCOPE-LINES-001",
                Title: "Excessive Changed Line Count",
                Category: DiffRiskCategory.ExcessiveBlastRadius,
                Severity: DiffRiskSeverity.Low,
                FilePath: null,
                LineNumber: null,
                Column: null,
                Description: $"Diff modifies {totalChangedLines} total lines ({addedLinesCount} additions, {deletedLinesCount} deletions), exceeding the configured limit of {_options.MaxChangedLines} lines."));
        }

        // 3. Relative Codebase Impact Checks (when baseline stats provided)
        if (baseline != null)
        {
            if (baseline.TotalTrackedLines > 0)
            {
                double codebasePct = totalChangedLines * 100.0 / baseline.TotalTrackedLines;
                if (codebasePct > _options.MaxCodebaseImpactPercent)
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "SCOPE-PCT-001",
                        Title: "Excessive Codebase Impact Percentage",
                        Category: DiffRiskCategory.ExcessiveBlastRadius,
                        Severity: DiffRiskSeverity.Low,
                        FilePath: null,
                        LineNumber: null,
                        Column: null,
                        Description: $"Diff impacts {codebasePct:F1}% of total codebase lines ({totalChangedLines} of {baseline.TotalTrackedLines}), exceeding the threshold of {_options.MaxCodebaseImpactPercent:F1}%."));
                }
            }

            if (baseline.TotalTrackedFiles > 0)
            {
                double filePct = changedFilesCount * 100.0 / baseline.TotalTrackedFiles;
                if (filePct > _options.MaxFileImpactPercent)
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "SCOPE-FILEPCT-001",
                        Title: "Excessive File Impact Percentage",
                        Category: DiffRiskCategory.ExcessiveBlastRadius,
                        Severity: DiffRiskSeverity.Low,
                        FilePath: null,
                        LineNumber: null,
                        Column: null,
                        Description: $"Diff modifies {filePct:F1}% of tracked repository files ({changedFilesCount} of {baseline.TotalTrackedFiles}), exceeding the threshold of {_options.MaxFileImpactPercent:F1}%."));
                }
            }
        }

        return findings;
    }
}

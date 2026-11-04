namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Top-level deterministic risk scanner orchestrating network, backdoor, payload, entropy, obfuscation,
/// binary validation, supply chain dependency, and blast radius checks across git diffs.
/// </summary>
public sealed class DeterministicDiffRiskScanner
{
    private readonly DiffRiskScannerOptions _options;
    private readonly NetworkAndBackdoorScanner _networkScanner;
    private readonly EntropyAndSecretScanner _entropyScanner;
    private readonly EncodedPayloadScanner _payloadScanner;
    private readonly ObfuscationScanner _obfuscationScanner;
    private readonly BinaryAndImageScanner _binaryScanner;
    private readonly DependencySupplyChainScanner _dependencyScanner;
    private readonly BlastRadiusScanner _blastRadiusScanner;

    public DeterministicDiffRiskScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
        _networkScanner = new NetworkAndBackdoorScanner(_options);
        _entropyScanner = new EntropyAndSecretScanner(_options);
        _payloadScanner = new EncodedPayloadScanner(_options);
        _obfuscationScanner = new ObfuscationScanner(_options);
        _binaryScanner = new BinaryAndImageScanner(_options);
        _dependencyScanner = new DependencySupplyChainScanner(_options);
        _blastRadiusScanner = new BlastRadiusScanner(_options);
    }

    /// <summary>
    /// Parses unified git diff text and executes the full deterministic scanner suite.
    /// </summary>
    public ValueTask<DiffRiskScanResult> ScanAsync(
        string diffText,
        RepositoryBaselineStats? baseline = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diffText))
        {
            return ScanAsync(Array.Empty<FileDiff>(), baseline, cancellationToken);
        }

        var files = DiffParser.Parse(diffText, _options.ExcludedFileGlobs);
        return ScanAsync(files, baseline, cancellationToken);
    }

    /// <summary>
    /// Executes the full deterministic scanner suite across pre-parsed file diffs.
    /// </summary>
    public async ValueTask<DiffRiskScanResult> ScanAsync(
        IReadOnlyList<FileDiff> files,
        RepositoryBaselineStats? baseline = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.ExcludedFileGlobs.Count > 0 && files.Count > 0)
        {
            files = files.Where(f => !_options.ExcludedFileGlobs.Any(g =>
                (!string.IsNullOrEmpty(f.FilePath) && GlobMatcher.IsMatch(f.FilePath, g)) ||
                (!string.IsNullOrEmpty(f.OldFilePath) && GlobMatcher.IsMatch(f.OldFilePath, g)))).ToList();
        }

        var findings = new List<DiffRiskFinding>();

        if (files.Count > 0)
        {
            // 1. Network & Backdoor Scanner
            findings.AddRange(_networkScanner.Scan(files));

            // 2. Entropy & Secrets Scanner
            findings.AddRange(_entropyScanner.Scan(files));

            // 3. Encoded Payload Scanner
            findings.AddRange(_payloadScanner.Scan(files));

            // 4. Unicode Obfuscation & Line Length Scanner
            findings.AddRange(_obfuscationScanner.Scan(files));

            // 5. Binary & Safe Image Scanner
            var binaryFindings = await _binaryScanner.ScanAsync(files, cancellationToken).ConfigureAwait(false);
            findings.AddRange(binaryFindings);

            // 6. Dependency Supply Chain Scanner
            findings.AddRange(_dependencyScanner.Scan(files));

            // 7. Blast Radius Scanner
            findings.AddRange(_blastRadiusScanner.Scan(files, baseline));
        }

        int changedFilesCount = files.Count;
        int addedLinesCount = files.Sum(f => f.AddedLines.Count);
        int deletedLinesCount = files.Sum(f => f.DeletedLines.Count);
        int totalChanged = addedLinesCount + deletedLinesCount;

        double? codebasePct = baseline is { TotalTrackedLines: > 0 }
            ? totalChanged * 100.0 / baseline.TotalTrackedLines
            : null;

        double? filePct = baseline is { TotalTrackedFiles: > 0 }
            ? changedFilesCount * 100.0 / baseline.TotalTrackedFiles
            : null;

        return new DiffRiskScanResult(
            Findings: findings,
            ChangedFilesCount: changedFilesCount,
            AddedLinesCount: addedLinesCount,
            DeletedLinesCount: deletedLinesCount,
            CodebaseImpactPercent: codebasePct,
            FileImpactPercent: filePct,
            MinimumRiskSeverity: _options.MinimumRiskSeverity);
    }
}


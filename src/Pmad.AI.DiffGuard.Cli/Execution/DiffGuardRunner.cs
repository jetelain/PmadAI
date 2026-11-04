using System.Diagnostics;
using Pmad.AI.DiffGuard.Cli.Cli;
using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.DiffGuard.Cli.Formatting.Sarif;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.DiffGuard.Cli.Llm;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Execution;

public sealed class DiffGuardRunner
{
    private readonly IGitDiffProvider _gitProvider;
    private readonly ILlmJudgeClientFactory _llmFactory;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public DiffGuardRunner(
        IGitDiffProvider? gitProvider = null,
        ILlmJudgeClientFactory? llmFactory = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        _gitProvider = gitProvider ?? GitDiffProvider.Instance;
        _llmFactory = llmFactory ?? LlmJudgeClientFactory.Instance;
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
    }

    public async Task<int> RunAsync(DiffGuardCliOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            // 1. Fetch git diff and baseline metadata
            GitDiffMetadata gitMeta;
            try
            {
                gitMeta = await _gitProvider.GetDiffAsync(
                    options.RepoPath,
                    options.Target,
                    options.SkipBaseline,
                    options.ExcludedFileGlobs,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _stderr.WriteLineAsync($"[ERROR] Failed to obtain Git diff: {ex.Message}").ConfigureAwait(false);
                return ExitCodes.Error;
            }

            // 2. Parse diff
            var files = DiffParser.Parse(gitMeta.UnifiedDiffText, options.ExcludedFileGlobs);

            int changedFilesCount = files.Count;
            int addedLinesCount = files.Sum(f => f.AddedLines.Count);
            int deletedLinesCount = files.Sum(f => f.DeletedLines.Count);
            double? codebaseImpact = null;
            double? fileImpact = null;
            if (gitMeta.BaselineStats != null)
            {
                if (gitMeta.BaselineStats.TotalTrackedLines > 0)
                {
                    codebaseImpact = Math.Round((double)(addedLinesCount + deletedLinesCount) / gitMeta.BaselineStats.TotalTrackedLines * 100.0, 2);
                }
                if (gitMeta.BaselineStats.TotalTrackedFiles > 0)
                {
                    fileImpact = Math.Round((double)changedFilesCount / gitMeta.BaselineStats.TotalTrackedFiles * 100.0, 2);
                }
            }

            // 3. Setup scanner options
            var scannerOptions = new DiffRiskScannerOptions
            {
                EnableNetworkEndpointRules = options.EnableDeterministic && options.EnableNetworkEndpointRules,
                EnableBackdoorSignatureRules = options.EnableDeterministic && options.EnableBackdoorSignatureRules,
                EnableSecretAndEntropyRules = options.EnableDeterministic && options.EnableSecretAndEntropyRules,
                EnableEncodedPayloadRules = options.EnableDeterministic && options.EnableEncodedPayloadRules,
                EnableUnicodeObfuscationRules = options.EnableDeterministic && options.EnableUnicodeObfuscationRules,
                EnableBinaryFileRules = options.EnableDeterministic && options.EnableBinaryFileRules,
                EnableDependencyRules = options.EnableDeterministic && options.EnableDependencyRules,
                EnableBlastRadiusRules = options.EnableDeterministic && options.EnableBlastRadiusRules,
                EnablePromptInjectionRules = options.EnablePromptInjection,
                FlagPrivateNetworkIps = options.FlagPrivateNetworkIps,
                AllowUnverifiedImageExtensionsWhenStreamUnavailable = options.AllowUnverifiedImageExtensionsWhenStreamUnavailable,
                MinimumRiskSeverity = options.MinimumRiskSeverity,
                MaxChangedFiles = options.MaxChangedFiles,
                MaxChangedLines = options.MaxChangedLines,
                MaxCodebaseImpactPercent = options.MaxCodebaseImpactPercent,
                MaxFileImpactPercent = options.MaxFileImpactPercent,
                ShannonEntropyThreshold = options.ShannonEntropyThreshold,
                EntropyMinStringLength = options.EntropyMinStringLength,
                MinSuspiciousBase64Length = options.MinSuspiciousBase64Length,
                MinSuspiciousHexLength = options.MinSuspiciousHexLength,
                MaxLineLength = options.MaxLineLength,
                EnableTestTolerance = options.EnableTestTolerance,
                MaxLineLengthExcludedExtensions = new HashSet<string>(options.MaxLineLengthExcludedExtensions, StringComparer.OrdinalIgnoreCase),
                WellKnownSafeTestPackages = new HashSet<string>(options.WellKnownSafeTestPackages, StringComparer.OrdinalIgnoreCase),
                RepositoryRootPath = gitMeta.RepositoryRootPath,
                TargetGitRef = gitMeta.HeadCommitSha ?? gitMeta.HeadRef,
                BlobStreamProvider = _gitProvider.CreateBlobStreamProvider(options.RepoPath, options.Target, gitMeta.HeadCommitSha),
                WellKnownSafeDomains = new HashSet<string>(options.WellKnownSafeDomains, StringComparer.OrdinalIgnoreCase),
                AllowedIpAddresses = new HashSet<string>(options.AllowedIpAddresses, StringComparer.OrdinalIgnoreCase),
                WellKnownSafePackages = new HashSet<string>(options.WellKnownSafePackages, StringComparer.OrdinalIgnoreCase),
                AllowedImageExtensions = new HashSet<string>(options.AllowedImageExtensions, StringComparer.OrdinalIgnoreCase),
                ExcludedFileGlobs = [.. options.ExcludedFileGlobs]
            };

            var allFindings = new List<DiffRiskFinding>();
            int llmChunksEvaluated = 0;

            // 4. Run Deterministic Scanner
            if (options.EnableDeterministic || options.EnablePromptInjection)
            {
                var deterministicScanner = new DeterministicDiffRiskScanner(scannerOptions);
                var detResult = await deterministicScanner.ScanAsync(files, gitMeta.BaselineStats, cancellationToken).ConfigureAwait(false);
                allFindings.AddRange(detResult.Findings);
            }

            // 5. Run LLM Judge
            if (options.EnableLlm && options.LlmProvider != LlmProviderKind.None)
            {
                (Microsoft.Extensions.AI.IChatClient? chatClient, IDisposable? disposable) clientPair;
                try
                {
                    clientPair = _llmFactory.CreateClient(options);
                }
                catch (Exception ex)
                {
                    await _stderr.WriteLineAsync($"[ERROR] Failed to initialize LLM judge client: {ex.Message}").ConfigureAwait(false);
                    return ExitCodes.Error;
                }

                var (chatClient, disposable) = clientPair;
                if (chatClient != null)
                {
                    try
                    {
                        var judgeOptions = new LlmDiffSecurityJudgeOptions
                        {
                            ModelId = options.LlmModel,
                            MaxChunkBytes = options.MaxChunkBytes,
                            FilterDeletedLines = options.FilterDeletedLines,
                            MaxConsecutiveDeletedLines = options.MaxConsecutiveDeletedLines,
                            FailClosedOnError = options.FailClosedOnError,
                            IncludePromptInjectionCheck = options.EnablePromptInjection
                        };

                        var judge = new LlmDiffSecurityJudge(chatClient, judgeOptions);
                        var llmResult = await judge.ScanDiffAsync(files, cancellationToken).ConfigureAwait(false);
                        allFindings.AddRange(llmResult.Findings);
                        llmChunksEvaluated = DiffSplitter.SplitIntoChunks(files, options.MaxChunkBytes, options.FilterDeletedLines, options.MaxConsecutiveDeletedLines).Count;
                    }
                    finally
                    {
                        disposable?.Dispose();
                    }
                }
            }

            stopwatch.Stop();

            // 6. Sort findings deterministically
            var sortedFindings = allFindings
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.LineNumber ?? int.MaxValue)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ToList();

            var combinedResult = new DiffRiskScanResult(
                sortedFindings,
                changedFilesCount,
                addedLinesCount,
                deletedLinesCount,
                codebaseImpact,
                fileImpact,
                options.MinimumRiskSeverity);

            var report = new DiffGuardReportModel
            {
                RepositoryPath = options.RepoPath,
                ComparisonDescription = gitMeta.TargetDescription,
                BaseRef = gitMeta.BaseRef,
                HeadRef = gitMeta.HeadRef,
                BaseCommitSha = gitMeta.BaseCommitSha,
                HeadCommitSha = gitMeta.HeadCommitSha,
                LoadedConfigPaths = options.LoadedConfigPaths,
                DeterministicScannerEnabled = options.EnableDeterministic,
                LlmJudgeEnabled = options.EnableLlm && options.LlmProvider != LlmProviderKind.None,
                LlmProvider = options.EnableLlm ? options.LlmProvider.ToString().ToLowerInvariant() : null,
                LlmModelId = options.EnableLlm ? options.LlmModel : null,
                LlmChunksEvaluated = llmChunksEvaluated,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ScanResult = combinedResult,
                BaselineStats = gitMeta.BaselineStats,
                MinimumRiskSeverity = options.MinimumRiskSeverity,
                FirstChangedFilePath = files.FirstOrDefault(f => !string.IsNullOrEmpty(f.FilePath))?.FilePath
            };

            // 7. Format reports
            // Primary format
            if (!string.IsNullOrEmpty(options.OutputPath))
            {
                string outDir = Path.GetDirectoryName(options.OutputPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                await using var fileWriter = new StreamWriter(options.OutputPath, append: false);
                var formatter = GetFormatter(options.PrimaryFormat);
                await formatter.FormatAsync(report, fileWriter, cancellationToken).ConfigureAwait(false);
            }
            else if (!(options.Quiet && options.PrimaryFormat == OutputFormat.Console))
            {
                var formatter = GetFormatter(options.PrimaryFormat);
                await formatter.FormatAsync(report, _stdout, cancellationToken).ConfigureAwait(false);
            }

            // Secondary reports: --markdown and --sarif
            if (!string.IsNullOrEmpty(options.MarkdownPath) &&
                !(options.PrimaryFormat == OutputFormat.Markdown && string.Equals(options.OutputPath, options.MarkdownPath, StringComparison.OrdinalIgnoreCase)))
            {
                string outDir = Path.GetDirectoryName(options.MarkdownPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                await using var mdWriter = new StreamWriter(options.MarkdownPath, append: false);
                await MarkdownReportFormatter.Instance.FormatAsync(report, mdWriter, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(options.SarifPath) &&
                !(options.PrimaryFormat == OutputFormat.Sarif && string.Equals(options.OutputPath, options.SarifPath, StringComparison.OrdinalIgnoreCase)))
            {
                string outDir = Path.GetDirectoryName(options.SarifPath) ?? string.Empty;
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                await using var sarifWriter = new StreamWriter(options.SarifPath, append: false);
                await SarifReportFormatter.Instance.FormatAsync(report, sarifWriter, cancellationToken).ConfigureAwait(false);
            }

            return combinedResult.HasRisks ? ExitCodes.RisksDetected : ExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            await _stderr.WriteLineAsync("[CANCELED] DiffGuard scan was canceled.").ConfigureAwait(false);
            return ExitCodes.Canceled;
        }
    }

    private static IReportFormatter GetFormatter(OutputFormat format) => format switch
    {
        OutputFormat.Markdown => MarkdownReportFormatter.Instance,
        OutputFormat.Sarif => SarifReportFormatter.Instance,
        _ => ConsoleReportFormatter.Instance
    };
}

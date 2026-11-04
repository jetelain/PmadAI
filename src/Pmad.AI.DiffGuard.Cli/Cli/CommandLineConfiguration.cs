using System.CommandLine;
using Pmad.AI.DiffGuard.Cli.Configuration;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.DiffGuard.Cli.Llm;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Cli;

public static class CommandLineConfiguration
{
    public static RootCommand CreateRootCommand(
        Func<DiffGuardRunner>? runnerFactory = null,
        IDiffGuardConfigLoader? configLoader = null,
        TextWriter? errorWriter = null)
    {
        // 1. Positional Arguments
        var baseArgument = new Argument<string?>("base")
        {
            Description = "Base branch, tag, or commit-ish (e.g. 'main', 'origin/main').",
            Arity = ArgumentArity.ZeroOrOne
        };

        var headArgument = new Argument<string?>("head")
        {
            Description = "Target branch, tag, or commit-ish to evaluate (e.g. 'feature/x', 'HEAD').",
            Arity = ArgumentArity.ZeroOrOne
        };

        // 2. Options
        var baseOption = new Option<string?>("--base")
        {
            Description = "Base branch, tag, or commit-ish."
        };

        var headOption = new Option<string?>("--head")
        {
            Description = "Target branch, tag, or commit-ish."
        };

        var repoOption = new Option<string>("--repo", "-r")
        {
            Description = "Path to the target Git repository root or subdirectory.",
            DefaultValueFactory = _ => "."
        };

        var configOption = new Option<string?>("--config", "-c")
        {
            Description = "Explicit path to a repository/custom configuration JSON file."
        };

        var twoDotOption = new Option<bool>("--two-dot")
        {
            Description = "Use direct tip diff (base..head) instead of merge-base diff (base...head)."
        };

        var stagedOption = new Option<bool>("--staged", "--cached")
        {
            Description = "Scan staged changes in the Git index (git diff --cached)."
        };

        var workingTreeOption = new Option<bool>("--working-tree")
        {
            Description = "Scan uncommitted working-tree changes against HEAD."
        };

        var commitOption = new Option<string?>("--commit")
        {
            Description = "Scan the diff introduced by a single commit (git show <sha>)."
        };

        var diffFileOption = new Option<string?>("--diff-file")
        {
            Description = "Scan a pre-generated unified .diff or .patch file from disk."
        };

        var noBaselineOption = new Option<bool>("--no-baseline")
        {
            Description = "Skip computing repository baseline file/line counts."
        };

        // Output & Reporting
        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Primary output format: console, markdown (md), or sarif.",
            DefaultValueFactory = _ => "console"
        };

        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "Write the primary format report to file instead of stdout."
        };

        var markdownOption = new Option<string?>("--markdown", "--md")
        {
            Description = "Write a Markdown report to the specified file."
        };

        var sarifOption = new Option<string?>("--sarif")
        {
            Description = "Write a SARIF 2.1.0 report to the specified file."
        };

        var minSeverityOption = new Option<string?>("--min-severity")
        {
            Description = "Minimum severity (Low, Medium, High, Critical) that triggers exit code 2."
        };

        var quietOption = new Option<bool>("--quiet", "-q")
        {
            Description = "Suppress progress/informational console messages."
        };

        // LLM Judge Connectivity
        var llmProviderOption = new Option<string?>("--llm-provider")
        {
            Description = "LLM provider: 'none', 'ollama', or 'openai'."
        };

        var llmModelOption = new Option<string?>("--llm-model", "-m")
        {
            Description = "Model identifier for the LLM security judge."
        };

        var ollamaModelOption = new Option<string?>("--ollama-model")
        {
            Description = "Shorthand for --llm-provider ollama --llm-model <id>."
        };

        var openaiModelOption = new Option<string?>("--openai-model")
        {
            Description = "Shorthand for --llm-provider openai --llm-model <id>."
        };

        var llmEndpointOption = new Option<string?>("--llm-endpoint")
        {
            Description = "Custom endpoint URL for LLM judge."
        };

        var llmApiKeyOption = new Option<string?>("--llm-api-key")
        {
            Description = "API key for OpenAI-compatible endpoints."
        };

        var noLlmOption = new Option<bool>("--no-llm")
        {
            Description = "Explicitly disable the LLM security judge."
        };

        var filterDeletedLinesOption = new Option<bool>("--filter-deleted-lines")
        {
            Description = "Compress contiguous blocks of deleted lines in LLM judge chunks."
        };

        var maxChunkBytesOption = new Option<int?>("--max-chunk-bytes")
        {
            Description = "Maximum UTF-8 bytes per diff chunk sent to LLM judge."
        };

        var failOpenOnLlmErrorOption = new Option<bool>("--fail-open-on-llm-error")
        {
            Description = "Do not emit LLM-JUDGE-ERROR if the LLM judge request fails."
        };

        // Scanner Rules & Toggles
        var noDeterministicOption = new Option<bool>("--no-deterministic")
        {
            Description = "Disable deterministic scanners and run only the LLM judge."
        };

        var enablePromptInjectionOption = new Option<bool>("--enable-prompt-injection")
        {
            Description = "Enable prompt-injection detection rules (PI001-PI013)."
        };

        var noPromptInjectionOption = new Option<bool>("--no-prompt-injection")
        {
            Description = "Disable prompt-injection detection rules."
        };

        var flagPrivateIpsOption = new Option<bool>("--flag-private-ips")
        {
            Description = "Flag private network IP literals (NET-IP-001)."
        };

        var allowDomainOption = new Option<string[]>("--allow-domain")
        {
            Description = "Additional trusted domain(s) appended to well-known safe domains.",
            AllowMultipleArgumentsPerToken = true
        };

        var allowIpOption = new Option<string[]>("--allow-ip")
        {
            Description = "Additional trusted IP address(es) appended to allowed IPs.",
            AllowMultipleArgumentsPerToken = true
        };

        var allowPackageOption = new Option<string[]>("--allow-package")
        {
            Description = "Additional trusted package prefix(es) appended to safe packages.",
            AllowMultipleArgumentsPerToken = true
        };

        var excludeOption = new Option<string[]>("--exclude")
        {
            Description = "Additional file glob pattern(s) appended to excluded files.",
            AllowMultipleArgumentsPerToken = true
        };

        var maxChangedFilesOption = new Option<int?>("--max-changed-files")
        {
            Description = "Maximum changed files threshold for blast radius."
        };

        var maxChangedLinesOption = new Option<int?>("--max-changed-lines")
        {
            Description = "Maximum changed lines threshold for blast radius."
        };

        var maxCodebasePctOption = new Option<double?>("--max-codebase-pct")
        {
            Description = "Maximum codebase impact percentage for blast radius."
        };

        var maxFilePctOption = new Option<double?>("--max-file-pct")
        {
            Description = "Maximum file impact percentage for blast radius."
        };

        var rootCommand = new RootCommand("Git diff security, supply-chain, blast-radius, and cross-model LLM judge CLI guardrail tool.")
        {
            baseArgument,
            headArgument,
            baseOption,
            headOption,
            repoOption,
            configOption,
            twoDotOption,
            stagedOption,
            workingTreeOption,
            commitOption,
            diffFileOption,
            noBaselineOption,
            formatOption,
            outputOption,
            markdownOption,
            sarifOption,
            minSeverityOption,
            quietOption,
            llmProviderOption,
            llmModelOption,
            ollamaModelOption,
            openaiModelOption,
            llmEndpointOption,
            llmApiKeyOption,
            noLlmOption,
            filterDeletedLinesOption,
            maxChunkBytesOption,
            failOpenOnLlmErrorOption,
            noDeterministicOption,
            enablePromptInjectionOption,
            noPromptInjectionOption,
            flagPrivateIpsOption,
            allowDomainOption,
            allowIpOption,
            allowPackageOption,
            excludeOption,
            maxChangedFilesOption,
            maxChangedLinesOption,
            maxCodebasePctOption,
            maxFilePctOption
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var err = errorWriter ?? Console.Error;
            var repoPath = parseResult.GetValue(repoOption) ?? ".";
            var customConfig = parseResult.GetValue(configOption);

            // 1. Load 3-tier hierarchical configuration (System > User > Repo)
            var loader = configLoader ?? DiffGuardConfigLoader.Instance;
            DiffGuardPolicy policy;
            try
            {
                policy = loader.LoadPolicy(repoPath, customConfig);
            }
            catch (Exception ex)
            {
                await err.WriteLineAsync($"[ERROR] Failed to load configuration: {ex.Message}").ConfigureAwait(false);
                return ExitCodes.Error;
            }

            // 2. Resolve target
            var positionalBase = parseResult.GetValue(baseArgument);
            var positionalHead = parseResult.GetValue(headArgument);
            var optBase = parseResult.GetValue(baseOption);
            var optHead = parseResult.GetValue(headOption);

            var explicitBase = positionalBase ?? optBase;
            var explicitHead = positionalHead ?? optHead;

            var diffFile = parseResult.GetValue(diffFileOption);
            var commitSha = parseResult.GetValue(commitOption);
            var staged = parseResult.GetValue(stagedOption);
            var workingTree = parseResult.GetValue(workingTreeOption);
            var twoDot = parseResult.GetValue(twoDotOption);

            GitComparisonTarget target;
            if (!string.IsNullOrWhiteSpace(diffFile))
            {
                target = new GitComparisonTarget.PatchFile(diffFile);
            }
            else if (!string.IsNullOrWhiteSpace(commitSha))
            {
                target = new GitComparisonTarget.Commit(commitSha);
            }
            else if (workingTree)
            {
                target = new GitComparisonTarget.WorkingTree();
            }
            else if (staged)
            {
                target = new GitComparisonTarget.Staged();
            }
            else if (!string.IsNullOrWhiteSpace(explicitBase))
            {
                if (string.IsNullOrWhiteSpace(explicitHead) && explicitBase.Contains("...", StringComparison.Ordinal))
                {
                    int idx = explicitBase.IndexOf("...", StringComparison.Ordinal);
                    string b = explicitBase[..idx];
                    string h = explicitBase[(idx + 3)..];
                    target = new GitComparisonTarget.BranchRange(b, string.IsNullOrWhiteSpace(h) ? "HEAD" : h, TwoDot: false);
                }
                else if (string.IsNullOrWhiteSpace(explicitHead) && explicitBase.Contains("..", StringComparison.Ordinal))
                {
                    int idx = explicitBase.IndexOf("..", StringComparison.Ordinal);
                    string b = explicitBase[..idx];
                    string h = explicitBase[(idx + 2)..];
                    target = new GitComparisonTarget.BranchRange(b, string.IsNullOrWhiteSpace(h) ? "HEAD" : h, TwoDot: true);
                }
                else
                {
                    var head = !string.IsNullOrWhiteSpace(explicitHead) ? explicitHead : "HEAD";
                    target = new GitComparisonTarget.BranchRange(explicitBase, head, twoDot);
                }
            }
            else
            {
                // Default target when no positional or explicit target option passed
                target = new GitComparisonTarget.Staged();
            }

            // 3. Resolve deterministic scanner
            bool enableDeterministic;
            var noDeterministicPassed = parseResult.GetResult(noDeterministicOption) is { Implicit: false };
            if (policy.ForceDeterministic)
            {
                if (noDeterministicPassed)
                {
                    await err.WriteLineAsync("[WARNING] Deterministic diff scanning is enforced by system configuration and cannot be disabled.").ConfigureAwait(false);
                }
                enableDeterministic = true;
            }
            else
            {
                if (noDeterministicPassed)
                {
                    enableDeterministic = false;
                }
                else
                {
                    enableDeterministic = policy.EnableDeterministic;
                }
            }

            // 4. Resolve prompt injection rules
            bool enablePromptInjection;
            var noPromptPassed = parseResult.GetResult(noPromptInjectionOption) is { Implicit: false };
            var enablePromptPassed = parseResult.GetResult(enablePromptInjectionOption) is { Implicit: false };

            if (policy.ForcePromptInjectionRules)
            {
                if (noPromptPassed)
                {
                    await err.WriteLineAsync("[WARNING] Prompt injection detection is enforced by system configuration and cannot be disabled.").ConfigureAwait(false);
                }
                enablePromptInjection = true;
            }
            else
            {
                if (noPromptPassed)
                {
                    enablePromptInjection = false;
                }
                else if (enablePromptPassed)
                {
                    enablePromptInjection = true;
                }
                else
                {
                    enablePromptInjection = policy.EnablePromptInjectionRules;
                }
            }

            // 5. Resolve LLM judge
            bool enableLlm;
            var noLlmPassed = parseResult.GetResult(noLlmOption) is { Implicit: false };
            var ollamaModelPassed = parseResult.GetValue(ollamaModelOption);
            var openaiModelPassed = parseResult.GetValue(openaiModelOption);
            var llmModelPassed = parseResult.GetValue(llmModelOption);
            var llmProviderPassed = parseResult.GetValue(llmProviderOption);

            if (!string.IsNullOrEmpty(llmProviderPassed) &&
                !llmProviderPassed.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                !llmProviderPassed.Equals("ollama", StringComparison.OrdinalIgnoreCase) &&
                !llmProviderPassed.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                await err.WriteLineAsync($"[ERROR] Invalid LLM provider '{llmProviderPassed}'. Valid values are: none, ollama, openai.").ConfigureAwait(false);
                return ExitCodes.Error;
            }

            bool cliRequestedNoLlmProvider = !string.IsNullOrEmpty(llmProviderPassed) &&
                                             llmProviderPassed.Equals("none", StringComparison.OrdinalIgnoreCase);

            if (policy.ForceLlmEnabled)
            {
                if (noLlmPassed || cliRequestedNoLlmProvider)
                {
                    await err.WriteLineAsync("[WARNING] LLM judge is enforced by system configuration and cannot be disabled.").ConfigureAwait(false);
                }
                enableLlm = true;
            }
            else
            {
                if (noLlmPassed || cliRequestedNoLlmProvider)
                {
                    enableLlm = false;
                }
                else if (!string.IsNullOrEmpty(ollamaModelPassed) ||
                         !string.IsNullOrEmpty(openaiModelPassed) ||
                         !string.IsNullOrEmpty(llmModelPassed) ||
                         !string.IsNullOrEmpty(llmProviderPassed))
                {
                    enableLlm = true;
                }
                else
                {
                    enableLlm = policy.LlmEnabled;
                }
            }

            // Provider & Model resolution
            LlmProviderKind provider = LlmProviderKind.None;
            string? modelId = null;

            if (enableLlm)
            {
                string? effectiveProviderStr = (!cliRequestedNoLlmProvider && !string.IsNullOrEmpty(llmProviderPassed))
                    ? llmProviderPassed
                    : policy.LlmProvider;

                if (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmProvider))
                {
                    effectiveProviderStr = policy.LlmProvider;
                }

                string? effectiveModelStr = (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmModel))
                    ? policy.LlmModel
                    : (llmModelPassed ?? policy.LlmModel);

                if (!policy.LockPolicy && !string.IsNullOrEmpty(ollamaModelPassed))
                {
                    provider = LlmProviderKind.Ollama;
                    modelId = ollamaModelPassed;
                }
                else if (!policy.LockPolicy && !string.IsNullOrEmpty(openaiModelPassed))
                {
                    provider = LlmProviderKind.OpenAI;
                    modelId = openaiModelPassed;
                }
                else if (!string.IsNullOrEmpty(effectiveProviderStr))
                {
                    if (effectiveProviderStr.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        provider = LlmProviderKind.Ollama;
                    }
                    else if (effectiveProviderStr.Equals("openai", StringComparison.OrdinalIgnoreCase))
                    {
                        provider = LlmProviderKind.OpenAI;
                    }
                    modelId = effectiveModelStr ?? ollamaModelPassed ?? openaiModelPassed;
                }
                else if (!string.IsNullOrEmpty(ollamaModelPassed))
                {
                    provider = LlmProviderKind.Ollama;
                    modelId = effectiveModelStr ?? ollamaModelPassed;
                }
                else if (!string.IsNullOrEmpty(openaiModelPassed))
                {
                    provider = LlmProviderKind.OpenAI;
                    modelId = effectiveModelStr ?? openaiModelPassed;
                }
                else
                {
                    // Infer from environment
                    string? apiKey = (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmApiKey))
                        ? policy.LlmApiKey
                        : (parseResult.GetValue(llmApiKeyOption)
                           ?? policy.LlmApiKey
                           ?? Environment.GetEnvironmentVariable("DIFFGUARD_API_KEY")
                           ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
                    var ep = (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmEndpoint))
                        ? policy.LlmEndpoint
                        : (parseResult.GetValue(llmEndpointOption) ?? policy.LlmEndpoint);
                    bool isOllamaPort = ep != null && ep.Contains("11434", StringComparison.Ordinal);

                    if (!string.IsNullOrEmpty(apiKey) && !isOllamaPort)
                    {
                        provider = LlmProviderKind.OpenAI;
                    }
                    else
                    {
                        provider = LlmProviderKind.Ollama;
                    }
                    modelId = effectiveModelStr;
                }
            }

            // Fail-closed LLM error resolution
            bool failClosedOnError;
            var failOpenPassed = parseResult.GetResult(failOpenOnLlmErrorOption) is { Implicit: false };
            if (policy.ForceFailClosedOnError)
            {
                if (failOpenPassed)
                {
                    await err.WriteLineAsync("[WARNING] Fail-closed LLM judge policy is enforced by system configuration and cannot be disabled.").ConfigureAwait(false);
                }
                failClosedOnError = true;
            }
            else
            {
                if (failOpenPassed)
                {
                    failClosedOnError = false;
                }
                else
                {
                    failClosedOnError = policy.FailClosedOnError;
                }
            }

            // 6. Resolve Minimum Severity
            var minSeverityRaw = parseResult.GetValue(minSeverityOption);
            DiffRiskSeverity minSeverity = policy.MinimumRiskSeverity;
            if (!string.IsNullOrWhiteSpace(minSeverityRaw))
            {
                if (int.TryParse(minSeverityRaw.Trim(), out _) ||
                    !Enum.TryParse<DiffRiskSeverity>(minSeverityRaw.Trim(), ignoreCase: true, out var parsedSev) ||
                    !Enum.IsDefined(parsedSev))
                {
                    await err.WriteLineAsync($"[ERROR] Invalid minimum risk severity '{minSeverityRaw}'. Valid values are: Low, Medium, High, Critical.").ConfigureAwait(false);
                    return ExitCodes.Error;
                }
                minSeverity = parsedSev;
            }

            // System severity floor enforcement
            if (policy.SystemMinimumRiskSeverityFloor.HasValue && minSeverity > policy.SystemMinimumRiskSeverityFloor.Value)
            {
                await err.WriteLineAsync($"[WARNING] Minimum risk severity '{minSeverity}' is more permissive than system configuration floor '{policy.SystemMinimumRiskSeverityFloor.Value}'. Clamping to '{policy.SystemMinimumRiskSeverityFloor.Value}'.").ConfigureAwait(false);
                minSeverity = policy.SystemMinimumRiskSeverityFloor.Value;
            }

            // 7. Resolve Thresholds (enforce system ceilings)
            int maxFiles = parseResult.GetValue(maxChangedFilesOption) ?? policy.MaxChangedFiles;
            if (policy.SystemMaxChangedFilesCeiling.HasValue && maxFiles > policy.SystemMaxChangedFilesCeiling.Value)
            {
                await err.WriteLineAsync($"[WARNING] MaxChangedFiles exceeds system ceiling {policy.SystemMaxChangedFilesCeiling.Value}. Clamping.").ConfigureAwait(false);
                maxFiles = policy.SystemMaxChangedFilesCeiling.Value;
            }

            int maxLines = parseResult.GetValue(maxChangedLinesOption) ?? policy.MaxChangedLines;
            if (policy.SystemMaxChangedLinesCeiling.HasValue && maxLines > policy.SystemMaxChangedLinesCeiling.Value)
            {
                await err.WriteLineAsync($"[WARNING] MaxChangedLines exceeds system ceiling {policy.SystemMaxChangedLinesCeiling.Value}. Clamping.").ConfigureAwait(false);
                maxLines = policy.SystemMaxChangedLinesCeiling.Value;
            }

            double maxCodebasePct = parseResult.GetValue(maxCodebasePctOption) ?? policy.MaxCodebaseImpactPercent;
            if (policy.SystemMaxCodebaseImpactPercentCeiling.HasValue && maxCodebasePct > policy.SystemMaxCodebaseImpactPercentCeiling.Value)
            {
                await err.WriteLineAsync($"[WARNING] MaxCodebaseImpactPercent exceeds system ceiling {policy.SystemMaxCodebaseImpactPercentCeiling.Value}. Clamping.").ConfigureAwait(false);
                maxCodebasePct = policy.SystemMaxCodebaseImpactPercentCeiling.Value;
            }

            double maxFilePct = parseResult.GetValue(maxFilePctOption) ?? policy.MaxFileImpactPercent;
            if (policy.SystemMaxFileImpactPercentCeiling.HasValue && maxFilePct > policy.SystemMaxFileImpactPercentCeiling.Value)
            {
                await err.WriteLineAsync($"[WARNING] MaxFileImpactPercent exceeds system ceiling {policy.SystemMaxFileImpactPercentCeiling.Value}. Clamping.").ConfigureAwait(false);
                maxFilePct = policy.SystemMaxFileImpactPercentCeiling.Value;
            }

            // 8. Resolve Allowlists
            var domains = new List<string>(policy.WellKnownSafeDomains);
            var ips = new List<string>(policy.AllowedIpAddresses);
            var packages = new List<string>(policy.WellKnownSafePackages);
            var excludes = new List<string>(policy.ExcludedFileGlobs);

            var cliDomains = parseResult.GetValue(allowDomainOption);
            var cliIps = parseResult.GetValue(allowIpOption);
            var cliPackages = parseResult.GetValue(allowPackageOption);
            var cliExcludes = parseResult.GetValue(excludeOption);

            bool allowAdditions = policy.AllowLowerTierAllowlistAdditions;
            if (!allowAdditions &&
                ((cliDomains != null && cliDomains.Length > 0) ||
                 (cliIps != null && cliIps.Length > 0) ||
                 (cliPackages != null && cliPackages.Length > 0) ||
                 (cliExcludes != null && cliExcludes.Length > 0)))
            {
                await err.WriteLineAsync("[WARNING] Custom allowlist additions are disallowed by system configuration.").ConfigureAwait(false);
            }
            else
            {
                if (cliDomains != null)
                {
                    foreach (var d in cliDomains)
                    {
                        if (!string.IsNullOrWhiteSpace(d) && !domains.Contains(d, StringComparer.OrdinalIgnoreCase))
                            domains.Add(d);
                    }
                }
                if (cliIps != null)
                {
                    foreach (var ip in cliIps)
                    {
                        if (!string.IsNullOrWhiteSpace(ip) && !ips.Contains(ip, StringComparer.OrdinalIgnoreCase))
                            ips.Add(ip);
                    }
                }
                if (cliPackages != null)
                {
                    foreach (var p in cliPackages)
                    {
                        if (!string.IsNullOrWhiteSpace(p) && !packages.Contains(p, StringComparer.OrdinalIgnoreCase))
                            packages.Add(p);
                    }
                }
                if (cliExcludes != null)
                {
                    foreach (var ex in cliExcludes)
                    {
                        if (!string.IsNullOrWhiteSpace(ex) && !excludes.Contains(ex, StringComparer.OrdinalIgnoreCase))
                            excludes.Add(ex);
                    }
                }
            }

            // 9. Format Resolution
            var formatStr = (parseResult.GetValue(formatOption) ?? "console").Trim();
            OutputFormat format;
            switch (formatStr.ToLowerInvariant())
            {
                case "console":
                    format = OutputFormat.Console;
                    break;
                case "sarif":
                    format = OutputFormat.Sarif;
                    break;
                case "markdown":
                case "md":
                    format = OutputFormat.Markdown;
                    break;
                default:
                    await err.WriteLineAsync($"[ERROR] Invalid output format '{formatStr}'. Valid formats are: console, markdown (md), sarif.").ConfigureAwait(false);
                    return ExitCodes.Error;
            }

            int maxChunkBytes = parseResult.GetValue(maxChunkBytesOption) ?? policy.MaxChunkBytes;
            if (maxChunkBytes <= 0)
            {
                await err.WriteLineAsync("[ERROR] --max-chunk-bytes must be greater than zero.").ConfigureAwait(false);
                return ExitCodes.Error;
            }

            string? resolvedEndpoint = (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmEndpoint))
                ? policy.LlmEndpoint
                : (parseResult.GetValue(llmEndpointOption) ?? policy.LlmEndpoint);

            string? resolvedApiKey = (policy.LockPolicy && !string.IsNullOrEmpty(policy.LlmApiKey))
                ? policy.LlmApiKey
                : (parseResult.GetValue(llmApiKeyOption) ?? policy.LlmApiKey);

            var options = new DiffGuardCliOptions
            {
                Target = target,
                RepoPath = repoPath,
                CustomConfigPath = customConfig,
                SkipBaseline = parseResult.GetValue(noBaselineOption),
                PrimaryFormat = format,
                OutputPath = parseResult.GetValue(outputOption),
                MarkdownPath = parseResult.GetValue(markdownOption),
                SarifPath = parseResult.GetValue(sarifOption),
                MinimumRiskSeverity = minSeverity,
                Quiet = parseResult.GetValue(quietOption),
                EnableDeterministic = enableDeterministic,
                EnableNetworkEndpointRules = policy.EnableNetworkEndpointRules,
                EnableBackdoorSignatureRules = policy.EnableBackdoorSignatureRules,
                EnableSecretAndEntropyRules = policy.EnableSecretAndEntropyRules,
                EnableEncodedPayloadRules = policy.EnableEncodedPayloadRules,
                EnableUnicodeObfuscationRules = policy.EnableUnicodeObfuscationRules,
                EnableBinaryFileRules = policy.EnableBinaryFileRules,
                EnableDependencyRules = policy.EnableDependencyRules,
                EnableBlastRadiusRules = policy.EnableBlastRadiusRules,
                EnablePromptInjection = enablePromptInjection,
                FlagPrivateNetworkIps = parseResult.GetValue(flagPrivateIpsOption) || policy.FlagPrivateNetworkIps,
                EnableTestTolerance = policy.EnableTestTolerance,
                AllowUnverifiedImageExtensionsWhenStreamUnavailable = policy.AllowUnverifiedImageExtensionsWhenStreamUnavailable,
                MaxChangedFiles = maxFiles,
                MaxChangedLines = maxLines,
                MaxCodebaseImpactPercent = maxCodebasePct,
                MaxFileImpactPercent = maxFilePct,
                ShannonEntropyThreshold = policy.ShannonEntropyThreshold,
                EntropyMinStringLength = policy.EntropyMinStringLength,
                MinSuspiciousBase64Length = policy.MinSuspiciousBase64Length,
                MinSuspiciousHexLength = policy.MinSuspiciousHexLength,
                MaxLineLength = policy.MaxLineLength,
                WellKnownSafeDomains = domains,
                AllowedIpAddresses = ips,
                WellKnownSafePackages = packages,
                WellKnownSafeTestPackages = policy.WellKnownSafeTestPackages,
                AllowedImageExtensions = policy.AllowedImageExtensions,
                MaxLineLengthExcludedExtensions = policy.MaxLineLengthExcludedExtensions,
                ExcludedFileGlobs = excludes,
                EnableLlm = enableLlm,
                LlmProvider = provider,
                LlmModel = modelId,
                LlmEndpoint = resolvedEndpoint,
                LlmApiKey = resolvedApiKey,
                MaxChunkBytes = maxChunkBytes,
                FilterDeletedLines = parseResult.GetValue(filterDeletedLinesOption) || policy.FilterDeletedLines,
                MaxConsecutiveDeletedLines = policy.MaxConsecutiveDeletedLines,
                FailClosedOnError = failClosedOnError,
                LoadedConfigPaths = policy.LoadedConfigPaths
            };

            var runner = runnerFactory != null ? runnerFactory() : new DiffGuardRunner();
            return await runner.RunAsync(options, cancellationToken).ConfigureAwait(false);
        });

        return rootCommand;
    }
}

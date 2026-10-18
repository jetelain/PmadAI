using System.Text;
using System.Text.Json;

namespace Pmad.AI.Benchmark.Scenarios.Compaction;

/// <summary>
/// Domain model for compaction-targeted benchmark trajectories.
/// </summary>
public sealed class CompactionTrajectory
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<CompactionTurnData> Turns { get; set; } = [];
}

/// <summary>
/// A single turn in a compaction benchmark trajectory.
/// </summary>
public sealed class CompactionTurnData
{
    public int TurnIndex { get; set; }
    public string UserPrompt { get; set; } = string.Empty;
    public string? ExpectedToolName { get; set; }
    public bool AllowAnyTool { get; set; }
    public bool IsRecallProbe { get; set; }
    public string? Observation { get; set; }
    public string? ExpectedCompletionSubstring { get; set; }
}

/// <summary>
/// Generates authentic, large-scale (15–40 KB) logs and deep-context trajectories
/// specifically designed to activate and evaluate ToolResultCompactor and ThinkingTraceCompactor.
/// </summary>
public static class CompactionFixtureGenerator
{
    public static CompactionTrajectory CreateLargeToolTrajectory()
    {
        var trajectory = new CompactionTrajectory
        {
            ScenarioId = "compaction-large-tool",
            Name = "Compaction: Large Compiler Logs & Source Replay",
            Description = "Evaluates ToolResultCompactor against 15-35 KB compiler test logs, grep outputs, and full source bodies across 7 turns."
        };

        // Turn 1: 25 KB Build & Test Log
        var buildLog = GenerateLargeBuildLog();
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 1,
            UserPrompt = "Please run 'run_tests' to execute our billing integration test suite and diagnose any compilation or runtime failures.",
            ExpectedToolName = "run_tests",
            AllowAnyTool = false,
            Observation = buildLog
        });

        // Turn 2: 30 KB Source File
        var sourceCode = GenerateLargeSourceFile();
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 2,
            UserPrompt = "Inspect 'src/Billing/SubscriptionService.cs' with 'read_file' to locate where SubscriptionTier is declared and used.",
            ExpectedToolName = "read_file",
            AllowAnyTool = false,
            Observation = sourceCode
        });

        // Turn 3: 18 KB Grep Search Output
        var grepOutput = GenerateLargeGrepOutput();
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 3,
            UserPrompt = "Search the repository using 'search_code' for 'SubscriptionLevel' or enum migration notes.",
            ExpectedToolName = "search_code",
            AllowAnyTool = false,
            Observation = grepOutput
        });

        // Turn 4: 15 KB Enum Definition File
        var enumCode = GenerateLargeEnumFile();
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 4,
            UserPrompt = "Read 'src/Contracts/SubscriptionEnums.cs' with 'read_file' to verify the exact enum members.",
            ExpectedToolName = "read_file",
            AllowAnyTool = false,
            Observation = enumCode
        });

        // Turn 5: Apply Patch Result (1 KB)
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 5,
            UserPrompt = "Apply the patch using 'apply_patch' to 'src/Billing/SubscriptionService.cs' replacing SubscriptionTier with SubscriptionLevel.",
            ExpectedToolName = "apply_patch",
            AllowAnyTool = false,
            Observation = "Patch applied successfully to 'src/Billing/SubscriptionService.cs'.\n2 hunks applied cleanly (lines 142 and 188). 0 rejected hunks."
        });

        // Turn 6: 20 KB Test Run Success Log
        var successLog = GenerateLargeSuccessLog();
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 6,
            UserPrompt = "Re-run 'run_tests' to verify that the billing integration suite builds and passes cleanly.",
            ExpectedToolName = "run_tests",
            AllowAnyTool = false,
            Observation = successLog
        });

        // Turn 7: Final Synthesis
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 7,
            UserPrompt = "Summarize the root cause of the billing compilation failure and confirm that all tests passed.",
            ExpectedToolName = null,
            IsRecallProbe = true,
            ExpectedCompletionSubstring = "SubscriptionLevel"
        });

        return trajectory;
    }

    public static CompactionTrajectory CreateDeepContextTrajectory()
    {
        var trajectory = new CompactionTrajectory
        {
            ScenarioId = "compaction-deep-context",
            Name = "Compaction: Deep 10-Turn Context with Needle Recall",
            Description = "Evaluates context retention and needle retrieval across a 10-turn DevOps workflow generating >120 KB of diagnostics."
        };

        const string needle = "OMEGA-8842-PROBE-SECURE";

        // Turn 1
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 1,
            UserPrompt = "Run diagnostic health check for cluster node eu-west-01 using 'run_tests'.",
            ExpectedToolName = "run_tests",
            AllowAnyTool = false,
            Observation = GenerateDiagnosticPayload("Node Health Diagnostics: CPU 42%, Mem 68%, LoadAvg: 1.42, 0.98, 0.85.", 12000)
        });

        // Turn 2: Needle in prefix!
        var authLog = new StringBuilder();
        authLog.AppendLine("[CLUSTER-SECURITY-AUDIT] Cluster Vault Secret Token: " + needle + " (Issued by Auth Authority: 2026-10-18)");
        authLog.AppendLine("TLS Certificate SHA256: 4a8f9c2d1e0b5a3e7f6d8c9a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c");
        authLog.AppendLine(GenerateDiagnosticPayload("Security Policy Audit and Role-Based Access Control Matrices.", 15000));

        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 2,
            UserPrompt = "Inspect cluster authentication credentials and vault configuration using 'search_code'.",
            ExpectedToolName = "search_code",
            AllowAnyTool = false,
            Observation = authLog.ToString()
        });

        // Turns 3 to 9: Large diagnostics (each 12-16 KB)
        var topics = new[]
        {
            ("PostgreSQL Database Pool & Latency", "read_file", 14000),
            ("Redis Distributed Cache & Replication", "run_tests", 13000),
            ("Envoy Ingress Mesh & Routing Rules", "search_code", 16000),
            ("Kubernetes Pod Deployment & cgroups", "read_file", 15000),
            ("Network Throughput & TCP Window Sizing", "run_tests", 14000),
            ("Kafka Event Log Partition Offsets", "search_code", 13000),
            ("Pre-flight Cluster Verification", "run_tests", 12000)
        };

        for (int i = 0; i < topics.Length; i++)
        {
            var (title, tool, targetBytes) = topics[i];
            trajectory.Turns.Add(new CompactionTurnData
            {
                TurnIndex = i + 3,
                UserPrompt = $"Execute diagnostic check for {title} using '{tool}'.",
                ExpectedToolName = tool,
                AllowAnyTool = false,
                Observation = GenerateDiagnosticPayload(title, targetBytes)
            });
        }

        // Turn 10: Needle Probe
        trajectory.Turns.Add(new CompactionTurnData
        {
            TurnIndex = 10,
            UserPrompt = "Diagnostics complete. For audit compliance, what was the Cluster Vault Secret Token issued by Auth Authority during initial configuration?",
            ExpectedToolName = null,
            IsRecallProbe = true,
            ExpectedCompletionSubstring = needle
        });

        return trajectory;
    }

    private static string GenerateDiagnosticPayload(string header, int targetLength)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {header} ===");
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:O} | Subsystem: Distributed Orchestrator v4.2.1");
        sb.AppendLine("Status: Operational | Telemetry Stream: Active | Error Count: 0 | Warning Count: 2");

        int line = 1;
        while (sb.Length < targetLength)
        {
            sb.AppendLine($"[{line:D5}] Metric probe sub-system sample {line}: latency={12.4 + (line % 7):F2}ms, throughput={1024 * (line % 15)} ops/sec, error_rate=0.000%, buffer_utilization={30 + (line % 50)}%");
            line++;
        }

        sb.AppendLine("=== End of Diagnostic Stream ===");
        return sb.ToString();
    }

    private static string GenerateLargeBuildLog()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft (R) Build Engine version 17.12.0+63401e18d for .NET");
        sb.AppendLine("Copyright (C) Microsoft Corporation. All rights reserved.");
        sb.AppendLine();
        sb.AppendLine("  Determining projects to restore...");
        sb.AppendLine("  All projects are up-to-date for restore.");
        sb.AppendLine("  Billing -> C:\\Repo\\src\\Billing\\bin\\Debug\\net10.0\\Billing.dll");

        for (int i = 1; i <= 35; i++)
        {
            sb.AppendLine($"C:\\Repo\\src\\Billing\\Legacy\\BillingHelper_{i}.cs(24,12): warning CS1591: Missing XML comment for publicly visible type or member 'BillingHelper_{i}.ComputeInternalRate(decimal)'");
            sb.AppendLine($"C:\\Repo\\src\\Billing\\Legacy\\BillingHelper_{i}.cs(58,19): warning CS8618: Non-nullable property 'TransactionId' must contain a non-null value when exiting constructor.");
        }

        sb.AppendLine();
        sb.AppendLine("C:\\Repo\\src\\Billing\\SubscriptionService.cs(142,35): error CS0246: The type or namespace name 'SubscriptionTier' could not be found (are you missing a using directive or an assembly reference?) [C:\\Repo\\src\\Billing\\Billing.csproj]");
        sb.AppendLine("C:\\Repo\\src\\Billing\\SubscriptionService.cs(188,42): error CS0246: The type or namespace name 'SubscriptionTier' could not be found (are you missing a using directive or an assembly reference?) [C:\\Repo\\src\\Billing\\Billing.csproj]");
        sb.AppendLine();
        sb.AppendLine("[xUnit.net 00:00:01.45] Billing.Tests.PaymentGatewayTests.ProcessCreditCard_Valid_ReturnsSuccess [PASS]");
        sb.AppendLine("[xUnit.net 00:00:01.62] Billing.Tests.PaymentGatewayTests.ProcessRefund_ValidInvoice_Succeeds [PASS]");
        sb.AppendLine("[xUnit.net 00:00:01.78] Billing.Tests.SubscriptionTests.ActivateSubscription_FailsWithMissingType [FAIL]");
        sb.AppendLine("   System.TypeLoadException: Could not load type 'Pmad.Contracts.SubscriptionTier' from assembly 'Pmad.Contracts'.");
        sb.AppendLine("   at Billing.SubscriptionService.ActivateSubscription(String userId, SubscriptionTier tier) in C:\\Repo\\src\\Billing\\SubscriptionService.cs:line 142");
        sb.AppendLine("   at Billing.Tests.SubscriptionTests.ActivateSubscription_FailsWithMissingType() in C:\\Repo\\tests\\Billing.Tests\\SubscriptionTests.cs:line 64");

        // Pad with realistic diagnostic compiler output to ~26 KB
        int step = 1;
        while (sb.Length < 26000)
        {
            sb.AppendLine($"  [CSC Diagnostic Trace #{step++}] C:\\Program Files\\dotnet\\sdk\\10.0.100\\Roslyn\\bincore\\csc.exe /noconfig /unsafe- /checked- /nowarn:1701,1702,IL2026,IL2104 /fullpaths /nostdlib+ /errorreport:prompt /warn:4 /define:TRACE;DEBUG;NET;NET10_0 /reference:C:\\Repo\\artifacts\\bin\\Pmad.Contracts\\Debug\\net10.0\\Pmad.Contracts.dll");
        }

        sb.AppendLine("Build FAILED.");
        sb.AppendLine("    80 Warning(s)");
        sb.AppendLine("    2 Error(s)");
        sb.AppendLine("Time Elapsed 00:00:03.82");
        return sb.ToString();
    }

    private static string GenerateLargeSourceFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated-by-pmad-billing-pipeline />");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Concurrent;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using Pmad.Contracts;");
        sb.AppendLine();
        sb.AppendLine("namespace Pmad.Billing;");
        sb.AppendLine();
        sb.AppendLine("public sealed class SubscriptionService : ISubscriptionService");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly ILogger<SubscriptionService> _logger;");
        sb.AppendLine("    private readonly ConcurrentDictionary<string, SubscriptionRecord> _activeSubscriptions = new();");
        sb.AppendLine();
        sb.AppendLine("    public SubscriptionService(ILogger<SubscriptionService> logger)");
        sb.AppendLine("    {");
        sb.AppendLine("        _logger = logger ?? throw new ArgumentNullException(nameof(logger));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Activates a new user subscription plan.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public async Task<SubscriptionRecord> ActivateSubscription(string userId, SubscriptionTier tier)");
        sb.AppendLine("    {");
        sb.AppendLine("        _logger.LogInformation(\"Activating plan {Tier} for user {UserId}\", tier, userId);");
        sb.AppendLine("        var record = new SubscriptionRecord(userId, tier.ToString(), DateTime.UtcNow);");
        sb.AppendLine("        _activeSubscriptions[userId] = record;");
        sb.AppendLine("        await Task.Yield();");
        sb.AppendLine("        return record;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public async Task<bool> CancelSubscription(string userId)");
        sb.AppendLine("    {");
        sb.AppendLine("        await Task.Yield();");
        sb.AppendLine("        return _activeSubscriptions.TryRemove(userId, out _);");
        sb.AppendLine("    }");

        // Pad with realistic domain methods up to ~32 KB
        int block = 1;
        while (sb.Length < 32000)
        {
            sb.AppendLine($@"
    /// <summary>
    /// Auxiliary billing validator module {block}.
    /// </summary>
    public sealed class BillingModule_{block}
    {{
        public string ModuleId {{ get; }} = ""module-{block}"";
        public bool IsEnabled {{ get; set; }} = true;
        public decimal CalculateProcessingFee(decimal amount, int tierLevel)
        {{
            if (amount <= 0m) return 0m;
            decimal baseRate = 0.029m + (tierLevel * 0.001m);
            return Math.Round(amount * baseRate + 0.30m, 2);
        }}
    }}");
            block++;
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateLargeGrepOutput()
    {
        var sb = new StringBuilder();
        sb.AppendLine("src/Billing/SubscriptionService.cs:142: public async Task<SubscriptionRecord> ActivateSubscription(string userId, SubscriptionTier tier)");
        sb.AppendLine("src/Billing/SubscriptionService.cs:188: public SubscriptionTier CurrentTier { get; set; }");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:12: // MIGRATION NOTE: SubscriptionTier was renamed to SubscriptionLevel in Contracts v2.0.");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:15: public enum SubscriptionLevel");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:17:     Free = 0,");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:18:     Starter = 1,");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:19:     Pro = 2,");
        sb.AppendLine("src/Contracts/SubscriptionEnums.cs:20:     Enterprise = 3");
        sb.AppendLine("docs/migrations/v2-contracts.md:34: - SubscriptionTier renamed to SubscriptionLevel for semantic consistency.");

        int line = 1;
        while (sb.Length < 19000)
        {
            sb.AppendLine($"docs/changelogs/archive_{line}.md:{10 + line}: Referenced SubscriptionTier in historical changelog version 1.{line}.0 release notes.");
            line++;
        }

        return sb.ToString();
    }

    private static string GenerateLargeEnumFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace Pmad.Contracts;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Defines available subscription tiers in Contracts v2.0.");
        sb.AppendLine("/// Replaces legacy SubscriptionTier enum.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public enum SubscriptionLevel");
        sb.AppendLine("{");
        sb.AppendLine("    Free = 0,");
        sb.AppendLine("    Starter = 1,");
        sb.AppendLine("    Pro = 2,");
        sb.AppendLine("    Enterprise = 3");
        sb.AppendLine("}");
        sb.AppendLine();

        int block = 1;
        while (sb.Length < 16000)
        {
            sb.AppendLine($@"
/// <summary>
/// Auxiliary Contract Enum #{block}
/// </summary>
public enum BillingCycle_{block}
{{
    Monthly = 1,
    Quarterly = 3,
    Annual = 12
}}");
            block++;
        }

        return sb.ToString();
    }

    private static string GenerateLargeSuccessLog()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft (R) Build Engine version 17.12.0+63401e18d for .NET");
        sb.AppendLine("  Billing -> C:\\Repo\\src\\Billing\\bin\\Debug\\net10.0\\Billing.dll");
        sb.AppendLine("  Billing.Tests -> C:\\Repo\\tests\\Billing.Tests\\bin\\Debug\\net10.0\\Billing.Tests.dll");
        sb.AppendLine();
        sb.AppendLine("Starting test execution, please wait...");
        sb.AppendLine("A total of 1 test files matched the specified pattern.");

        for (int i = 1; i <= 30; i++)
        {
            sb.AppendLine($"  Passed Billing.Tests.PaymentTests.Test_Payment_Scenario_{i:D2} [42 ms]");
            sb.AppendLine($"  Passed Billing.Tests.SubscriptionTests.ActivateSubscription_WithLevel_{i:D2}_Succeeds [18 ms]");
        }

        sb.AppendLine();
        sb.AppendLine("Passed! - Failed: 0, Passed: 60, Skipped: 0, Total: 60, Duration: 1.42s - Billing.Tests.dll (net10.0)");
        sb.AppendLine("Build succeeded.");
        sb.AppendLine("    0 Warning(s)");
        sb.AppendLine("    0 Error(s)");
        sb.AppendLine("Time Elapsed 00:00:02.18");

        while (sb.Length < 21000)
        {
            sb.AppendLine("  Test host process terminated gracefully. Diagnostic telemetry flushed to disk successfully.");
        }

        return sb.ToString();
    }
}

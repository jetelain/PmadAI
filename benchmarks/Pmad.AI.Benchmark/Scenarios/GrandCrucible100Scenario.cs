namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// 100-turn repository audit, remediation and release session, with recall probes at turns 35, 55, 75 and 98.
/// </summary>
public sealed class GrandCrucible100Scenario : LongHorizonScenario
{
    public override string Id => "grand-crucible-100";
    public override string Name => "Grand Crucible (100 Turns)";
    public override string Description => "100-turn security audit, remediation and release session with early anchor facts recalled across four probes.";

    protected override int TotalTurns => 100;
    protected override string Topic => "security remediation and release";

    protected override IReadOnlyList<string> ToolNames =>
    [
        "read_file", "write_file", "apply_patch", "search_code", "execute_sast_scan",
        "build_solution", "run_unit_tests", "run_benchmarks", "git_command"
    ];

    protected override IReadOnlyList<Anchor> Anchors =>
    [
        new(4, "execute_sast_scan", "run the static security scan.", "CVE-2026-4412 flagged in JwtValidator v2.1.0; minimum patched version is v2.4.5."),
        new(12, "read_file", "inspect the connection pool manager.", "Connection leak at ConnectionPoolManager.cs:L142: DisposeAsync omitted on timeout."),
        new(18, "search_code", "search for hardcoded secrets.", "Hardcoded testing token found: TEST_TOKEN_SECRET_98812."),
        new(68, "run_benchmarks", "run the micro-benchmarks.", "Throughput improved from 1,240 ops/sec to 18,900 ops/sec.")
    ];

    protected override IReadOnlyList<Probe> Probes =>
    [
        new(35, "Which file and line number had the connection leak flagged during the initial audit?", ["ConnectionPoolManager.cs", "142"]),
        new(55, "Verify the exact secret token string that was excised from the codebase.", ["TEST_TOKEN_SECRET_98812"]),
        new(75, "What was the CVE identifier and minimum safe patch version identified in Phase 1?", ["CVE-2026-4412", "v2.4.5"]),
        new(98, "Give an executive summary: the CVE and safe version, the connection leak line, and the throughput gain.",
            ["CVE-2026-4412", "v2.4.5", "142", "18,900"])
    ];
}

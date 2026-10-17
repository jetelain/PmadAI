namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// 50-turn autonomous refactoring of a monolith into CQRS, with recall probes at turns 25 and 49.
/// </summary>
public sealed class MonolithMigration50Scenario : LongHorizonScenario
{
    public override string Id => "monolith-migration-50";
    public override string Name => "Monolith Migration (50 Turns)";
    public override string Description => "50-turn CQRS refactoring session with early anchor facts recalled at turns 25 and 49.";

    protected override int TotalTurns => 50;
    protected override string Topic => "CQRS migration";

    protected override IReadOnlyList<string> ToolNames =>
        ["read_file", "write_file", "search_code", "compile_project", "run_tests", "check_git_diff"];

    protected override IReadOnlyList<Anchor> Anchors =>
    [
        new(3, "read_file", "inspect the legacy validation rule and report it.", "Legacy validation rule emits error code ERR-9021-AUTH-EXPIRED."),
        new(7, "read_file", "inspect the database context and report the schema.", "Database column type: datetime2(7) DEFAULT SYSUTCDATETIME()."),
        new(24, "run_tests", "run the test suite.", "Failed test: TestExpiredTokenIsRejected_ReturnsUnauthorized (1 failed, 31 passed).")
    ];

    protected override IReadOnlyList<Probe> Probes =>
    [
        new(25, "What exact error code was identified in Step 3 for expired authentication?", ["ERR-9021-AUTH-EXPIRED"]),
        new(49, "Recall from our early analysis: (1) the exact database column type from Step 7, and (2) the failing test method name from Step 24?",
            ["datetime2(7)", "TestExpiredTokenIsRejected_ReturnsUnauthorized"])
    ];
}

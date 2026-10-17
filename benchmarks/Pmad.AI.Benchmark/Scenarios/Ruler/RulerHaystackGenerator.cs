using System.Text;
using Pmad.AI.Tokens;

namespace Pmad.AI.Benchmark.Scenarios.Ruler;

/// <summary>
/// Generates realistic synthetic haystacks composed of authentic code, logs, and technical documentation
/// with needles placed at configurable context depths (e.g. 10%, 25%, 50%, 75%, 90%).
/// </summary>
public static class RulerHaystackGenerator
{
    private static readonly string[] _codeTemplates =
    [
        @"
public sealed class ConnectionPoolManager_{0} : IAsyncDisposable
{{
    private readonly ConcurrentQueue<DbConnection> _connections = new();
    private readonly int _maxPoolSize = {1};
    private int _currentCount;

    public async ValueTask<DbConnection> AcquireConnectionAsync(CancellationToken cancellationToken = default)
    {{
        if (_connections.TryDequeue(out var conn))
        {{
            return conn;
        }}

        if (Interlocked.Increment(ref _currentCount) <= _maxPoolSize)
        {{
            return await CreateNewConnectionAsync(cancellationToken);
        }}

        Interlocked.Decrement(ref _currentCount);
        throw new TimeoutException(""Connection pool exhausted on cluster node {0}."");
    }}

    private async Task<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken)
    {{
        var conn = new SqlConnection(""Server=cluster-{0}.db.internal;Database=App_{1};Trusted_Connection=True;"");
        await conn.OpenAsync(cancellationToken);
        return conn;
    }}

    public ValueTask DisposeAsync()
    {{
        while (_connections.TryDequeue(out var conn))
        {{
            conn.Dispose();
        }}
        return ValueTask.CompletedTask;
    }}
}}
",
        @"
// [Diagnostic Trace {0}]
// Timestamp: 2026-10-17T14:{1:D2}:00.000Z | ThreadId: {1} | Severity: Information
// Assembly: Pmad.Security.Core v2.4.{0} | Architecture: x64
[EventSource(Name = ""Pmad-Security-Audit-{0}"")]
public sealed class SecurityAuditSource_{0} : EventSource
{{
    public static readonly SecurityAuditSource_{0} Log = new();

    [Event(1, Message = ""Token signature verification succeeded for principal ID {1}."", Level = EventLevel.Informational)]
    public void TokenValidated(long principalId) => WriteEvent(1, principalId);

    [Event(2, Message = ""Authorization cache refreshed with {1} entries."", Level = EventLevel.Verbose)]
    public void CacheRefreshed(int count) => WriteEvent(2, count);
}}
",
        @"
-- Table schema migration partition {0}
CREATE TABLE dbo.AuditRecords_Partition_{0} (
    RecordId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    SessionToken VARCHAR(128) NOT NULL,
    EventTimestamp DATETIME2(7) NOT NULL CONSTRAINT DF_Audit_{0} DEFAULT SYSUTCDATETIME(),
    ActionType NVARCHAR(64) NOT NULL,
    PayloadJson NVARCHAR(MAX) NULL
);
CREATE NONCLUSTERED INDEX IX_Audit_{0}_Tenant ON dbo.AuditRecords_Partition_{0}(TenantId, EventTimestamp);
"
    ];

    /// <summary>
    /// Generates a realistic haystack containing the specified needles at designated fractional depths (0.0 to 1.0).
    /// </summary>
    public static string GenerateHaystack(
        int targetTokens,
        IReadOnlyList<(double DepthFraction, string NeedleText)> needles)
    {
        var targetChars = targetTokens * 4; // ~4 chars per token approximation
        var sortedNeedles = needles.OrderBy(n => n.DepthFraction).ToList();

        var sb = new StringBuilder(targetChars + 2048);
        int templateIdx = 0;
        int nextNeedleIdx = 0;

        while (sb.Length < targetChars)
        {
            var currentFraction = (double)sb.Length / targetChars;

            // Check if it's time to insert the next needle
            if (nextNeedleIdx < sortedNeedles.Count && currentFraction >= sortedNeedles[nextNeedleIdx].DepthFraction)
            {
                sb.AppendLine();
                sb.AppendLine("// ===========================================================================");
                sb.AppendLine($"// [IMPORTANT SYSTEM ANCHOR FACT]");
                sb.AppendLine($"// {sortedNeedles[nextNeedleIdx].NeedleText}");
                sb.AppendLine("// ===========================================================================");
                sb.AppendLine();
                nextNeedleIdx++;
            }

            var template = _codeTemplates[templateIdx % _codeTemplates.Length];
            sb.AppendFormat(template, templateIdx, (templateIdx * 7) % 60);
            sb.AppendLine();
            templateIdx++;
        }

        // Insert any remaining needles if target length was reached early
        while (nextNeedleIdx < sortedNeedles.Count)
        {
            sb.AppendLine();
            sb.AppendLine($"// [IMPORTANT SYSTEM ANCHOR FACT: {sortedNeedles[nextNeedleIdx].NeedleText}]");
            nextNeedleIdx++;
        }

        return sb.ToString();
    }
}

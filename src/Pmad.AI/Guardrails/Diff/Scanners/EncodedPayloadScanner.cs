using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diff added lines for Base64 and Hex decoded payloads, decoding function invocations, and dense byte literals.
/// </summary>
public sealed partial class EncodedPayloadScanner
{
    [GeneratedRegex(@"\b(?:Convert\.FromBase64String|atob|base64\.b64decode)\b|Buffer\.from\s*\([^)]*['""]base64['""]", RegexOptions.NonBacktracking)]
    private static partial Regex Base64DecodeInvocationRegex();

    [GeneratedRegex(@"(?:""|'|`)([A-Za-z0-9+/\-_]{16,}={0,2})(?:""|'|`)", RegexOptions.NonBacktracking)]
    private static partial Regex Base64LiteralRegex();

    [GeneratedRegex(@"\b(?:Convert\.FromHexString|bytes\.fromhex)\b|Buffer\.from\s*\([^)]*['""]hex['""]", RegexOptions.NonBacktracking)]
    private static partial Regex HexDecodeInvocationRegex();

    [GeneratedRegex(@"(?:""|'|`)([0-9a-fA-F]{16,})(?:""|'|`)", RegexOptions.NonBacktracking)]
    private static partial Regex HexLiteralRegex();

    [GeneratedRegex(@"(?:\\x[0-9a-fA-F]{2}){16,}", RegexOptions.NonBacktracking)]
    private static partial Regex HexEscapesRegex();

    [GeneratedRegex(@"(?:new\s+byte\s*\[\s*\]\s*\{|\[)\s*0x[0-9a-fA-F]{1,2}(?:\s*,\s*0x[0-9a-fA-F]{1,2}){15,}", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex DenseByteArrayRegex();

    private readonly DiffRiskScannerOptions _options;

    public EncodedPayloadScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(IReadOnlyList<FileDiff> files)
    {
        if (!_options.EnableEncodedPayloadRules || files.Count == 0)
        {
            return [];
        }

        var findings = new List<DiffRiskFinding>();
        foreach (var file in files)
        {
            foreach (var line in file.AddedLines)
            {
                findings.AddRange(ScanLine(line));
            }
        }

        return findings;
    }

    public List<DiffRiskFinding> ScanLine(DiffAddedLine line)
    {
        var findings = new List<DiffRiskFinding>();
        if (string.IsNullOrWhiteSpace(line.Content))
        {
            return findings;
        }

        // 1. Base64 Decode Invocations (PAY-B64-001)
        var b64Inv = Base64DecodeInvocationRegex().Match(line.Content);
        if (b64Inv.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-B64-001",
                Title: "Base64 Decoding Invocation",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: b64Inv.Index + 1,
                Description: "Base64 decoding function invoked. May be used to unpack obfuscated binaries or scripts.",
                Snippet: ExtractSnippet(line.Content, b64Inv.Index, b64Inv.Length)));
        }

        // 2. Suspicious Base64 Literal (PAY-B64-001)
        foreach (Match b64Lit in Base64LiteralRegex().Matches(line.Content))
        {
            string val = b64Lit.Groups[1].Value;
            if (val.Length >= _options.MinSuspiciousBase64Length)
            {
                // Avoid flagging pure hex checksums/hashes as Base64
                if (IsHexOnly(val) && IsLikelyChecksumOrCommit(line.Content))
                {
                    continue;
                }

                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-B64-001",
                    Title: "Suspicious Base64 Literal",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.High,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: b64Lit.Index + 1,
                    Description: $"Large Base64-encoded literal ({val.Length} characters) detected. Potential encoded binary payload.",
                    Snippet: ExtractSnippet(line.Content, b64Lit.Index, Math.Min(60, b64Lit.Length))));
            }
        }

        // 3. Hex Decode Invocations (PAY-HEX-001)
        var hexInv = HexDecodeInvocationRegex().Match(line.Content);
        if (hexInv.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-HEX-001",
                Title: "Hex Decoding Invocation",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: hexInv.Index + 1,
                Description: "Hexadecimal decoding function invoked. May be used to reconstruct malicious payloads.",
                Snippet: ExtractSnippet(line.Content, hexInv.Index, hexInv.Length)));
        }

        // 4. Suspicious Hex Literals (PAY-HEX-001)
        foreach (Match hexLit in HexLiteralRegex().Matches(line.Content))
        {
            if (hexLit.Groups[1].Length >= _options.MinSuspiciousHexLength)
            {
                if (!IsLikelyChecksumOrCommit(line.Content))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "PAY-HEX-001",
                        Title: "Suspicious Hex Literal",
                        Category: DiffRiskCategory.SuspiciousPayload,
                        Severity: DiffRiskSeverity.High,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: hexLit.Index + 1,
                        Description: $"Large contiguous hex literal ({hexLit.Groups[1].Length} characters) detected.",
                        Snippet: ExtractSnippet(line.Content, hexLit.Index, Math.Min(60, hexLit.Length))));
                }
            }
        }

        // 5. Hex Escape Sequences \xNN (PAY-HEX-001)
        var hexEsc = HexEscapesRegex().Match(line.Content);
        if (hexEsc.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-HEX-001",
                Title: "Dense Hex Escape Sequence",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: hexEsc.Index + 1,
                Description: "Dense sequence of \\xNN hex escape bytes (>= 16 bytes) detected.",
                Snippet: ExtractSnippet(line.Content, hexEsc.Index, Math.Min(60, hexEsc.Length))));
        }

        // 6. Dense Hex Byte Arrays (PAY-HEX-001)
        var byteArr = DenseByteArrayRegex().Match(line.Content);
        if (byteArr.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-HEX-001",
                Title: "Dense Hex Byte Array",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: byteArr.Index + 1,
                Description: "Dense inline byte array of hex constants (>= 16 elements) detected.",
                Snippet: ExtractSnippet(line.Content, byteArr.Index, Math.Min(60, byteArr.Length))));
        }

        return findings;
    }

    private static bool IsHexOnly(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static bool IsLikelyChecksumOrCommit(string lineContent)
    {
        ReadOnlySpan<char> span = lineContent.AsSpan();
        return span.Contains("sha256", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("sha512", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("sha1", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("md5", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("checksum", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("hash", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
               span.Contains("integrity", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSnippet(string content, int matchIndex, int matchLength)
    {
        const int padding = 25;
        int start = Math.Max(0, matchIndex - padding);
        int end = Math.Min(content.Length, matchIndex + matchLength + padding);

        string prefix = start > 0 ? "..." : "";
        string suffix = end < content.Length ? "..." : "";

        return $"{prefix}{content[start..end].Replace("\r", " ").Replace("\n", " ").Trim()}{suffix}";
    }
}

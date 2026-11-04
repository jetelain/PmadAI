using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diff added lines for cloud/API token signatures, private keys, hardcoded credentials, and high Shannon entropy literals.
/// </summary>
public sealed partial class EntropyAndSecretScanner
{
    [GeneratedRegex(@"\b(?:AKIA[0-9A-Z]{16}|ghp_[a-zA-Z0-9]{36}|github_pat_[a-zA-Z0-9_]{82}|sk-[a-zA-Z0-9]{32,}|sk-proj-[a-zA-Z0-9_\-]{32,}|xox[baprs]-[0-9a-zA-Z]{10,48})\b", RegexOptions.NonBacktracking)]
    private static partial Regex ApiTokenRegex();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.NonBacktracking)]
    private static partial Regex PrivateKeyHeaderRegex();

    [GeneratedRegex(@"(?:\b|[""'])(?:api[_-]?key|secret[_-]?key|access[_-]?token|private[_-]?key)[""']?\s*[:=]\s*[""'][^""']{12,}[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"""([^""\\]*(?:\\.[^""\\]*)*)""|'([^'\\]*(?:\\.[^'\\]*)*)'|`([^`\\]*(?:\\.[^`\\]*)*)`", RegexOptions.NonBacktracking)]
    private static partial Regex QuotedLiteralRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.NonBacktracking)]
    private static partial Regex GuidRegex();

    private readonly DiffRiskScannerOptions _options;

    public EntropyAndSecretScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(IReadOnlyList<FileDiff> files)
    {
        if (!_options.EnableSecretAndEntropyRules || files.Count == 0)
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

        // 1. Known Cloud / API Tokens (PAY-SEC-001)
        var tokenMatch = ApiTokenRegex().Match(line.Content);
        if (tokenMatch.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-SEC-001",
                Title: "Cloud or API Token Signature",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.Critical,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: tokenMatch.Index + 1,
                Description: "Known cloud or API token signature (AWS, GitHub, OpenAI, or Slack) detected in code.",
                Snippet: ExtractSnippet(line.Content, tokenMatch.Index, tokenMatch.Length)));
        }

        // 2. Private Key Headers (PAY-SEC-002)
        var keyMatch = PrivateKeyHeaderRegex().Match(line.Content);
        if (keyMatch.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-SEC-002",
                Title: "Private Key Header",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: keyMatch.Index + 1,
                Description: "Cryptographic private key header block detected.",
                Snippet: ExtractSnippet(line.Content, keyMatch.Index, keyMatch.Length)));
        }

        // 3. Secret / Key Assignments (PAY-SEC-003)
        var assignMatch = SecretAssignmentRegex().Match(line.Content);
        if (assignMatch.Success)
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-SEC-003",
                Title: "Hardcoded Secret Assignment",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: assignMatch.Index + 1,
                Description: "Hardcoded secret, API key, access token, or private key assignment detected.",
                Snippet: ExtractSnippet(line.Content, assignMatch.Index, assignMatch.Length)));
        }

        // 4. Shannon Entropy Evaluation (PAY-ENT-001)
        var quoteMatches = QuotedLiteralRegex().Matches(line.Content);
        foreach (Match match in quoteMatches)
        {
            string literal = match.Value;
            if (literal.Length >= 2)
            {
                literal = literal[1..^1];
            }

            if (literal.Length < _options.EntropyMinStringLength)
            {
                continue;
            }

            // Exclude benign URLs, file paths, XML namespaces, and GUIDs
            if (IsBenignLiteral(literal))
            {
                continue;
            }

            double entropy = CalculateShannonEntropy(literal);
            if (entropy >= _options.ShannonEntropyThreshold)
            {
                string display = literal.Length > 60 ? $"{literal[..57]}..." : literal;
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-ENT-001",
                    Title: "High Shannon Entropy Literal",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.Medium,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: match.Index + 1,
                    Description: $"High Shannon entropy ({entropy:F2} bits/char >= {_options.ShannonEntropyThreshold:F2}) indicates a hardcoded secret, cryptographic key, or obfuscated payload.",
                    Snippet: $"\"{display}\""));
            }
        }

        return findings;
    }

    private static bool IsBenignLiteral(string text)
    {
        // URLs and URNs
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Standard GUIDs
        if (GuidRegex().IsMatch(text))
        {
            return true;
        }

        // Benign relative/absolute file paths (excluding Base64 strings starting with '/')
        if ((text.StartsWith("./") || text.StartsWith("../") ||
             (text.StartsWith('/') && !text.Contains('+') && !text.EndsWith('=') && text.IndexOf('/', 1) > 1) ||
             (text.Length > 3 && text[1] == ':' && (text[2] == '\\' || text[2] == '/'))))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes Shannon entropy in bits per character: H(X) = - SUM(P(x) * log2(P(x)))
    /// </summary>
    public static double CalculateShannonEntropy(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0.0;
        }

        Span<int> charCounts = stackalloc int[256];
        Dictionary<char, int>? highChars = null;
        int len = text.Length;

        for (int i = 0; i < len; i++)
        {
            char c = text[i];
            if (c < 256)
            {
                charCounts[c]++;
            }
            else
            {
                highChars ??= new Dictionary<char, int>();
                highChars[c] = highChars.GetValueOrDefault(c) + 1;
            }
        }

        double entropy = 0.0;
        for (int i = 0; i < 256; i++)
        {
            int count = charCounts[i];
            if (count > 0)
            {
                double p = (double)count / len;
                entropy -= p * Math.Log2(p);
            }
        }

        if (highChars != null)
        {
            foreach (var count in highChars.Values)
            {
                double p = (double)count / len;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
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


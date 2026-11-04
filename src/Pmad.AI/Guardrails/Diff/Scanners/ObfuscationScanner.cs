namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diff added lines for Unicode obfuscation (Trojan Source BiDi, zero-width chars, Cyrillic homoglyphs) and excessive line lengths.
/// </summary>
public sealed class ObfuscationScanner
{
    private static readonly Dictionary<char, string> DirectionalOverrides = new()
    {
        { '\u202A', "Left-to-Right Embedding (LRE)" },
        { '\u202B', "Right-to-Left Embedding (RLE)" },
        { '\u202C', "Pop Directional Formatting (PDF)" },
        { '\u202D', "Left-to-Right Override (LRO)" },
        { '\u202E', "Right-to-Left Override (RLO)" },
        { '\u2066', "Left-to-Right Isolate (LRI)" },
        { '\u2067', "Right-to-Left Isolate (RLI)" },
        { '\u2068', "First Strong Isolate (FSI)" },
        { '\u2069', "Pop Directional Isolate (PDI)" }
    };

    private static readonly Dictionary<char, string> ZeroWidthAndInvisible = new()
    {
        { '\u200B', "Zero-Width Space (ZWSP)" },
        { '\u200C', "Zero-Width Non-Joiner (ZWNJ)" },
        { '\u200D', "Zero-Width Joiner (ZWJ)" },
        { '\u2060', "Word Joiner (WJ)" },
        { '\u00AD', "Soft Hyphen (SHY)" },
        { '\u200E', "Left-to-Right Mark (LRM)" },
        { '\u200F', "Right-to-Left Mark (RLM)" },
        { '\u2028', "Line Separator (LS)" },
        { '\u2029', "Paragraph Separator (PS)" }
    };

    private static readonly Dictionary<char, char> CyrillicToLatinHomoglyphs = new()
    {
        // Lowercase
        { '\u0430', 'a' }, { '\u0441', 'c' }, { '\u0435', 'e' }, { '\u043E', 'o' },
        { '\u0440', 'p' }, { '\u0445', 'x' }, { '\u0443', 'y' }, { '\u0456', 'i' },
        { '\u0458', 'j' }, { '\u0455', 's' },
        // Uppercase
        { '\u0410', 'A' }, { '\u0412', 'B' }, { '\u0421', 'C' }, { '\u0415', 'E' },
        { '\u041D', 'H' }, { '\u0406', 'I' }, { '\u0408', 'J' }, { '\u041A', 'K' },
        { '\u041C', 'M' }, { '\u041E', 'O' }, { '\u0420', 'P' }, { '\u0422', 'T' },
        { '\u0425', 'X' }
    };

    private readonly DiffRiskScannerOptions _options;

    public ObfuscationScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(IReadOnlyList<FileDiff> files)
    {
        if (!_options.EnableUnicodeObfuscationRules || files.Count == 0)
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
        if (string.IsNullOrEmpty(line.Content))
        {
            return findings;
        }

        // 1. Line Length Anomaly (PAY-UNI-004)
        if (line.Content.Length > _options.MaxLineLength)
        {
            string preview = line.Content.Length > 80
                ? $"{line.Content[..80]}... [+{line.Content.Length - 80} more chars]"
                : line.Content;

            findings.Add(new DiffRiskFinding(
                RuleId: "PAY-UNI-004",
                Title: "Line Length Anomaly",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.Medium,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: _options.MaxLineLength + 1,
                Description: $"Added line length ({line.Content.Length:N0} characters) exceeds maximum threshold ({_options.MaxLineLength:N0} characters). Code may be pushed off-screen to bypass review.",
                Snippet: preview));
        }

        // 2. Unicode and Invisible Characters
        for (int col = 0; col < line.Content.Length; col++)
        {
            char c = line.Content[col];

            // BiDi Directional Overrides (CVE-2021-42574 Trojan Source)
            if (DirectionalOverrides.TryGetValue(c, out var bidiName))
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-UNI-001",
                    Title: "Unicode Directional Override (Trojan Source)",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.Critical,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: col + 1,
                    Description: $"Detected BiDi override character {bidiName} (U+{(int)c:X4}). May obscure or reorder code execution during review.",
                    Snippet: ExtractContextSnippet(line.Content, col)));
            }

            // Zero-Width & Invisible Characters (U+FEFF is ignored only if BOM at line 1, column 0)
            if (ZeroWidthAndInvisible.TryGetValue(c, out var invisibleName) || (c == '\uFEFF' && (col > 0 || line.LineNumber > 1)))
            {
                string name = invisibleName ?? "Zero-Width No-Break Space (ZWNBSP)";
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-UNI-002",
                    Title: "Invisible / Zero-Width Character",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.High,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: col + 1,
                    Description: $"Detected invisible character {name} (U+{(int)c:X4}). Used in obfuscation and hiding payloads.",
                    Snippet: ExtractContextSnippet(line.Content, col)));
            }

            // Cyrillic Confusable Homoglyphs (mixed with ASCII Latin characters in the same word/identifier)
            if (CyrillicToLatinHomoglyphs.TryGetValue(c, out char latinLookalike) &&
                IsMixedWithAsciiLatinInIdentifier(line.Content, col))
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-UNI-003",
                    Title: "Cyrillic Homoglyph Obfuscation",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.High,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: col + 1,
                    Description: $"Detected Cyrillic homoglyph '{c}' (U+{(int)c:X4}) confusable with Latin '{latinLookalike}'. Often used to deceive reviewers and spoof identifiers.",
                    Snippet: ExtractContextSnippet(line.Content, col)));
            }
        }

        return findings;
    }

    private static bool IsMixedWithAsciiLatinInIdentifier(string content, int index)
    {
        static bool IsIdentifierChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
        static bool IsAsciiLatinLetter(char ch) => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

        int start = index;
        while (start > 0 && IsIdentifierChar(content[start - 1]))
        {
            start--;
        }

        int end = index;
        while (end + 1 < content.Length && IsIdentifierChar(content[end + 1]))
        {
            end++;
        }

        for (int i = start; i <= end; i++)
        {
            if (IsAsciiLatinLetter(content[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractContextSnippet(string content, int charIndex)
    {
        const int padding = 20;
        int start = Math.Max(0, charIndex - padding);
        int end = Math.Min(content.Length, charIndex + padding + 1);

        string prefix = start > 0 ? "..." : "";
        string suffix = end < content.Length ? "..." : "";

        return $"{prefix}{content[start..end].Replace("\r", " ").Replace("\n", " ").Trim()}{suffix}";
    }
}


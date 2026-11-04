using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class ObfuscationScannerTests
{
    private readonly ObfuscationScanner _scanner = new();

    [Fact]
    public void ScanLine_ExcessiveLineLength_FlagsPayUni004()
    {
        string longCode = "var x = " + new string('a', 550) + ";";
        var line = new DiffAddedLine("obf.js", 1, longCode);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-004" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.Medium);
    }

    [Fact]
    public void ScanLine_NormalLineLength_DoesNotFlagAnomaly()
    {
        string normalCode = "var x = " + new string('a', 120) + ";";
        var line = new DiffAddedLine("normal.js", 1, normalCode);

        var findings = _scanner.ScanLine(line);

        Assert.DoesNotContain(findings, f => f.RuleId == "PAY-UNI-004");
    }

    [Fact]
    public void ScanLine_TrojanSourceBidiOverride_FlagsCriticalPayUni001()
    {
        // Trojan Source CVE-2021-42574: uses RLO (\u202E) to flip displayed code
        string bidiLine = "/* \u202E } \u2066 if (isAdmin) \u202E { */ return false;";
        var line = new DiffAddedLine("auth.c", 45, bidiLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-001" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.Critical);
    }

    [Fact]
    public void ScanLine_ZeroWidthSpace_FlagsPayUni002()
    {
        string zwspLine = "string command = \"rm\" + \"\u200B\" + \" -rf /\";";
        var line = new DiffAddedLine("stealth.py", 10, zwspLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-002" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void ScanLine_MidLineBom_FlagsPayUni002()
    {
        string bomLine = "var hidden = \uFEFF\"secret\";";
        var line = new DiffAddedLine("stealth.cs", 5, bomLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-002" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_Line2StartBom_FlagsPayUni002()
    {
        string bomLine = "\uFEFFint y = 2;";
        var line = new DiffAddedLine("stealth.cs", 2, bomLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-002" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_Line1StartBom_PermittedWithoutFinding()
    {
        string bomLine = "\uFEFFint y = 2;";
        var line = new DiffAddedLine("clean.cs", 1, bomLine);

        var findings = _scanner.ScanLine(line);

        Assert.Empty(findings);
    }

    [Fact]
    public void ScanLine_CyrillicHomoglyph_FlagsPayUni003()
    {
        // 'а' is Cyrillic \u0430 instead of Latin 'a'
        string homoglyphLine = "int p\u0430ssword = 1;";
        var line = new DiffAddedLine("spoof.cs", 25, homoglyphLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-003" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void ScanLine_PureCyrillicCommentOrString_DoesNotFlagHomoglyph()
    {
        string russianComment = "// Привет мир - локализованное сообщение";
        var line = new DiffAddedLine("localization.cs", 10, russianComment);

        var findings = _scanner.ScanLine(line);

        Assert.DoesNotContain(findings, f => f.RuleId == "PAY-UNI-003");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/ARCHITECTURE.markdown")]
    [InlineData("notes.txt")]
    [InlineData("assets/diagram.svg")]
    public void ScanLine_ExcessiveLineLengthInExcludedDocExtensions_DoesNotFlagPayUni004(string filePath)
    {
        string longDocLine = new string('a', 800);
        var line = new DiffAddedLine(filePath, 1, longDocLine);

        var findings = _scanner.ScanLine(line);

        Assert.DoesNotContain(findings, f => f.RuleId == "PAY-UNI-004");
    }

    [Fact]
    public void ScanLine_TrojanSourceInMarkdown_StillFlagsPayUni001()
    {
        string bidiLine = "<!-- \u202E reverse text \u2066 -->";
        var line = new DiffAddedLine("README.md", 1, bidiLine);

        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-UNI-001");
    }
}
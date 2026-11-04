using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class EncodedPayloadScannerTests
{
    private readonly EncodedPayloadScanner _scanner = new();

    [Theory]
    [InlineData("byte[] data = Convert.FromBase64String(rawPayload);")]
    [InlineData("const decoded = atob(encodedStr);")]
    [InlineData("raw = base64.b64decode(b64_string)")]
    [InlineData("const buf = Buffer.from(payload, 'base64');")]
    public void ScanLine_Base64Invocations_FlagsPayB64001(string lineContent)
    {
        var line = new DiffAddedLine("unpack.cs", 10, lineContent);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-B64-001" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void ScanLine_SuspiciousBase64Literal_FlagsPayB64001()
    {
        string base64Payload = new('A', 80);
        var line = new DiffAddedLine("blob.js", 5, $"const payload = \"{base64Payload}\";");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-B64-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Theory]
    [InlineData("byte[] bin = Convert.FromHexString(hexData);")]
    [InlineData("const buf = Buffer.from(hexStr, 'hex');")]
    [InlineData("raw = bytes.fromhex(hex_input)")]
    public void ScanLine_HexInvocations_FlagsPayHex001(string lineContent)
    {
        var line = new DiffAddedLine("unpack.cs", 20, lineContent);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-HEX-001" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void ScanLine_SuspiciousHexLiteral_FlagsPayHex001()
    {
        string hexPayload = new('a', 70);
        var line = new DiffAddedLine("data.cs", 1, $"string hex = \"{hexPayload}\";");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-HEX-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_Sha256Checksum_DoesNotFlagSuspiciousHex()
    {
        string sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var line = new DiffAddedLine("verify.cs", 15, $"string sha256_checksum = \"{sha256}\";");
        var findings = _scanner.ScanLine(line);

        Assert.Empty(findings);
    }

    [Fact]
    public void ScanLine_CustomLowerThresholdsAndMultipleLiteralsOnLine_Detected()
    {
        var customScanner = new EncodedPayloadScanner(new DiffRiskScannerOptions
        {
            MinSuspiciousBase64Length = 24,
            MinSuspiciousHexLength = 24
        });

        string shortLit = "short_benign_literal";
        string b64Payload = new('B', 30);
        var line = new DiffAddedLine("multi.cs", 5, $"var a = \"{shortLit}\"; var b = \"{b64Payload}\";");

        var findings = customScanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-B64-001");
    }

    [Fact]
    public void ScanLine_HexEscapes_FlagsPayHex001()
    {
        string escapes = "\\x4d\\x5a\\x90\\x00\\x03\\x00\\x00\\x00\\x04\\x00\\x00\\x00\\xff\\xff\\x00\\x00";
        var line = new DiffAddedLine("exploit.py", 12, $"shellcode = b\"{escapes}\"");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-HEX-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_DenseHexByteArray_FlagsPayHex001()
    {
        string arrayLine = "byte[] shell = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };";
        var line = new DiffAddedLine("loader.cs", 30, arrayLine);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-HEX-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_DenseHexCollectionExpression_FlagsPayHex001()
    {
        string collectionExpr = "ReadOnlySpan<byte> shell = [ 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 ];";
        var line = new DiffAddedLine("loader.cs", 35, collectionExpr);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-HEX-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_UrlSafeBase64Literal_FlagsPayB64001()
    {
        string urlSafeB64 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9_eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIn0-";
        var line = new DiffAddedLine("token.js", 1, $"const jwt = \"{urlSafeB64}\";");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-B64-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }
}


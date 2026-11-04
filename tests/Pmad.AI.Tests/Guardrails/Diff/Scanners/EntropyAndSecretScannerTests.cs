using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class EntropyAndSecretScannerTests
{
    private readonly EntropyAndSecretScanner _scanner = new();

    [Fact]
    public void CalculateShannonEntropy_MonotonousString_ReturnsZero()
    {
        double entropy = EntropyAndSecretScanner.CalculateShannonEntropy("AAAAAAAAAA");
        Assert.Equal(0.0, entropy, 4);
    }

    [Fact]
    public void CalculateShannonEntropy_HighEntropyBase64_ReturnsGreaterThanFourPointFive()
    {
        string base64Payload = "4aK9Z8vB1cM2xL3qR4wP5sT6yU7iO8eF0dG1hJ2kL3=";
        double entropy = EntropyAndSecretScanner.CalculateShannonEntropy(base64Payload);

        Assert.True(entropy >= 4.50, $"Expected entropy >= 4.5, got {entropy:F2}");
    }

    [Fact]
    public void ScanLine_DetectsHighEntropyStringLiteral()
    {
        var line = new DiffAddedLine("payload.js", 14, "const token = \"4aK9Z8vB1cM2xL3qR4wP5sT6yU7iO8eF0dG1hJ2kL3=\";");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-ENT-001" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.Medium);
    }

    [Fact]
    public void ScanLine_NormalEnglishString_DoesNotFlag()
    {
        var line = new DiffAddedLine("log.cs", 8, "Console.WriteLine(\"User authentication successfully completed.\");");
        var findings = _scanner.ScanLine(line);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("string awsKey = \"AKIAIOSFODNN7EXAMPLE\";")]
    [InlineData("const ghp = \"ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789\";")]
    [InlineData("var openai = \"sk-1234567890abcdef1234567890abcdef12345678\";")]
    [InlineData("string slack = \"xoxb-123456789012-1234567890123-4567890abcdef\";")]
    public void ScanLine_WellKnownCloudApiTokens_FlagsPaySec001(string lineContent)
    {
        var line = new DiffAddedLine("keys.cs", 1, lineContent);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-SEC-001" && f.Severity == DiffRiskSeverity.Critical && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Fact]
    public void ScanLine_PrivateKeyHeader_FlagsPaySec002()
    {
        var line = new DiffAddedLine("cert.pem", 1, "-----BEGIN RSA PRIVATE KEY-----");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-SEC-002" && f.Severity == DiffRiskSeverity.High && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Theory]
    [InlineData("const api_key = \"ab8934fa78c9120de9a8bc43f\";")]
    [InlineData("secret_key: \"my_super_secret_production_key_123\"")]
    [InlineData("{\"api_key\": \"my_super_secret_production_key_123\"}")]
    [InlineData("'secret_key': 'my_super_secret_production_key_123'")]
    public void ScanLine_SecretAssignment_FlagsPaySec003(string lineContent)
    {
        var line = new DiffAddedLine("settings.yaml", 5, lineContent);
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-SEC-003" && f.Severity == DiffRiskSeverity.High && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Theory]
    [InlineData("const guid = \"4a840e6c-7f51-40e1-b1e9-4e7cb8a4918e\";")]
    [InlineData("var url = \"https://github.com/organization/long-repository-name-with-segments\";")]
    [InlineData("var path = \"/usr/local/share/application/data/resources/cache.json\";")]
    [InlineData("var urn = \"urn:schemas-microsoft-com:xml-analysis:mddataset\";")]
    public void ScanLine_BenignStrings_AreExemptFromEntropy(string lineContent)
    {
        var line = new DiffAddedLine("info.cs", 3, lineContent);
        var findings = _scanner.ScanLine(line);

        Assert.DoesNotContain(findings, f => f.RuleId == "PAY-ENT-001");
    }

    [Fact]
    public void ScanLine_HighEntropyBase64StartingWithSlash_NotExemptedAsPath()
    {
        var line = new DiffAddedLine("secret.cs", 5, "string secret = \"/4aK9Z8vB1cM2xL3qR4wP5sT6yU7iO8eF0dG1hJ2kL3=\";");
        var findings = _scanner.ScanLine(line);

        Assert.Contains(findings, f => f.RuleId == "PAY-ENT-001");
    }
}


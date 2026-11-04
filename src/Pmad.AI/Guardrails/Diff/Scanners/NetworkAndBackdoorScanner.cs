using System.Net;
using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diffs for hardcoded public IP addresses, cloud metadata endpoints, non-allowlisted URLs,
/// tunneling/exfiltration services, backdoor auth checks, dangerous execution vectors, and build/CI tampering.
/// </summary>
public sealed partial class NetworkAndBackdoorScanner
{
    private static readonly HashSet<string> ExfiltrationDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "webhook.site",
        "ngrok.io",
        "ngrok-free.app",
        "pipedream.net",
        "requestbin.com",
        "burpcollaborator.net",
        "interact.sh",
        "oastify.com"
    };

    [GeneratedRegex(@"\b(?:https?|wss?):\/\/[^\s""'<>`]+", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", RegexOptions.NonBacktracking)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"""(\[?[0-9a-fA-F:.]+\]?)""|'(\[?[0-9a-fA-F:.]+\]?)'|`(\[?[0-9a-fA-F:.]+\]?)`", RegexOptions.NonBacktracking)]
    private static partial Regex QuotedIpCandidateRegex();

    [GeneratedRegex(@"\b(?:password|passwd|pwd|auth_token|secret_key|api_key)\s*={2,3}\s*[""'][^""']+[""']|[""'][^""']+[""']\s*={2,3}\s*\b(?:password|passwd|pwd|auth_token|secret_key|api_key)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex AuthBypassRegex();

    [GeneratedRegex(@"(?-i:\b(?:Process\.Start|ProcessStartInfo|Assembly\.(?:Load|LoadFrom|LoadFile|UnsafeLoadFrom)|child_process\.(?:exec|spawn|execSync|spawnSync)|subprocess\.(?:Popen|run|call|check_output)|os\.system|Runtime\.getRuntime\(\)\.exec)\b|\b(?:eval|exec)\s*\()|\b(?:powershell(?:\.exe)?\s+-(?:enc|encodedcommand|e|c|command)\b|cmd(?:\.exe)?\s+/[cC]\b|/(?:usr/)?bin/(?:ba)?sh\s+-c\b)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex RuntimeExecutionRegex();

    [GeneratedRegex(@"<(?:Exec|UsingTask)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex MsBuildTamperingRegex();

    // Prompt Injection Rules (PI001 - PI013)
    [GeneratedRegex(@"\b(?:ignore|disregard|forget|skip|override|bypass)\s+(?:all\s+)?(?:previous|prior|above|former|earlier|initial)\s+(?:instructions|directions|commands|rules|prompts|guidelines|context)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi001Regex();

    [GeneratedRegex(@"\b(?:disregard|ignore)\s+everything\s+(?:above|previously\s+stated|prior)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi002Regex();

    [GeneratedRegex(@"\b(?:new\s+instructions?|system\s+override|admin\s+override)\s*:\s*(?:ignore|you\s+are|act\s+as|forget)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi003Regex();

    [GeneratedRegex(@"\b(?:you\s+must\s+now|from\s+now\s+on\s+you\s+(?:must|will)|you\s+are\s+now)\s+(?:act\s+as|roleplay\s+as|operate\s+as|become|in\s+developer\s+mode|unrestricted|jailbroken)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi004Regex();

    [GeneratedRegex(@"\bact\s+as\s+(?:DAN|an\s+unrestricted|a\s+jailbroken|developer\s+mode)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi005Regex();

    [GeneratedRegex(@"<\|(?:im_start|im_end|endoftext|fim_prefix|fim_suffix)\|>", RegexOptions.NonBacktracking)]
    private static partial Regex Pi006Regex();

    [GeneratedRegex(@"(?:\[INST\]|\[/INST\]|<<SYS>>|<</SYS>>)", RegexOptions.NonBacktracking)]
    private static partial Regex Pi007Regex();

    [GeneratedRegex(@"(?:<turn_start>\s*(?:system|user|model)|<turn_end>)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi008Regex();

    [GeneratedRegex(@"(?:^|\n)\s*(?:System|Human|Assistant)\s*:\s*(?:ignore\s+all|disregard|you\s+are\s+now|override|new\s+instructions?)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi009Regex();

    [GeneratedRegex(@"\b(?:print|reveal|output|display|show|leak|echo|send|exfiltrate)\s+(?:your\s+)?(?:system\s+(?:prompt|instructions?)|initial\s+(?:prompt|instructions?)|secret\s+key|api\s+key|passwords?|credentials?|environment\s+variables?)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi010Regex();

    [GeneratedRegex(@"\b(?:IMPORTANT|CRITICAL|NOTE\s+TO|ATTENTION)\s*(?::\s*)?(?:AI|LLM|AGENT|ASSISTANT|CHATGPT|CLAUDE|GEMINI)\s*[:,]\s*(?:ignore|disregard|do not|you must|override|forget)\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi011Regex();

    [GeneratedRegex(@"<!--\s*(?:AI|LLM|AGENT|ASSISTANT)\s*(?:INSTRUCTION|PROMPT)\s*:", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi012Regex();

    [GeneratedRegex(@"(?:Output|Respond|Reply)\s+ONLY\s+(?:with\s+)?(?:\{?\s*[""']?result[""']?\s*:\s*[""']?OK[""']?|OK|clean)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Pi013Regex();

    private readonly DiffRiskScannerOptions _options;

    public NetworkAndBackdoorScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(IReadOnlyList<FileDiff> files)
    {
        var findings = new List<DiffRiskFinding>();

        foreach (var file in files)
        {
            // 1. Build / CI / Hook tampering at file level (BD-BUILD-001)
            if (_options.EnableBackdoorSignatureRules && !file.IsDeletedFile)
            {
                if (file.FilePath.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) ||
                    file.FilePath.StartsWith(".git/hooks/", StringComparison.OrdinalIgnoreCase) ||
                    file.FilePath.StartsWith(".husky/", StringComparison.OrdinalIgnoreCase) ||
                    file.NewFileMode == "160000" ||
                    file.OldFileMode == "160000" ||
                    file.FilePath.Equals(".gitmodules", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "BD-BUILD-001",
                        Title: "Build, CI, or Hook Tampering",
                        Category: DiffRiskCategory.BackdoorPattern,
                        Severity: DiffRiskSeverity.High,
                        FilePath: file.FilePath,
                        LineNumber: null,
                        Column: null,
                        Description: $"Build configuration, CI workflow, git hook, or git submodule modified: {file.FilePath}"));
                }
            }

            bool isMsBuildFile = file.FilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                 file.FilePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                                 file.FilePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                                 file.FilePath.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                                 file.FilePath.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

            foreach (var line in file.AddedLines)
            {
                findings.AddRange(ScanLine(line, isMsBuildFile));
            }
        }

        return findings;
    }

    public List<DiffRiskFinding> ScanLine(DiffAddedLine line, bool isMsBuildFile = false)
    {
        var findings = new List<DiffRiskFinding>();
        if (string.IsNullOrWhiteSpace(line.Content))
        {
            return findings;
        }

        // 1. Backdoor & Dangerous Execution Rules
        if (_options.EnableBackdoorSignatureRules)
        {
            // MSBuild Execution Tampering
            if (isMsBuildFile)
            {
                var msbMatch = MsBuildTamperingRegex().Match(line.Content);
                if (msbMatch.Success)
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "BD-BUILD-001",
                        Title: "MSBuild Execution Tampering",
                        Category: DiffRiskCategory.BackdoorPattern,
                        Severity: DiffRiskSeverity.High,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: msbMatch.Index + 1,
                        Description: "Added <Exec> or <UsingTask> element in MSBuild file allows arbitrary process execution during build.",
                        Snippet: ExtractSnippet(line.Content, msbMatch.Index, msbMatch.Length)));
                }
            }

            // Auth Bypass Checks (BD-AUTH-001)
            var authMatch = AuthBypassRegex().Match(line.Content);
            if (authMatch.Success)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "BD-AUTH-001",
                    Title: "Hardcoded Credential or Authentication Bypass",
                    Category: DiffRiskCategory.BackdoorPattern,
                    Severity: DiffRiskSeverity.High,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: authMatch.Index + 1,
                    Description: "Potential authentication bypass with hardcoded password or credential equality comparison.",
                    Snippet: ExtractSnippet(line.Content, authMatch.Index, authMatch.Length)));
            }

            // Runtime Code/Process Execution (BD-EXEC-001)
            var execMatch = RuntimeExecutionRegex().Match(line.Content);
            if (execMatch.Success)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "BD-EXEC-001",
                    Title: "Dangerous Runtime Execution Vector",
                    Category: DiffRiskCategory.BackdoorPattern,
                    Severity: DiffRiskSeverity.High,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: execMatch.Index + 1,
                    Description: "Dynamic code evaluation, process execution, or assembly loading detected.",
                    Snippet: ExtractSnippet(line.Content, execMatch.Index, execMatch.Length)));
            }
        }

        // 2. Network Endpoint Rules
        if (_options.EnableNetworkEndpointRules)
        {
            var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // URLs
            var urlMatches = UrlRegex().Matches(line.Content);
            foreach (Match urlMatch in urlMatches)
            {
                string rawUrl = urlMatch.Value.TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                // Host-confusion URL (NET-URL-001)
                bool hasUserInfo = !string.IsNullOrEmpty(uri.UserInfo);
                if (hasUserInfo)
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "NET-URL-001",
                        Title: "Host Confusion URL with UserInfo",
                        Category: DiffRiskCategory.HardcodedNetworkEndpoint,
                        Severity: DiffRiskSeverity.High,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: urlMatch.Index + 1,
                        Description: $"URL '{rawUrl}' contains userinfo in authority, potential host confusion attack.",
                        Snippet: rawUrl));
                }

                // Exfiltration / Tunneling endpoint (NET-URL-002)
                if (IsExfiltrationHost(uri.IdnHost))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "NET-URL-002",
                        Title: "Exfiltration or Tunneling Endpoint",
                        Category: DiffRiskCategory.HardcodedNetworkEndpoint,
                        Severity: DiffRiskSeverity.Critical,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: urlMatch.Index + 1,
                        Description: $"Known exfiltration or tunneling domain detected: {uri.IdnHost}",
                        Snippet: rawUrl));
                    continue;
                }

                // If host is an IP address, evaluate via IP rules
                if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
                {
                    string hostStr = uri.DnsSafeHost.Trim('[', ']');
                    if (IPAddress.TryParse(hostStr, out var ipFromUrl))
                    {
                        var normIp = ipFromUrl.IsIPv4MappedToIPv6 ? ipFromUrl.MapToIPv4() : ipFromUrl;
                        if (seenIps.Add(normIp.ToString()))
                        {
                            seenIps.Add(hostStr);
                            EvaluateIpAddress(normIp, line, urlMatch.Index + 1, rawUrl, findings);
                        }
                    }
                    continue;
                }

                // Non-allowlisted domain check (NET-URL-001)
                if (!hasUserInfo && !IsSafeDomain(uri.IdnHost, _options.WellKnownSafeDomains))
                {
                    if (_options.EnableTestTolerance && _options.IsTestFile(line.FilePath))
                    {
                        if (!uri.IdnHost.Contains('.') ||
                            uri.IdnHost.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                            uri.IdnHost.Contains("mock", StringComparison.OrdinalIgnoreCase) ||
                            uri.IdnHost.Contains("fake", StringComparison.OrdinalIgnoreCase) ||
                            uri.IdnHost.Contains("dummy", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    findings.Add(new DiffRiskFinding(
                        RuleId: "NET-URL-001",
                        Title: "Non-Allowlisted URL",
                        Category: DiffRiskCategory.HardcodedNetworkEndpoint,
                        Severity: DiffRiskSeverity.High,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: urlMatch.Index + 1,
                        Description: $"URL '{rawUrl}' targets non-allowlisted domain '{uri.IdnHost}'.",
                        Snippet: rawUrl));
                }
            }

            // Standalone / Quoted IP addresses on added line
            // First check quoted IPv6 matches (including IPv4-mapped IPv6 like ::ffff:169.254.169.254)
            var quotedMatches = QuotedIpCandidateRegex().Matches(line.Content);
            foreach (Match m in quotedMatches)
            {
                string rawCandidate = m.Groups[1].Success
                    ? m.Groups[1].Value
                    : (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
                string candidate = rawCandidate.Trim('[', ']');
                if (candidate.Contains(':') && IPAddress.TryParse(candidate, out var ip6))
                {
                    var normIp = ip6.IsIPv4MappedToIPv6 ? ip6.MapToIPv4() : ip6;
                    if (seenIps.Add(normIp.ToString()))
                    {
                        seenIps.Add(candidate);
                        EvaluateIpAddress(normIp, line, m.Index + 1, candidate, findings);
                    }
                }
            }

            // Check IPv4 matches
            var ipv4Matches = Ipv4Regex().Matches(line.Content);
            foreach (Match m in ipv4Matches)
            {
                string ipStr = m.Value;
                if (seenIps.Contains(ipStr)) continue;

                if (IPAddress.TryParse(ipStr, out var ip))
                {
                    var normIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
                    // Skip 4-part version numbers (e.g. Version="1.2.3.4"), except cloud metadata IP
                    if (!normIp.Equals(MetadataIp) && IsPrecededByVersionKeyword(line.Content, m.Index))
                    {
                        continue;
                    }

                    if (seenIps.Add(normIp.ToString()))
                    {
                        EvaluateIpAddress(normIp, line, m.Index + 1, ipStr, findings);
                    }
                }
            }
        }

        // 3. Optional Prompt Injection Rules
        if (_options.EnablePromptInjectionRules)
        {
            CheckPromptInjection(line, findings);
        }

        return findings;
    }

    private static readonly IPAddress MetadataIp = IPAddress.Parse("169.254.169.254");

    private void EvaluateIpAddress(
        IPAddress ip,
        DiffAddedLine line,
        int column,
        string snippet,
        List<DiffRiskFinding> findings)
    {
        // Must unwrap IPv4-mapped IPv6 (e.g. ::ffff:169.254.169.254 -> 169.254.169.254)
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        // Check for cloud metadata IP (169.254.169.254)
        if (ip.Equals(MetadataIp))
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "NET-IP-002",
                Title: "Cloud Metadata IP Detected",
                Category: DiffRiskCategory.HardcodedNetworkEndpoint,
                Severity: DiffRiskSeverity.Critical,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: column,
                Description: "Hardcoded cloud metadata IP 169.254.169.254 detected. Potential SSRF or cloud credential exfiltration.",
                Snippet: snippet));
            return;
        }

        // Loopback / unspecified / link-local
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
        {
            return;
        }

        byte[] bytes = ip.GetAddressBytes();

        // IPv4 link-local (169.254.0.0/16)
        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254)
        {
            return;
        }

        // IPv6 link-local (fe80::/10)
        if (ip.IsIPv6LinkLocal)
        {
            return;
        }

        // RFC 5737 Documentation blocks (192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24)
        if (bytes.Length == 4)
        {
            if ((bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
                (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113))
            {
                return;
            }
        }
        else if (bytes.Length == 16)
        {
            // IPv6 documentation block (2001:db8::/32)
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            {
                return;
            }
        }

        // Private network ranges (RFC 1918 / RFC 4193)
        bool isPrivate = IsPrivateIp(bytes);
        if (isPrivate)
        {
            if (_options.FlagPrivateNetworkIps)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "NET-IP-001",
                    Title: "Private Network IP Address Detected",
                    Category: DiffRiskCategory.HardcodedNetworkEndpoint,
                    Severity: DiffRiskSeverity.Medium,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: column,
                    Description: $"Private network IP address {ip} detected.",
                    Snippet: snippet));
            }
            return;
        }

        // Check AllowedIpAddresses allowlist
        if (IsAllowedIp(ip, _options.AllowedIpAddresses))
        {
            return;
        }

        // Public IP address (NET-IP-001)
        findings.Add(new DiffRiskFinding(
            RuleId: "NET-IP-001",
            Title: "Public IP Address Detected",
            Category: DiffRiskCategory.HardcodedNetworkEndpoint,
            Severity: DiffRiskSeverity.High,
            FilePath: line.FilePath,
            LineNumber: line.LineNumber,
            Column: column,
            Description: $"Hardcoded public IP address {ip} detected.",
            Snippet: snippet));
    }

    private static bool IsPrivateIp(byte[] bytes)
    {
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        else if (bytes.Length == 16)
        {
            // Unique Local Address fc00::/7 (first byte 0xFC or 0xFD)
            if ((bytes[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }

    private static bool IsAllowedIp(IPAddress ip, HashSet<string> allowedIps)
    {
        string ipStr = ip.ToString();
        if (allowedIps.Contains(ipStr))
        {
            return true;
        }

        foreach (var allowed in allowedIps)
        {
            int slashIndex = allowed.IndexOf('/');
            if (slashIndex > 0)
            {
                string prefixStr = allowed[..slashIndex];
                if (IPAddress.TryParse(prefixStr, out var prefixIp) &&
                    int.TryParse(allowed[(slashIndex + 1)..], out int prefixLength))
                {
                    if (IsInCidr(ip, prefixIp, prefixLength))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsInCidr(IPAddress ip, IPAddress prefixIp, int prefixLength)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }
        if (prefixIp.IsIPv4MappedToIPv6)
        {
            prefixIp = prefixIp.MapToIPv4();
        }

        if (ip.AddressFamily != prefixIp.AddressFamily)
        {
            return false;
        }

        byte[] ipBytes = ip.GetAddressBytes();
        byte[] prefixBytes = prefixIp.GetAddressBytes();

        int maxBits = ipBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > maxBits)
        {
            return false;
        }

        int fullBytes = prefixLength / 8;
        int remBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != prefixBytes[i]) return false;
        }

        if (remBits > 0 && fullBytes < ipBytes.Length)
        {
            byte mask = (byte)(0xFF << (8 - remBits));
            if ((ipBytes[fullBytes] & mask) != (prefixBytes[fullBytes] & mask)) return false;
        }

        return true;
    }

    private static bool IsExfiltrationHost(string host)
    {
        if (ExfiltrationDomains.Contains(host))
        {
            return true;
        }

        foreach (var domain in ExfiltrationDomains)
        {
            if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeDomain(string host, HashSet<string> safeDomains)
    {
        if (safeDomains.Contains(host))
        {
            return true;
        }

        foreach (var pattern in safeDomains)
        {
            if (pattern.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = pattern[2..];
                if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPrecededByVersionKeyword(string line, int matchIndex)
    {
        if (matchIndex <= 0)
        {
            return false;
        }

        // Walk backward past delimiters such as quotes, '=', ':', '(', '>', whitespace, or 'v'/'V'
        int i = matchIndex - 1;
        if (i >= 0 && line[i] is 'v' or 'V')
        {
            i--;
        }

        bool sawDelimiter = false;
        while (i >= 0 && (char.IsWhiteSpace(line[i]) || line[i] is '=' or ':' or '(' or '<' or '>' or '"' or '\''))
        {
            if (line[i] is '=' or ':' or '(' or '>' or '"' or '\'')
            {
                sawDelimiter = true;
            }
            i--;
        }

        if (!sawDelimiter || i < 0)
        {
            return false;
        }

        int end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '-' or '.'))
        {
            i--;
        }

        ReadOnlySpan<char> token = line.AsSpan(i + 1, end - (i + 1));
        return token.Contains("version", StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckPromptInjection(DiffAddedLine line, List<DiffRiskFinding> findings)
    {
        void Check(Regex regex, string ruleId, string title, string desc, DiffRiskSeverity sev)
        {
            var match = regex.Match(line.Content);
            if (match.Success)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: ruleId,
                    Title: title,
                    Category: DiffRiskCategory.PromptInjection,
                    Severity: sev,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: match.Index + 1,
                    Description: desc,
                    Snippet: ExtractSnippet(line.Content, match.Index, match.Length)));
            }
        }

        Check(Pi001Regex(), "PI001", "Directive Override", "Attempts to override prior instructions", DiffRiskSeverity.High);
        Check(Pi002Regex(), "PI002", "Disregard Context", "Attempts to clear prior context", DiffRiskSeverity.High);
        Check(Pi003Regex(), "PI003", "System Override Directive", "Attempts system override directive", DiffRiskSeverity.High);
        Check(Pi004Regex(), "PI004", "Persona Hijack", "Attempts developer mode or jailbroken persona", DiffRiskSeverity.High);
        Check(Pi005Regex(), "PI005", "DAN Jailbreak", "Classic DAN jailbreak pattern", DiffRiskSeverity.High);
        Check(Pi006Regex(), "PI006", "ChatML Control Token", "ChatML special tokens injected", DiffRiskSeverity.Critical);
        Check(Pi007Regex(), "PI007", "Llama Instruction Delimiter", "Llama instruction delimiters injected", DiffRiskSeverity.Critical);
        Check(Pi008Regex(), "PI008", "Turn Delimiter Injection", "Turn delimiter injected", DiffRiskSeverity.Critical);
        Check(Pi009Regex(), "PI009", "Role Turn Injection", "Conversation roles injected", DiffRiskSeverity.High);
        Check(Pi010Regex(), "PI010", "Secret Exfiltration Directive", "Directives to leak system prompts or keys", DiffRiskSeverity.High);
        Check(Pi011Regex(), "PI011", "Targeted Agent Directive", "Instructions targeted at AI model", DiffRiskSeverity.High);
        Check(Pi012Regex(), "PI012", "Hidden Comment Injection", "HTML/markdown comments directing AI assistant", DiffRiskSeverity.High);
        Check(Pi013Regex(), "PI013", "Output Format Coercion", "Attempts to coerce AI judge into reporting OK", DiffRiskSeverity.High);
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


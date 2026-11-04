namespace Pmad.AI.DiffGuard.Cli.Formatting.Sarif;

public static class SarifRules
{
    private static SarifReportingDescriptor CreateRule(
        string id,
        string name,
        string shortDescription,
        string defaultLevel = "warning",
        string? fullDescription = null)
    {
        return new SarifReportingDescriptor
        {
            Id = id,
            Name = name,
            ShortDescription = new SarifMultiformatMessageString
            {
                Text = shortDescription
            },
            FullDescription = fullDescription != null
                ? new SarifMultiformatMessageString { Text = fullDescription }
                : null,
            DefaultConfiguration = new SarifReportingConfiguration
            {
                Level = defaultLevel
            }
        };
    }

    public static readonly IReadOnlyList<SarifReportingDescriptor> AllRules =
    [
        // Network
        CreateRule("NET-IP-001", "HardcodedPublicIp", "Hardcoded public or non-allowlisted IP address literal detected.", "error"),
        CreateRule("NET-IP-002", "CloudMetadataIp", "Hardcoded cloud instance metadata service IP (169.254.169.254) detected.", "error"),
        CreateRule("NET-URL-001", "NonAllowlistedUrl", "Hardcoded URL targeting a non-allowlisted domain or containing userinfo host confusion.", "error"),
        CreateRule("NET-URL-002", "ExfiltrationEndpointUrl", "Hardcoded URL targeting a known data exfiltration or tunneling service.", "error"),

        // Backdoor & Tampering
        CreateRule("BD-AUTH-001", "HardcodedAuthBypass", "Potential authentication bypass using hardcoded credential comparison.", "error"),
        CreateRule("BD-EXEC-001", "DynamicCodeExecution", "Dynamic code evaluation, process execution, or runtime assembly loading detected.", "error"),
        CreateRule("BD-BUILD-001", "BuildOrHookTampering", "MSBuild task/exec injection, CI workflow modification, Git hook, or submodule tampering.", "error"),

        // Secrets & Entropy
        CreateRule("PAY-SEC-001", "HardcodedApiToken", "Known cloud or API secret token signature detected.", "error"),
        CreateRule("PAY-SEC-002", "PrivateKeyMaterial", "Private cryptographic key header detected in added code.", "error"),
        CreateRule("PAY-SEC-003", "HardcodedSecretAssignment", "Hardcoded secret, password, or API key variable assignment detected.", "error"),
        CreateRule("PAY-ENT-001", "HighEntropyLiteral", "High Shannon entropy string literal detected (potential obfuscated secret or payload).", "warning"),

        // Encoded & Obfuscated Payloads
        CreateRule("PAY-B64-001", "SuspiciousBase64Payload", "Base64 decoding call or long suspicious Base64 payload literal detected.", "warning"),
        CreateRule("PAY-HEX-001", "SuspiciousHexPayload", "Hex decoding call, long hex payload literal, or dense hex byte array detected.", "warning"),
        CreateRule("PAY-UNI-001", "TrojanSourceBidiOverride", "Trojan Source Unicode bidirectional override character (CVE-2021-42574) detected.", "error"),
        CreateRule("PAY-UNI-002", "ZeroWidthObfuscation", "Invisible or zero-width Unicode character detected in added code.", "error"),
        CreateRule("PAY-UNI-003", "HomoglyphIdentifier", "Confusable mixed-script Cyrillic/Latin homoglyph identifier detected.", "error"),
        CreateRule("PAY-UNI-004", "ExcessiveLineLength", "Added line exceeds maximum permitted character length (potential horizontal obfuscation).", "warning"),

        // Binary Files
        CreateRule("PAY-BIN-001", "SuspiciousBinaryFile", "Unverified or executable binary file added or modified in diff.", "error"),
        CreateRule("PAY-BIN-002", "InvalidOrDisguisedBinary", "Binary file failed safe magic-byte validation, disguised executable header, or path traversal.", "error"),

        // Supply Chain & Dependencies
        CreateRule("DEP-NEW-001", "UnverifiedNewDependency", "Newly added external package dependency not matching safe package allowlist.", "error"),
        CreateRule("DEP-DOWN-001", "DependencyVersionDowngrade", "Existing package dependency downgraded to an older semantic version.", "error"),
        CreateRule("DEP-SRC-001", "UntrustedPackageSource", "Untrusted package registry or feed source added to configuration.", "error"),

        // Blast Radius
        CreateRule("SCOPE-FILES-001", "ExcessiveChangedFiles", "Number of changed files in diff exceeds maximum threshold.", "note"),
        CreateRule("SCOPE-LINES-001", "ExcessiveChangedLines", "Total changed lines in diff exceeds maximum threshold.", "note"),
        CreateRule("SCOPE-PCT-001", "ExcessiveCodebaseImpactPercent", "Percentage of repository lines modified exceeds maximum threshold.", "note"),
        CreateRule("SCOPE-FILEPCT-001", "ExcessiveFileImpactPercent", "Percentage of repository files modified exceeds maximum threshold.", "note"),

        // Prompt Injection
        CreateRule("PI001", "DirectiveOverride", "Directive Override: Attempts to override prior instructions.", "error"),
        CreateRule("PI002", "DisregardContext", "Disregard Context: Attempts to clear prior context.", "error"),
        CreateRule("PI003", "SystemOverrideDirective", "System Override Directive: Attempts system override directive.", "error"),
        CreateRule("PI004", "PersonaHijack", "Persona Hijack: Attempts developer mode or jailbroken persona.", "error"),
        CreateRule("PI005", "DanJailbreak", "DAN Jailbreak: Classic DAN jailbreak pattern.", "error"),
        CreateRule("PI006", "ChatMlControlToken", "ChatML Control Token: ChatML special tokens injected.", "error"),
        CreateRule("PI007", "LlamaInstructionDelimiter", "Llama Instruction Delimiter: Llama instruction delimiters injected.", "error"),
        CreateRule("PI008", "TurnDelimiterInjection", "Turn Delimiter Injection: Turn delimiter injected.", "error"),
        CreateRule("PI009", "RoleTurnInjection", "Role Turn Injection: Conversation roles injected.", "error"),
        CreateRule("PI010", "SecretExfiltrationDirective", "Secret Exfiltration Directive: Directives to leak system prompts or keys.", "error"),
        CreateRule("PI011", "TargetedAgentDirective", "Targeted Agent Directive: Instructions targeted at AI model.", "error"),
        CreateRule("PI012", "HiddenCommentInjection", "Hidden Comment Injection: HTML/markdown comments directing AI assistant.", "error"),
        CreateRule("PI013", "OutputFormatCoercion", "Output Format Coercion: Attempts to coerce AI judge into reporting OK.", "error"),

        // LLM Judge
        CreateRule("LLM-JUDGE-001", "LlmJudgeSecurityFinding", "Security vulnerability, backdoor, logic bomb, or injection flagged by LLM security judge.", "error"),
        CreateRule("LLM-JUDGE-ERROR", "LlmJudgeEvaluationError", "LLM security judge evaluation failed or returned an invalid response (fail-closed policy).", "error")
    ];

    private static readonly Dictionary<string, SarifReportingDescriptor> RuleLookup =
        AllRules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    public static SarifReportingDescriptor? FindRule(string ruleId)
    {
        return RuleLookup.GetValueOrDefault(ruleId);
    }
}


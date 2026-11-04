namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Category of risk detected within a git diff.
/// </summary>
public enum DiffRiskCategory
{
    /// <summary>
    /// Authentication bypasses, logic bombs, dynamic execution, MSBuild/CI/hook tampering, or LLM Judge findings.
    /// </summary>
    BackdoorPattern,

    /// <summary>
    /// Hardcoded public IPv4/IPv6 literals, non-allowlisted URLs, or exfiltration/tunneling domains.
    /// </summary>
    HardcodedNetworkEndpoint,

    /// <summary>
    /// Hardcoded secrets/keys, high Shannon entropy literals, Base64/Hex payloads, Trojan Source, or unsafe binaries.
    /// </summary>
    SuspiciousPayload,

    /// <summary>
    /// Newly added external package dependency or untrusted package feed not matching allowlists.
    /// </summary>
    UnverifiedDependency,

    /// <summary>
    /// Existing package dependency modified to a lower semantic version (V_new &lt; V_old).
    /// </summary>
    DependencyDowngrade,

    /// <summary>
    /// Diff exceeds relative codebase impact (&gt; N%), affected file count (&gt; M), or changed line count (&gt; X).
    /// </summary>
    ExcessiveBlastRadius,

    /// <summary>
    /// Directives attempting prompt injection or LLM behavioral override (active when EnablePromptInjectionRules = true).
    /// </summary>
    PromptInjection
}


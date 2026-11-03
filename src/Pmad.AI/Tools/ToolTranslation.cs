namespace Pmad.AI.Tools;

/// <summary>
/// Shell quoting and escaping method applied to translated tool parameters.
/// </summary>
public enum ParameterEscapeMethod
{
    /// <summary>Raw string substitution without quoting.</summary>
    None,

    /// <summary>POSIX single-quoted string substitution escaping internal single quotes as '\''.</summary>
    Bash,

    /// <summary>PowerShell single-quoted string substitution doubling internal single quotes as ''.</summary>
    PowerShell
}

/// <summary>
/// Rule translating a virtual tool invocation into a shell command executed by a target tool (e.g. bash).
/// </summary>
public class ToolTranslation
{
    /// <summary>Virtual tool name invoked by the model (e.g. "grep_search").</summary>
    public required string SourceTool { get; init; }

    /// <summary>Target executable tool (e.g. "bash").</summary>
    public required string TargetTool { get; init; }

    /// <summary>Command template with {name} or {name:default} placeholders.</summary>
    public required string CommandTemplate { get; init; }

    /// <summary>Escaping method for substituted values.</summary>
    public ParameterEscapeMethod EscapeMethod { get; init; } = ParameterEscapeMethod.Bash;
}

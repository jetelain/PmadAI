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

    /// <summary>Alternative names by which the virtual tool can be invoked.</summary>
    public HashSet<string> Aliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parameter aliases mapping candidate parameter names to placeholder names.</summary>
    public Dictionary<string, string> ParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a deep copy of this translation rule so that mutations do not affect snapshots.
    /// </summary>
    public ToolTranslation Clone()
    {
        return new ToolTranslation
        {
            SourceTool = SourceTool,
            TargetTool = TargetTool,
            CommandTemplate = CommandTemplate,
            EscapeMethod = EscapeMethod,
            Aliases = new HashSet<string>(Aliases, StringComparer.OrdinalIgnoreCase),
            ParameterAliases = new Dictionary<string, string>(ParameterAliases, StringComparer.OrdinalIgnoreCase)
        };
    }
}

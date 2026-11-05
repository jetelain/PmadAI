namespace Pmad.AI.Tools;

/// <summary>
/// Declarative rule that computes a count/limit parameter from a start parameter and end boundary parameter aliases.
/// </summary>
public sealed class RangeToLimitRule
{
    /// <summary>Name of the start parameter (e.g. "offset").</summary>
    public required string StartParameter { get; init; }

    /// <summary>Name of the limit/count parameter to produce (e.g. "limit").</summary>
    public required string CountParameter { get; init; }

    /// <summary>Set of candidate parameter aliases representing the end boundary (e.g. ["end_line", "endLine", "line_end", "to_line", "to", "end"]).</summary>
    public HashSet<string> EndParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default start offset when the start parameter is omitted or cannot be parsed (default: 1).</summary>
    public int DefaultStart { get; init; } = 1;

    /// <summary>Whether the range is inclusive (end - start + 1) or exclusive (end - start). Default: true.</summary>
    public bool Inclusive { get; init; } = true;
}


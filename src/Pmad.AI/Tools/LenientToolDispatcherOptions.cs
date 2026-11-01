using Pmad.AI.Guardrails;

namespace Pmad.AI.Tools;

/// <summary>
/// Configuration options for <see cref="LenientToolDispatcher"/>.
/// </summary>
public class LenientToolDispatcherOptions
{
    /// <summary>Whether to strip namespace prefixes like "workspace__" or "default:". Default: true.</summary>
    public bool StripNamespacePrefixes { get; set; } = true;

    /// <summary>Whether to validate rendered shell translations with <see cref="ReadOnlyCommandValidator"/>. Default: true.</summary>
    public bool ValidateTranslationsWithReadOnlyValidator { get; set; } = true;

    /// <summary>Validator for translated shell commands. If null, a default validator is used.</summary>
    public ReadOnlyCommandValidator? CommandValidator { get; set; }

    /// <summary>
    /// Whether to enable lenient parameter coercion (string-to-number, comma-delimited string to array,
    /// case-insensitive enum and boolean matching). Default: true.
    /// </summary>
    public bool EnableParameterCoercion { get; set; } = true;

    /// <summary>
    /// Whether to automatically synthesize collection/array parameters when the model passes flattened root-level
    /// properties, or unpack 1-element arrays into flat parameters when the tool expects scalar parameters.
    /// Also handles blank-chunk fallback (e.g. [{}]). Default: true.
    /// </summary>
    public bool AutoPromoteFlattenedArrayParameters { get; set; } = true;

    /// <summary>
    /// Whether to automatically propagate unmapped root-level parameters into nested collection items
    /// when the item schema defines a matching property. Default: true.
    /// </summary>
    public bool AutoPropagateCollectionParameters { get; set; } = true;

    /// <summary>
    /// Backward-compatible alias for <see cref="AutoPropagateCollectionParameters"/>.
    /// </summary>
    public bool PropagateReplaceAllToCollections
    {
        get => AutoPropagateCollectionParameters;
        set => AutoPropagateCollectionParameters = value;
    }

    /// <summary>
    /// Global parameter names that should be propagated to nested collection items if present at root.
    /// </summary>
    public HashSet<string> PropagatedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configurable fallback property names when determining the primary string property of an object schema.
    /// Used when the schema does not define a single required or unique string property.
    /// Default: "prompt", "question", "text", "query", "content", "message", "title", "name", "value", "command".
    /// </summary>
    public IList<string> DefaultPrimaryStringProperties { get; set; } = new List<string>
    {
        "prompt", "question", "text", "query", "content", "message", "title", "name", "value", "command"
    };

    /// <summary>
    /// Optional telemetry collector to record lenient dispatch remappings and alias hits.
    /// </summary>
    public LenientDispatchTelemetry? Telemetry { get; set; }

    /// <summary>
    /// Optional session identifier propagated to telemetry events.
    /// </summary>
    public string? SessionId { get; set; }
}

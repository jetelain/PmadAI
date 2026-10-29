using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;

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
/// Delegate that computes or resolves a parameter value from the dictionary of incoming arguments.
/// </summary>
public delegate object? ParameterComputer(IDictionary<string, object?> rawArguments);

/// <summary>
/// Alias and parameter mapping configuration for a single canonical tool.
/// </summary>
public class ToolMapping
{
    /// <summary>Alternative names by which the tool can be invoked.</summary>
    public HashSet<string> Aliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parameter aliases mapping canonical parameter names to acceptable alternative names.</summary>
    public Dictionary<string, HashSet<string>> ParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computed parameter factories evaluated when canonical parameters are missing.
    /// Allows multi-parameter synthesis (e.g. limit = end_line - start_line + 1).
    /// </summary>
    public Dictionary<string, ParameterComputer> ComputedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit nested property alias mappings (e.g. "edits[]" -> { "oldText": ["old_text", "search"] }).
    /// </summary>
    public Dictionary<string, Dictionary<string, HashSet<string>>> NestedParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit parameter value aliases mapping parameter paths to canonical values and acceptable alternative synonyms/codes
    /// (e.g. "species" -> { "wild tiger": ["wild tigers", "tiger", "tigers"] } or "resource_type" -> { "MemoryMb": ["memory", "ram"] }).
    /// </summary>
    public Dictionary<string, Dictionary<string, HashSet<string>>> ParameterValueAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit list of root-level parameters that should be propagated to nested collection items.
    /// </summary>
    public HashSet<string> PropagatedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit mappings of collection parameter paths (e.g. "questions[]" or "questions")
    /// to the primary string property name used when wrapping raw strings into objects.
    /// </summary>
    public Dictionary<string, string> PrimaryCollectionProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ToolMapping() { }

    public ToolMapping(
        IEnumerable<string>? aliases = null,
        IDictionary<string, IEnumerable<string>>? parameterAliases = null,
        IDictionary<string, ParameterComputer>? computedParameters = null,
        IDictionary<string, IDictionary<string, IEnumerable<string>>>? nestedParameterAliases = null,
        IDictionary<string, IDictionary<string, IEnumerable<string>>>? parameterValueAliases = null,
        IEnumerable<string>? propagatedParameters = null,
        IDictionary<string, string>? primaryCollectionProperties = null)
    {
        if (aliases != null)
        {
            foreach (var alias in aliases)
            {
                Aliases.Add(alias);
            }
        }

        if (parameterAliases != null)
        {
            foreach (var (param, pAliases) in parameterAliases)
            {
                ParameterAliases[param] = new HashSet<string>(pAliases, StringComparer.OrdinalIgnoreCase);
            }
        }

        if (computedParameters != null)
        {
            foreach (var (param, computer) in computedParameters)
            {
                ComputedParameters[param] = computer;
            }
        }

        if (nestedParameterAliases != null)
        {
            foreach (var (path, propDict) in nestedParameterAliases)
            {
                var inner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (prop, pAliases) in propDict)
                {
                    inner[prop] = new HashSet<string>(pAliases, StringComparer.OrdinalIgnoreCase);
                }
                NestedParameterAliases[path] = inner;
            }
        }

        if (parameterValueAliases != null)
        {
            foreach (var (path, valDict) in parameterValueAliases)
            {
                var inner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (canonicalVal, vAliases) in valDict)
                {
                    inner[canonicalVal] = new HashSet<string>(vAliases, StringComparer.OrdinalIgnoreCase);
                }
                ParameterValueAliases[path] = inner;
            }
        }

        if (propagatedParameters != null)
        {
            foreach (var p in propagatedParameters)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    PropagatedParameters.Add(p.Trim());
                }
            }
        }

        if (primaryCollectionProperties != null)
        {
            foreach (var (path, prop) in primaryCollectionProperties)
            {
                if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(prop))
                {
                    PrimaryCollectionProperties[path.Trim()] = prop.Trim();
                }
            }
        }
    }

    /// <summary>
    /// Adds a root-level parameter to propagate to nested collection items.
    /// </summary>
    public ToolMapping AddPropagatedParameter(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        PropagatedParameters.Add(parameterName.Trim());
        return this;
    }

    /// <summary>
    /// Configures the primary string property used when wrapping a raw string into an object for a collection parameter.
    /// </summary>
    public ToolMapping SetPrimaryCollectionProperty(string paramPath, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        PrimaryCollectionProperties[paramPath.Trim()] = propertyName.Trim();
        return this;
    }

    /// <summary>
    /// Adds a parameter value alias mapping for a specified parameter or nested property path.
    /// </summary>
    public ToolMapping AddParameterValueAlias(string paramPath, string canonicalValue, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalValue);

        if (!ParameterValueAliases.TryGetValue(paramPath, out var valDict))
        {
            valDict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            ParameterValueAliases[paramPath] = valDict;
        }

        if (!valDict.TryGetValue(canonicalValue, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            valDict[canonicalValue] = set;
        }

        foreach (var a in aliases)
        {
            if (!string.IsNullOrWhiteSpace(a))
            {
                set.Add(a.Trim());
            }
        }

        return this;
    }

    /// <summary>
    /// Discovers and registers <see cref="ToolValueAliasAttribute"/> aliases defined on enum members of <typeparamref name="TEnum"/>.
    /// </summary>
    public ToolMapping AddEnumAliases<TEnum>(string? paramPath = null) where TEnum : struct, Enum
    {
        var enumType = typeof(TEnum);
        var targetPath = paramPath ?? enumType.Name;

        foreach (var field in enumType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var canonicalVal = field.Name;
            var aliasAttr = (ToolValueAliasAttribute?)Attribute.GetCustomAttribute(field, typeof(ToolValueAliasAttribute));
            if (aliasAttr != null && aliasAttr.Aliases.Length > 0)
            {
                AddParameterValueAlias(targetPath, canonicalVal, aliasAttr.Aliases);
            }
        }

        return this;
    }

    /// <summary>
    /// Attempts to resolve a parameter value alias (synonyms, plural/singular forms, casing variations)
    /// to its canonical value for the specified parameter name or nested property path.
    /// </summary>
    public bool TryResolveValueAlias(string paramPath, string rawValue, out string canonicalValue)
    {
        canonicalValue = rawValue;
        if (ParameterValueAliases == null || ParameterValueAliases.Count == 0 || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (FindValueAlias(paramPath, rawValue, out canonicalValue))
        {
            return true;
        }

        var altPath = paramPath.EndsWith("[]", StringComparison.Ordinal)
            ? paramPath.Substring(0, paramPath.Length - 2)
            : paramPath + "[]";

        if (FindValueAlias(altPath, rawValue, out canonicalValue))
        {
            return true;
        }

        var lastDot = paramPath.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < paramPath.Length - 1)
        {
            var leafParam = paramPath.Substring(lastDot + 1).TrimEnd('[', ']');
            if (FindValueAlias(leafParam, rawValue, out canonicalValue))
            {
                return true;
            }
        }

        if (FindValueAlias("*", rawValue, out canonicalValue))
        {
            return true;
        }

        canonicalValue = rawValue;
        return false;
    }

    private bool FindValueAlias(string path, string rawValue, out string canonicalValue)
    {
        canonicalValue = rawValue;
        if (!ParameterValueAliases.TryGetValue(path, out var valMap))
        {
            var snakePath = LenientToolDispatcher.ToSnakeCase(path);
            if (!ParameterValueAliases.TryGetValue(snakePath, out valMap))
            {
                var camelPath = LenientToolDispatcher.ToCamelCase(path);
                if (!ParameterValueAliases.TryGetValue(camelPath, out valMap))
                {
                    var pascalPath = LenientToolDispatcher.ToPascalCase(path);
                    if (!ParameterValueAliases.TryGetValue(pascalPath, out valMap))
                    {
                        return false;
                    }
                }
            }
        }

        // 1. Direct or registered alias match (case-insensitive)
        foreach (var (canonical, aliases) in valMap)
        {
            if (string.Equals(canonical, rawValue, StringComparison.OrdinalIgnoreCase))
            {
                canonicalValue = canonical;
                return true;
            }
            if (aliases.Contains(rawValue))
            {
                canonicalValue = canonical;
                return true;
            }
        }

        // 2. Snake_case match (e.g. "memory_limit" vs "MemoryLimit" or "memoryLimit")
        var snakeRaw = LenientToolDispatcher.ToSnakeCase(rawValue);
        foreach (var (canonical, aliases) in valMap)
        {
            if (string.Equals(LenientToolDispatcher.ToSnakeCase(canonical), snakeRaw, StringComparison.OrdinalIgnoreCase))
            {
                canonicalValue = canonical;
                return true;
            }
            foreach (var alias in aliases)
            {
                if (string.Equals(LenientToolDispatcher.ToSnakeCase(alias), snakeRaw, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalValue = canonical;
                    return true;
                }
            }
        }

        // 3. Plural / singular heuristic (e.g. "wild tigers" -> "wild tiger" or "tigers" -> "tiger")
        if (rawValue.EndsWith("s", StringComparison.OrdinalIgnoreCase) && rawValue.Length > 1)
        {
            var singular = rawValue.Substring(0, rawValue.Length - 1);
            var snakeSingular = LenientToolDispatcher.ToSnakeCase(singular);
            foreach (var (canonical, aliases) in valMap)
            {
                if (string.Equals(canonical, singular, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(LenientToolDispatcher.ToSnakeCase(canonical), snakeSingular, StringComparison.OrdinalIgnoreCase) ||
                    aliases.Contains(singular))
                {
                    canonicalValue = canonical;
                    return true;
                }
                foreach (var alias in aliases)
                {
                    if (string.Equals(LenientToolDispatcher.ToSnakeCase(alias), snakeSingular, StringComparison.OrdinalIgnoreCase))
                    {
                        canonicalValue = canonical;
                        return true;
                    }
                }
            }
        }
        else
        {
            var plural = rawValue + "s";
            var snakePlural = LenientToolDispatcher.ToSnakeCase(plural);
            foreach (var (canonical, aliases) in valMap)
            {
                if (string.Equals(canonical, plural, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(LenientToolDispatcher.ToSnakeCase(canonical), snakePlural, StringComparison.OrdinalIgnoreCase) ||
                    aliases.Contains(plural))
                {
                    canonicalValue = canonical;
                    return true;
                }
                foreach (var alias in aliases)
                {
                    if (string.Equals(LenientToolDispatcher.ToSnakeCase(alias), snakePlural, StringComparison.OrdinalIgnoreCase))
                    {
                        canonicalValue = canonical;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to resolve a nested property alias for a given parameter path.
    /// </summary>
    public bool TryGetNestedAlias(string paramPath, string aliasOrKey, out string canonicalName)
    {
        canonicalName = aliasOrKey;
        if (NestedParameterAliases == null || NestedParameterAliases.Count == 0)
        {
            return false;
        }

        if (FindNestedAlias(paramPath, aliasOrKey, out canonicalName))
        {
            return true;
        }

        var trimmedPath = paramPath.EndsWith("[]", StringComparison.Ordinal)
            ? paramPath.Substring(0, paramPath.Length - 2)
            : paramPath + "[]";

        if (FindNestedAlias(trimmedPath, aliasOrKey, out canonicalName))
        {
            return true;
        }

        if (FindNestedAlias("*", aliasOrKey, out canonicalName))
        {
            return true;
        }

        return false;
    }

    private bool FindNestedAlias(string path, string aliasOrKey, out string canonicalName)
    {
        if (NestedParameterAliases.TryGetValue(path, out var propDict))
        {
            foreach (var (canonical, aliases) in propDict)
            {
                if (string.Equals(canonical, aliasOrKey, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalName = canonical;
                    return true;
                }
                if (aliases.Contains(aliasOrKey))
                {
                    canonicalName = canonical;
                    return true;
                }
            }
        }
        canonicalName = aliasOrKey;
        return false;
    }
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

/// <summary>
/// Declarative configuration model for tool dispatching, alias resolution, and shell translations.
/// Reflection-free and Native AOT compatible.
/// </summary>
public class ToolDispatcherConfiguration
{
    /// <summary>Configured canonical tools and their alias mappings.</summary>
    public Dictionary<string, ToolMapping> Tools { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configured virtual-to-shell tool translations.</summary>
    public List<ToolTranslation> Translations { get; init; } = [];
}

/// <summary>
/// Severity of a tool dispatcher schema validation issue.
/// </summary>
public enum ToolValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Diagnostic issue detected during schema validation.
/// </summary>
public record ToolValidationIssue(
    ToolValidationSeverity Severity,
    string ToolName,
    string? ParameterName,
    string Message);

/// <summary>
/// Report produced by validating a <see cref="LenientToolDispatcher"/> against registered <see cref="AIFunction"/> instances.
/// </summary>
public class ToolDispatcherValidationReport
{
    /// <summary>Whether the configuration contains zero errors (warnings permitted).</summary>
    public bool IsValid => Issues.All(i => i.Severity != ToolValidationSeverity.Error);

    /// <summary>List of validation issues.</summary>
    public IReadOnlyList<ToolValidationIssue> Issues { get; init; } = Array.Empty<ToolValidationIssue>();

    /// <summary>Throws <see cref="ToolDispatcherValidationException"/> if any error issues exist.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            var errors = string.Join("\n", Issues.Where(i => i.Severity == ToolValidationSeverity.Error).Select(i => $"  - {i.Message}"));
            throw new ToolDispatcherValidationException($"LenientToolDispatcher validation failed:\n{errors}");
        }
    }
}

/// <summary>
/// Exception thrown when tool dispatcher schema validation fails.
/// </summary>
public sealed class ToolDispatcherValidationException(string message) : Exception(message);

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

/// <summary>
/// Result of normalizing a tool invocation call.
/// </summary>
public sealed record DispatchedToolCall(
    string CanonicalToolName,
    AIFunctionArguments Arguments,
    string OriginalToolName,
    bool WasTranslated,
    string? RenderedCommand = null);

/// <summary>
/// Configurable tool router with alias resolution, parameter translation, and schema validation.
/// 100% Native AOT compatible and reflection-free.
/// </summary>
public class LenientToolDispatcher
{
    private readonly LenientToolDispatcherOptions _options;
    private readonly object _syncLock = new();
    private ToolDispatcherConfiguration _configuration = new();
    private readonly Dictionary<string, AIFunction> _registeredTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalToolNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliasToCanonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolTranslation> _translationsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (string CanonicalName, JsonElement Schema)>> _toolSchemaProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _toolRootSchemas = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TemplatePlaceholderRegex = new(@"\{([a-zA-Z0-9_]+)(?::([^}]*))?\}", RegexOptions.Compiled);

    public LenientDispatchTelemetry? Telemetry
    {
        get => _options.Telemetry;
        set => _options.Telemetry = value;
    }

    public string? SessionId
    {
        get => _options.SessionId;
        set => _options.SessionId = value;
    }

    public LenientToolDispatcher(LenientToolDispatcherOptions? options = null)
    {
        _options = options ?? new LenientToolDispatcherOptions();
    }

    /// <summary>
    /// Ingests a strongly-typed tool dispatcher configuration.
    /// </summary>
    public void Configure(ToolDispatcherConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_syncLock)
        {
            _configuration = configuration;
            RebuildLookupTables();
        }
    }

    private void RebuildLookupTables()
    {
        _canonicalToolNames.Clear();
        _aliasToCanonical.Clear();
        _translationsBySource.Clear();

        if (_configuration.Translations != null)
        {
            foreach (var t in _configuration.Translations)
            {
                if (!string.IsNullOrWhiteSpace(t.SourceTool))
                {
                    _translationsBySource[t.SourceTool] = t;
                }
            }
        }

        if (_configuration.Tools != null)
        {
            foreach (var (canonical, mapping) in _configuration.Tools)
            {
                _canonicalToolNames[canonical] = canonical;
                if (mapping.Aliases != null)
                {
                    foreach (var alias in mapping.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(alias))
                        {
                            _aliasToCanonical[alias] = canonical;
                        }
                    }
                }
            }
        }

        foreach (var (toolName, tool) in _registeredTools)
        {
            _canonicalToolNames[tool.Name] = tool.Name;
        }
    }

    /// <summary>
    /// Registers an executable <see cref="AIFunction"/> canonical tool.
    /// </summary>
    public void RegisterTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        lock (_syncLock)
        {
            RegisterToolInternal(tool);
        }
    }

    private void RegisterToolInternal(AIFunction tool)
    {
        _canonicalToolNames[tool.Name] = tool.Name;

        if (_registeredTools.TryGetValue(tool.Name, out var existing))
        {
            if (ReferenceEquals(existing, tool))
            {
                return;
            }

            if (_toolRootSchemas.TryGetValue(tool.Name, out var existingSchema) &&
                AreSchemasEqual(existingSchema, tool.JsonSchema))
            {
                _registeredTools[tool.Name] = tool;
                return;
            }

            _registeredTools[tool.Name] = tool;
        }
        else
        {
            _registeredTools[tool.Name] = tool;
        }

        CacheToolSchema(tool);
    }

    private static bool AreSchemasEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        if (a.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
    }

    internal bool TryGetCachedSchemaProperties(string toolName, out Dictionary<string, (string CanonicalName, JsonElement Schema)>? properties)
        => _toolSchemaProperties.TryGetValue(toolName, out properties);

    private void CacheToolSchema(AIFunction tool)
    {
        _toolRootSchemas[tool.Name] = tool.JsonSchema;
        if (tool.JsonSchema.ValueKind == JsonValueKind.Object &&
            tool.JsonSchema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            var propDict = new Dictionary<string, (string CanonicalName, JsonElement Schema)>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in props.EnumerateObject())
            {
                propDict[prop.Name] = (prop.Name, prop.Value);
                propDict[ToSnakeCase(prop.Name)] = (prop.Name, prop.Value);
                propDict[ToCamelCase(prop.Name)] = (prop.Name, prop.Value);
                propDict[ToPascalCase(prop.Name)] = (prop.Name, prop.Value);
            }
            _toolSchemaProperties[tool.Name] = propDict;
        }
        else
        {
            _toolSchemaProperties.Remove(tool.Name);
        }
    }

    /// <summary>
    /// Registers a collection of executable <see cref="AIFunction"/> canonical tools.
    /// </summary>
    public void RegisterTools(IEnumerable<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        lock (_syncLock)
        {
            foreach (var t in tools)
            {
                RegisterToolInternal(t);
            }
        }
    }

    /// <summary>
    /// Validates the configured mappings and translations against registered tools and their schemas.
    /// </summary>
    public ToolDispatcherValidationReport Validate(bool throwOnError = true)
    {
        var issues = new List<ToolValidationIssue>();
        var seenAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Tool existence and parameter schema validation
        foreach (var (toolName, mapping) in _configuration.Tools)
        {
            if (!_registeredTools.TryGetValue(toolName, out var aiFunction))
            {
                issues.Add(new ToolValidationIssue(
                    ToolValidationSeverity.Error,
                    toolName,
                    null,
                    $"Configured tool '{toolName}' is not registered as an AIFunction."));
                continue;
            }

            // Inspect parameter schema properties
            var schemaProps = ExtractSchemaProperties(aiFunction.JsonSchema);

            foreach (var (canonicalParam, aliases) in mapping.ParameterAliases)
            {
                if (schemaProps != null && !schemaProps.Contains(canonicalParam))
                {
                    issues.Add(new ToolValidationIssue(
                        ToolValidationSeverity.Error,
                        toolName,
                        canonicalParam,
                        $"Configured parameter alias targets non-existent parameter '{canonicalParam}' on tool '{toolName}'."));
                }

                // Check duplicate aliases within the same tool mapping
                var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var alias in aliases)
                {
                    if (string.Equals(alias, canonicalParam, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Warning,
                            toolName,
                            canonicalParam,
                            $"Parameter alias '{alias}' on tool '{toolName}' is identical to the canonical parameter name."));
                    }
                    if (!aliasSet.Add(alias))
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Error,
                            toolName,
                            canonicalParam,
                            $"Duplicate parameter alias '{alias}' declared on tool '{toolName}'."));
                    }
                }
            }

            // Inspect computed parameters
            if (mapping.ComputedParameters != null)
            {
                foreach (var computedParam in mapping.ComputedParameters.Keys)
                {
                    if (schemaProps != null && !schemaProps.Contains(computedParam))
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Error,
                            toolName,
                            computedParam,
                            $"Computed parameter targets non-existent parameter '{computedParam}' on tool '{toolName}'."));
                    }
                }
            }

            // Inspect nested parameter aliases
            if (mapping.NestedParameterAliases != null)
            {
                foreach (var (path, _) in mapping.NestedParameterAliases)
                {
                    var rootParam = path.Split('.', '[')[0];
                    if (rootParam != "*" && schemaProps != null && !schemaProps.Contains(rootParam))
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Warning,
                            toolName,
                            rootParam,
                            $"Nested parameter alias path '{path}' targets non-existent root parameter '{rootParam}' on tool '{toolName}'."));
                    }
                }
            }

            // Alias collision detection across tools
            foreach (var alias in mapping.Aliases)
            {
                if (string.Equals(alias, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ToolValidationIssue(
                        ToolValidationSeverity.Warning,
                        toolName,
                        null,
                        $"Tool alias '{alias}' is identical to the canonical tool name."));
                }
                else if (seenAliases.TryGetValue(alias, out var existingOwner))
                {
                    if (string.Equals(existingOwner, toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Error,
                            toolName,
                            null,
                            $"Duplicate alias '{alias}' declared within tool '{toolName}'."));
                    }
                    else
                    {
                        issues.Add(new ToolValidationIssue(
                            ToolValidationSeverity.Warning,
                            toolName,
                            null,
                            $"Alias collision: '{alias}' is mapped to both '{existingOwner}' and '{toolName}'. First-configured will win."));
                    }
                }
                else
                {
                    seenAliases[alias] = toolName;
                }
            }
        }

        // 2. Validate translations
        foreach (var translation in _configuration.Translations)
        {
            if (!_registeredTools.ContainsKey(translation.TargetTool))
            {
                issues.Add(new ToolValidationIssue(
                    ToolValidationSeverity.Error,
                    translation.TargetTool,
                    null,
                    $"Translation source '{translation.SourceTool}' targets non-existent tool '{translation.TargetTool}'."));
            }
        }

        var report = new ToolDispatcherValidationReport { Issues = issues };
        if (throwOnError)
        {
            report.ThrowIfInvalid();
        }
        return report;
    }

    /// <summary>
    /// Normalizes a tool name and raw argument JSON into a canonical dispatched call.
    /// </summary>
    public DispatchedToolCall NormalizeCall(string rawToolName, string rawArgumentsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToolName);
        var rawArgs = ToolCallHealingTransform.ParseArgumentsJson(rawArgumentsJson, null, rawToolName);
        return NormalizeCall(rawToolName, rawArgs);
    }

    /// <summary>
    /// Normalizes a tool name and arguments dictionary into a canonical dispatched call.
    /// </summary>
    public DispatchedToolCall NormalizeCall(string rawToolName, IDictionary<string, object?> rawArgs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToolName);

        var toolName = rawToolName.Trim();
        if (_options.StripNamespacePrefixes)
        {
            var stripped = StripNamespace(toolName);
            if (!string.Equals(stripped, toolName, StringComparison.Ordinal))
            {
                Telemetry?.Record(LenientDispatchEventType.NamespaceStripped, toolName, stripped, SessionId);
                toolName = stripped;
            }
        }

        // 1. Check if tool is a translation rule via O(1) lookup
        if (_translationsBySource.TryGetValue(toolName, out var translation))
        {
            Telemetry?.Record(LenientDispatchEventType.ToolAliasResolved, toolName, translation.TargetTool, SessionId);
            return RenderTranslationCall(translation, rawArgs, rawToolName);
        }

        // 2. Resolve canonical tool name (exact match always wins over aliases)
        var (canonicalTool, mapping, wasAliasResolved) = ResolveCanonicalToolAndMapping(toolName);
        if (wasAliasResolved)
        {
            Telemetry?.Record(LenientDispatchEventType.ToolAliasResolved, toolName, canonicalTool, SessionId);
        }

        if (string.Equals(toolName, canonicalTool, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolName, canonicalTool, StringComparison.Ordinal))
        {
            Telemetry?.Record(LenientDispatchEventType.CasingNormalized, toolName, canonicalTool, SessionId);
        }

        // 3. Resolve parameter aliases
        var canonicalArgs = new AIFunctionArguments();
        if (mapping != null && mapping.ParameterAliases.Count > 0)
        {
            var matchedRawKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (canonicalParam, aliases) in mapping.ParameterAliases)
            {
                // Exact match has highest precedence
                if (rawArgs.TryGetValue(canonicalParam, out var exactVal))
                {
                    canonicalArgs[canonicalParam] = exactVal;
                    matchedRawKeys.Add(canonicalParam);
                    continue;
                }

                // Check case-insensitive match for canonical param
                if (TryGetValueCaseInsensitive(rawArgs, canonicalParam, out var caseInsensitiveVal, out var matchedKey))
                {
                    canonicalArgs[canonicalParam] = caseInsensitiveVal;
                    matchedRawKeys.Add(matchedKey!);
                    if (!string.Equals(matchedKey, canonicalParam, StringComparison.Ordinal))
                    {
                        Telemetry?.Record(LenientDispatchEventType.CasingNormalized, matchedKey!, canonicalParam, SessionId);
                    }
                    continue;
                }

                // Check aliases
                foreach (var alias in aliases)
                {
                    if (TryGetValueCaseInsensitive(rawArgs, alias, out var aliasVal, out var matchedAliasKey))
                    {
                        canonicalArgs[canonicalParam] = aliasVal;
                        matchedRawKeys.Add(matchedAliasKey!);
                        Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, matchedAliasKey!, canonicalParam, SessionId);
                        break;
                    }
                }
            }

            // Include any remaining arguments that were not remapped
            foreach (var (k, v) in rawArgs)
            {
                if (!matchedRawKeys.Contains(k))
                {
                    canonicalArgs[k] = v;
                }
            }
        }
        else
        {
            foreach (var (k, v) in rawArgs)
            {
                canonicalArgs[k] = v;
            }
        }

        // 4. Evaluate computed parameters if canonical parameter not already resolved
        if (mapping?.ComputedParameters != null && mapping.ComputedParameters.Count > 0)
        {
            var caseInsensitiveRaw = rawArgs is Dictionary<string, object?> dict && dict.Comparer == StringComparer.OrdinalIgnoreCase
                ? rawArgs
                : new Dictionary<string, object?>(rawArgs, StringComparer.OrdinalIgnoreCase);

            foreach (var (computedParam, computer) in mapping.ComputedParameters)
            {
                if (!canonicalArgs.ContainsKey(computedParam) || canonicalArgs[computedParam] == null)
                {
                    try
                    {
                        var computedVal = computer(caseInsensitiveRaw);
                        if (computedVal != null)
                        {
                            canonicalArgs[computedParam] = computedVal;
                            Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, "<computed>", computedParam, SessionId);
                        }
                    }
                    catch (Exception)
                    {
                        // In lenient mode, ignore computer failures and let tool execution or validation report missing/invalid arguments.
                    }
                }
            }
        }

        // 5. Deep schema-driven and mapping-driven normalization (nested property renaming, casing matching, stringified JSON parsing, value alias resolution)
        _toolRootSchemas.TryGetValue(canonicalTool, out var rootSchema);
        _toolSchemaProperties.TryGetValue(canonicalTool, out var propDict);

        // 4.5. Flattened collection promotion & array-to-flat unpacking & replaceAll propagation
        if (_options.AutoPromoteFlattenedArrayParameters && propDict != null)
        {
            PromoteFlattenedParametersToArrays(canonicalTool, canonicalArgs, propDict, rootSchema, mapping);
            UnpackSingleItemArraysToFlatParameters(canonicalTool, canonicalArgs, propDict, rootSchema);
        }

        if (_options.AutoPropagateCollectionParameters && propDict != null)
        {
            PropagateUnmappedParametersToCollections(canonicalArgs, propDict, rootSchema, mapping);
        }

        var currentKeys = canonicalArgs.Keys.ToList();
        foreach (var key in currentKeys)
        {
            var val = canonicalArgs[key];
            if (val == null) continue;

            string canonicalKey = key;
            JsonElement? paramSchema = null;

            if (propDict != null && propDict.TryGetValue(key, out var schemaEntry))
            {
                canonicalKey = schemaEntry.CanonicalName;
                paramSchema = schemaEntry.Schema;
            }

            var normalizedVal = NormalizeValueDeep(val, paramSchema, mapping, canonicalKey, rootSchema);

            if (!string.Equals(canonicalKey, key, StringComparison.Ordinal))
            {
                canonicalArgs.Remove(key);
                canonicalArgs[canonicalKey] = normalizedVal;
                if (string.Equals(canonicalKey, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ToSnakeCase(canonicalKey), ToSnakeCase(key), StringComparison.OrdinalIgnoreCase))
                {
                    Telemetry?.Record(LenientDispatchEventType.CasingNormalized, key, canonicalKey, SessionId);
                }
                else
                {
                    Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, key, canonicalKey, SessionId);
                }
            }
            else
            {
                canonicalArgs[key] = normalizedVal;
            }
        }

        if (_options.EnableParameterCoercion)
        {
            CoerceParameters(canonicalTool, canonicalArgs);
        }

        return new DispatchedToolCall(canonicalTool, canonicalArgs, rawToolName, false);
    }

    /// <summary>
    /// Normalizes a <see cref="FunctionCallContent"/> into a canonical dispatched call.
    /// </summary>
    public DispatchedToolCall NormalizeCall(FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var args = call.Arguments ?? new Dictionary<string, object?>();
        return NormalizeCall(call.Name, args);
    }

    /// <summary>
    /// Dispatches and executes the resolved canonical tool using JSON arguments.
    /// </summary>
    public async Task<object?> ExecuteAsync(string rawToolName, string rawArgumentsJson, CancellationToken ct = default)
    {
        var dispatched = NormalizeCall(rawToolName, rawArgumentsJson);
        return await ExecuteDispatchedAsync(dispatched, rawToolName, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches and executes the resolved canonical tool using dictionary arguments.
    /// </summary>
    public async Task<object?> ExecuteAsync(string rawToolName, IDictionary<string, object?> rawArgs, CancellationToken ct = default)
    {
        var dispatched = NormalizeCall(rawToolName, rawArgs);
        return await ExecuteDispatchedAsync(dispatched, rawToolName, ct).ConfigureAwait(false);
    }

    private async Task<object?> ExecuteDispatchedAsync(DispatchedToolCall dispatched, string rawToolName, CancellationToken ct)
    {
        if (!_registeredTools.TryGetValue(dispatched.CanonicalToolName, out var tool))
        {
            throw new InvalidOperationException($"Tool '{dispatched.CanonicalToolName}' (from original '{rawToolName}') is not registered.");
        }

        var rawResult = await tool.InvokeAsync(dispatched.Arguments, ct).ConfigureAwait(false);
        if (rawResult is JsonElement je)
        {
            return ToolCallHealingTransform.UnwrapJsonElement(je);
        }
        return rawResult;
    }

    private DispatchedToolCall RenderTranslationCall(ToolTranslation translation, IDictionary<string, object?> rawArgs, string originalName)
    {
        var template = translation.CommandTemplate;

        var rendered = TemplatePlaceholderRegex.Replace(template, match =>
        {
            var paramName = match.Groups[1].Value;
            var hasDefault = match.Groups[2].Success;
            var defaultValue = match.Groups[2].Value;

            object? val = null;
            if (!TryGetValueCaseInsensitive(rawArgs, paramName, out val))
            {
                if (_configuration.Tools.TryGetValue(translation.SourceTool, out var sourceMapping) &&
                    sourceMapping.ParameterAliases.TryGetValue(paramName, out var pAliases))
                {
                    foreach (var alias in pAliases)
                    {
                        if (TryGetValueCaseInsensitive(rawArgs, alias, out val))
                        {
                            break;
                        }
                    }
                }
            }

            if (val == null && hasDefault)
            {
                val = defaultValue;
            }

            if (val == null)
            {
                throw new KeyNotFoundException($"Required parameter '{paramName}' is missing for translation '{translation.SourceTool}' -> '{translation.TargetTool}'.");
            }

            var strVal = val.ToString() ?? string.Empty;

            // Reject leading '-' option injection unless it's a default or flag-safe value
            if (strVal.StartsWith('-') && (!hasDefault || strVal != defaultValue))
            {
                throw new ArgumentException($"Parameter '{paramName}' value '{strVal}' begins with '-' which is prohibited to prevent option injection.");
            }

            return EscapeParameter(strVal, translation.EscapeMethod);
        });

        if (_options.ValidateTranslationsWithReadOnlyValidator)
        {
            var validator = _options.CommandValidator ?? new ReadOnlyCommandValidator();
            var valResult = validator.ValidateCommand(rendered);
            if (!valResult.IsAllowed)
            {
                throw new InvalidOperationException($"Rendered translation command '{rendered}' was blocked by ReadOnlyCommandValidator: {valResult.Reason}");
            }
        }

        // Target tool arguments: find parameter name (default "command")
        string paramKey = "command";
        if (_registeredTools.TryGetValue(translation.TargetTool, out var targetFn))
        {
            var props = ExtractSchemaProperties(targetFn.JsonSchema);
            if (props != null && props.Count > 0)
            {
                paramKey = props.Contains("command") ? "command"
                    : props.Contains("commandLine") ? "commandLine"
                    : props.Contains("cmd") ? "cmd"
                    : props.Contains("script") ? "script"
                    : props.First();
            }
        }

        var targetArgs = new AIFunctionArguments { [paramKey] = rendered };
        return new DispatchedToolCall(translation.TargetTool, targetArgs, originalName, true, rendered);
    }

    private static bool TryGetValueCaseInsensitive(IDictionary<string, object?> dict, string key, out object? value)
    {
        return TryGetValueCaseInsensitive(dict, key, out value, out _);
    }

    private static bool TryGetValueCaseInsensitive(IDictionary<string, object?> dict, string key, out object? value, out string? matchedKey)
    {
        if (dict.TryGetValue(key, out value))
        {
            matchedKey = key;
            return true;
        }
        foreach (var (k, v) in dict)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                matchedKey = k;
                return true;
            }
        }
        value = null;
        matchedKey = null;
        return false;
    }

    private static string EscapeParameter(string value, ParameterEscapeMethod method)
    {
        return method switch
        {
            ParameterEscapeMethod.Bash => "'" + value.Replace("'", "'\\''") + "'",
            ParameterEscapeMethod.PowerShell => "'" + value.Replace("'", "''") + "'",
            _ => value
        };
    }

    /// <summary>
    /// Gets whether any canonical tools are registered in the dispatcher.
    /// </summary>
    public bool HasRegisteredTools => _registeredTools.Count > 0;

    /// <summary>
    /// Attempts to retrieve a registered <see cref="AIFunction"/> canonical tool by name.
    /// </summary>
    public bool TryGetRegisteredTool(string name, out AIFunction? tool)
    {
        if (_registeredTools.TryGetValue(name, out tool))
        {
            return true;
        }

        var canonical = ResolveCanonicalToolName(name);
        return _registeredTools.TryGetValue(canonical, out tool);
    }

    /// <summary>
    /// Resolves a raw candidate tool name to its canonical tool name, taking into account
    /// namespace prefixes, tool translations, and tool aliases.
    /// </summary>
    public string ResolveCanonicalToolName(string rawToolName)
    {
        if (string.IsNullOrWhiteSpace(rawToolName))
        {
            return rawToolName;
        }

        var toolName = rawToolName.Trim();
        if (_options.StripNamespacePrefixes)
        {
            toolName = StripNamespace(toolName);
        }

        // 1. Check if tool is a translation rule via O(1) lookup
        if (_translationsBySource.TryGetValue(toolName, out var translation))
        {
            return translation.TargetTool;
        }

        // 2. Resolve canonical tool name (exact match always wins over aliases)
        var (canonicalTool, _, _) = ResolveCanonicalToolAndMapping(toolName);
        return canonicalTool;
    }

    private (string CanonicalName, ToolMapping? Mapping, bool WasAliasResolved) ResolveCanonicalToolAndMapping(string toolName)
    {
        if (_registeredTools.TryGetValue(toolName, out var registeredTool))
        {
            var canonical = registeredTool.Name;
            _configuration.Tools.TryGetValue(canonical, out var mapping);
            if (mapping == null && _aliasToCanonical.TryGetValue(canonical, out var aliasedConfigCanonical))
            {
                _configuration.Tools.TryGetValue(aliasedConfigCanonical, out mapping);
            }
            return (canonical, mapping, false);
        }

        if (_canonicalToolNames.TryGetValue(toolName, out var configuredCanonical))
        {
            _configuration.Tools.TryGetValue(configuredCanonical, out var mapping);
            if (_registeredTools.TryGetValue(configuredCanonical, out var rt))
            {
                return (rt.Name, mapping, false);
            }
            if (mapping != null)
            {
                foreach (var alias in mapping.Aliases)
                {
                    if (_registeredTools.TryGetValue(alias, out var aliasTool))
                    {
                        return (aliasTool.Name, mapping, false);
                    }
                }
            }
            return (configuredCanonical, mapping, false);
        }

        if (_aliasToCanonical.TryGetValue(toolName, out var canonicalAlias))
        {
            _configuration.Tools.TryGetValue(canonicalAlias, out var mapping);
            string targetTool = canonicalAlias;
            if (_registeredTools.TryGetValue(canonicalAlias, out var rt))
            {
                targetTool = rt.Name;
            }
            else if (mapping != null)
            {
                foreach (var alias in mapping.Aliases)
                {
                    if (_registeredTools.TryGetValue(alias, out var aliasTool))
                    {
                        targetTool = aliasTool.Name;
                        break;
                    }
                }
            }
            return (targetTool, mapping, true);
        }

        return (toolName, null, false);
    }

    /// <summary>
    /// Checks whether the given tool name is registered as a canonical tool or alias.
    /// </summary>
    public bool IsToolRegistered(string rawToolName)
    {
        if (string.IsNullOrWhiteSpace(rawToolName))
        {
            return false;
        }

        var canonical = ResolveCanonicalToolName(rawToolName);
        var stripped = StripNamespace(rawToolName.Trim());

        return _registeredTools.ContainsKey(rawToolName) ||
               _registeredTools.ContainsKey(canonical) ||
               _registeredTools.ContainsKey(stripped) ||
               _configuration.Tools.ContainsKey(rawToolName) ||
               _configuration.Tools.ContainsKey(canonical);
    }

    /// <summary>
    /// Determines whether the specified candidate tool name matches one of the declared available tools,
    /// taking into account namespace prefixes, tool aliases, and virtual tool translations.
    /// </summary>
    public bool IsToolAvailable(string rawToolName, IEnumerable<AITool>? availableTools)
    {
        if (string.IsNullOrWhiteSpace(rawToolName) || availableTools == null)
        {
            return false;
        }

        var canonical = ResolveCanonicalToolName(rawToolName);
        var stripped = StripNamespace(rawToolName.Trim());

        foreach (var tool in availableTools)
        {
            var name = tool.Name ?? (tool is AIFunction fn ? fn.Name : null);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var declaredCanonical = ResolveCanonicalToolName(name);
            var declaredStripped = StripNamespace(name.Trim());

            // Direct match or canonical match
            if (string.Equals(name, rawToolName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, canonical, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, stripped, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(declaredCanonical, canonical, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(declaredStripped, stripped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // If declared tool has aliases in dispatcher config, check if rawToolName, stripped, or canonical matches
            if (_configuration.Tools.TryGetValue(name, out var mapping))
            {
                if (mapping.Aliases.Contains(rawToolName) || mapping.Aliases.Contains(stripped) || mapping.Aliases.Contains(canonical))
                {
                    return true;
                }
            }

            if (_configuration.Tools.TryGetValue(declaredCanonical, out var canonicalMapping))
            {
                if (canonicalMapping.Aliases.Contains(rawToolName) || canonicalMapping.Aliases.Contains(stripped) || canonicalMapping.Aliases.Contains(canonical))
                {
                    return true;
                }
            }

            // If declared tool is target of a translation whose SourceTool matches rawToolName, stripped, or canonical
            if (_configuration.Translations.Any(t =>
                (string.Equals(t.TargetTool, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(t.TargetTool, declaredCanonical, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(t.SourceTool, rawToolName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(t.SourceTool, stripped, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(t.SourceTool, canonical, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips namespace prefixes like "workspace__", "default:", "functions.", or "tools.".
    /// </summary>
    public static string StripNamespace(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var idx = name.LastIndexOf("__", StringComparison.Ordinal);
        if (idx >= 0) return name.Substring(idx + 2);

        idx = name.LastIndexOf(':');
        if (idx >= 0) return name.Substring(idx + 1);

        if (name.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(10);
        }

        if (name.StartsWith("tools.", StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(6);
        }

        idx = name.LastIndexOf('.');
        if (idx >= 0 && idx < name.Length - 1)
        {
            return name.Substring(idx + 1);
        }

        return name;
    }

    private static HashSet<string>? ExtractSchemaProperties(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in props.EnumerateObject())
            {
                set.Add(prop.Name);
            }
            return set;
        }
        return null;
    }

    private void CoerceParameters(string canonicalToolName, AIFunctionArguments arguments)
    {
        int count = arguments.Count;
        if (count == 0) return;

        _toolSchemaProperties.TryGetValue(canonicalToolName, out var propDict);
        _toolRootSchemas.TryGetValue(canonicalToolName, out var rootSchema);
        _configuration.Tools.TryGetValue(canonicalToolName, out var mapping);

        var keyArray = new string[count];
        arguments.Keys.CopyTo(keyArray, 0);

        for (int i = 0; i < count; i++)
        {
            var key = keyArray[i];
            var value = arguments[key];
            if (value == null) continue;

            JsonElement paramSchema = default;
            bool paramSchemaFound = false;
            string effectiveKey = key;

            if (propDict != null && propDict.TryGetValue(key, out var schemaEntry))
            {
                paramSchema = schemaEntry.Schema;
                paramSchemaFound = true;
                effectiveKey = schemaEntry.CanonicalName;
            }

            var resolvedSchema = paramSchemaFound ? ResolveSchema(paramSchema, rootSchema) : (JsonElement?)null;
            var coercedVal = CoerceParameterValue(effectiveKey, value, resolvedSchema, mapping);
            if (!string.Equals(effectiveKey, key, StringComparison.Ordinal))
            {
                arguments.Remove(key);
                arguments[effectiveKey] = coercedVal;
                Telemetry?.Record(LenientDispatchEventType.CasingNormalized, key, effectiveKey, SessionId);
            }
            else
            {
                arguments[key] = coercedVal;
            }
        }
    }

    private static object? CoerceParameterValue(string paramName, object value, JsonElement? schema, ToolMapping? mapping = null)
    {
        if (value is not string strVal)
        {
            return value;
        }

        var trimmed = strVal.Trim();

        // 0. ToolMapping parameter value alias resolution (synonyms, plural/singular, casing)
        string? aliasVal = null;
        if (mapping?.TryResolveValueAlias(paramName, trimmed, out aliasVal) == true && aliasVal != null)
        {
            trimmed = aliasVal;
        }

        // 1. Schema-based Enum matching (case-insensitive + casing/plural heuristics)
        if (schema.HasValue)
        {
            if (TryGetEnumOrConstMembers(schema.Value, out var enumMembers))
            {
                var snapped = SnapEnumValueCore(trimmed, enumMembers, telemetry: null, sessionId: null);
                if (!string.Equals(snapped, trimmed, StringComparison.Ordinal) || enumMembers.Contains(snapped))
                {
                    return snapped;
                }
            }

            if (schema.Value.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var enumEl in enumProp.EnumerateArray())
                {
                    if (enumEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        if (bool.TryParse(trimmed, out var parsedBool) && parsedBool == enumEl.GetBoolean())
                        {
                            return enumEl.GetBoolean();
                        }
                    }
                    else if (enumEl.ValueKind == JsonValueKind.Number)
                    {
                        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDbl))
                        {
                            if (Math.Abs(parsedDbl - enumEl.GetDouble()) < double.Epsilon)
                            {
                                return ToolCallHealingTransform.UnwrapJsonElement(enumEl);
                            }
                        }
                    }
                }
            }
        }

        // If a value alias was mapped and schema does not specify an alternative enum, return the resolved alias
        if (aliasVal != null)
        {
            return aliasVal;
        }

        // 2. Boolean coercion (schema or heuristic)
        bool isBooleanTarget = schema.HasValue && SchemaTypeMatches(schema.Value, "boolean");

        if (isBooleanTarget || (!schema.HasValue && IsBooleanParameterName(paramName)))
        {
            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
            {
                return true;
            }
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
            {
                return false;
            }
        }

        // 3. Number coercion (schema or heuristic)
        bool isIntegerTarget = schema.HasValue && SchemaTypeMatches(schema.Value, "integer");
        bool isNumberTarget = schema.HasValue && SchemaTypeMatches(schema.Value, "number");

        if (isIntegerTarget || (!schema.HasValue && IsNumericParameterName(paramName)))
        {
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
            {
                if (longVal >= int.MinValue && longVal <= int.MaxValue)
                {
                    return (int)longVal;
                }
                return longVal;
            }
        }

        if (isNumberTarget || (!schema.HasValue && IsNumericParameterName(paramName)))
        {
            if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dblVal))
            {
                return dblVal;
            }
        }

        // 4. Array coercion (schema or heuristic)
        bool isArrayTarget = schema.HasValue && SchemaTypeMatches(schema.Value, "array");

        if (isArrayTarget || (!schema.HasValue && IsArrayParameterName(paramName) && trimmed.Contains(',')))
        {
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        return doc.RootElement.EnumerateArray().Select(ToolCallHealingTransform.UnwrapJsonElement).ToArray();
                    }
                }
                catch
                {
                    // Fall back to comma splitting
                }
            }

            var parts = trimmed.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();

            if (schema.HasValue && schema.Value.TryGetProperty("items", out var itemsSchema) && itemsSchema.TryGetProperty("type", out var itemType))
            {
                if (itemType.ValueEquals("integer"))
                {
                    return parts.Select(p => int.TryParse(p, CultureInfo.InvariantCulture, out var iv) ? (object)iv : p).ToArray();
                }
                if (itemType.ValueEquals("number"))
                {
                    return parts.Select(p => double.TryParse(p, CultureInfo.InvariantCulture, out var dv) ? (object)dv : p).ToArray();
                }
            }

            return parts;
        }

        return value;
    }

    private static bool IsBooleanParameterName(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "cached" or "force" or "recursive" or "verbose" or "dry_run" or "enabled" or "disabled" or "strict" or "sync" or "async"
            || n.StartsWith("is_") || n.StartsWith("has_") || n.StartsWith("allow_") || n.StartsWith("check_");
    }

    private static bool IsNumericParameterName(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "start_line" or "end_line" or "line" or "count" or "limit" or "port" or "offset" or "size" or "year" or "page" or "timeout" or "delay" or "attempts" or "step"
            || n.EndsWith("_line") || n.EndsWith("_number") || n.EndsWith("_count") || n.EndsWith("_index")
            || n.EndsWith("_port") || n.EndsWith("_offset") || n.EndsWith("_limit") || n.EndsWith("_size")
            || n.EndsWith("_len") || n.EndsWith("_id") || n.EndsWith("_year") || n.EndsWith("_page");
    }

    private static bool IsArrayParameterName(string name)
    {
        var n = name.ToLowerInvariant();
        return n.EndsWith("s") || n.Contains("list") || n.Contains("array");
    }

    private static bool SchemaTypeMatches(JsonElement schema, string expectedType)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var typeProp))
        {
            if (typeProp.ValueKind == JsonValueKind.String)
            {
                return typeProp.ValueEquals(expectedType);
            }
            if (typeProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typeProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.ValueEquals(expectedType))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private object? NormalizeValueDeep(
        object? value,
        JsonElement? schema,
        ToolMapping? mapping,
        string paramPath,
        JsonElement? rootSchema)
    {
        if (value == null) return null;

        var resolvedSchema = schema.HasValue && rootSchema.HasValue
            ? ResolveSchema(schema.Value, rootSchema.Value)
            : schema;

        // 0. Unwrap JsonElement if present
        if (value is JsonElement je)
        {
            value = ConvertJsonElementToClr(je);
            if (value == null) return null;
        }

        // 1. String value alias resolution & stringified JSON parsing
        if (value is string str)
        {
            if (mapping?.TryResolveValueAlias(paramPath, str, out var resolvedStr) == true)
            {
                str = resolvedStr;
                value = str;
            }

            bool expectsArray = resolvedSchema.HasValue && (SchemaTypeMatches(resolvedSchema.Value, "array") || (resolvedSchema.Value.ValueKind == JsonValueKind.Object && resolvedSchema.Value.TryGetProperty("items", out _)));
            bool expectsObject = resolvedSchema.HasValue && (SchemaTypeMatches(resolvedSchema.Value, "object") || (resolvedSchema.Value.ValueKind == JsonValueKind.Object && resolvedSchema.Value.TryGetProperty("properties", out _)));

            var trimmed = str.Trim();
            if ((trimmed.StartsWith('{') && trimmed.EndsWith('}') && (expectsObject || expectsArray || !resolvedSchema.HasValue)) ||
                (trimmed.StartsWith('[') && trimmed.EndsWith(']') && (expectsArray || !resolvedSchema.HasValue)))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    value = ConvertJsonElementToClr(doc.RootElement);
                    if (value == null) return null;
                }
                catch (JsonException) { }
            }

            if (value is string currentStr && resolvedSchema.HasValue && TryGetEnumOrConstMembers(resolvedSchema.Value, out var enumMembers))
            {
                value = SnapEnumValueCore(currentStr, enumMembers, Telemetry, SessionId);
            }
        }

        // 2. Handle array of items
        JsonElement? itemsSchema = null;
        if (resolvedSchema.HasValue && resolvedSchema.Value.ValueKind == JsonValueKind.Object &&
            resolvedSchema.Value.TryGetProperty("items", out var isEl) && isEl.ValueKind == JsonValueKind.Object)
        {
            itemsSchema = isEl;
        }

        if (value is System.Collections.IEnumerable list and not string and not IDictionary<string, object?> and not System.Collections.IDictionary)
        {
            var normalizedList = new List<object?>();
            foreach (var item in list)
            {
                var effectiveItem = item;
                if (effectiveItem is string strItem && itemsSchema.HasValue)
                {
                    var resolvedItemSchema = rootSchema.HasValue ? ResolveSchema(itemsSchema.Value, rootSchema.Value) : itemsSchema.Value;
                    if (resolvedItemSchema.ValueKind == JsonValueKind.Object &&
                        resolvedItemSchema.TryGetProperty("properties", out var itemProps) &&
                        itemProps.ValueKind == JsonValueKind.Object)
                    {
                        var primaryProp = FindPrimaryStringProperty(itemProps, resolvedItemSchema, mapping, paramPath + "[]");
                        if (primaryProp != null)
                        {
                            effectiveItem = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                [primaryProp] = strItem
                            };
                        }
                    }
                }

                normalizedList.Add(NormalizeValueDeep(effectiveItem, itemsSchema, mapping, paramPath + "[]", rootSchema));
            }
            return normalizedList;
        }
        else if (resolvedSchema.HasValue && SchemaTypeMatches(resolvedSchema.Value, "array"))
        {
            if (value is IDictionary<string, object?> singleDict)
            {
                // Single object shortcut healing for array parameters
                return new List<object?>
                {
                    NormalizeValueDeep(singleDict, itemsSchema, mapping, paramPath + "[]", rootSchema)
                };
            }
            else if (value is string singleStr && itemsSchema.HasValue)
            {
                var resolvedItemSchema = rootSchema.HasValue ? ResolveSchema(itemsSchema.Value, rootSchema.Value) : itemsSchema.Value;
                if (resolvedItemSchema.ValueKind == JsonValueKind.Object &&
                    resolvedItemSchema.TryGetProperty("properties", out var itemProps) &&
                    itemProps.ValueKind == JsonValueKind.Object)
                {
                    var primaryProp = FindPrimaryStringProperty(itemProps, resolvedItemSchema, mapping, paramPath + "[]");
                    if (primaryProp != null)
                    {
                        var dictItem = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            [primaryProp] = singleStr
                        };
                        return new List<object?>
                        {
                            NormalizeValueDeep(dictItem, itemsSchema, mapping, paramPath + "[]", rootSchema)
                        };
                    }
                }
            }
        }

        // 3. Handle object properties (typed or untyped dictionary)
        IDictionary<string, object?>? dict = null;
        if (value is IDictionary<string, object?> typedDict)
        {
            dict = typedDict;
        }
        else if (value is System.Collections.IDictionary nonGenericDict)
        {
            dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry de in nonGenericDict)
            {
                if (de.Key != null)
                {
                    dict[de.Key.ToString()!] = de.Value;
                }
            }
        }

        if (dict != null)
        {
            var normalizedDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            var expectedProperties = new Dictionary<string, (string CanonicalName, JsonElement Schema)>(StringComparer.OrdinalIgnoreCase);

            if (resolvedSchema.HasValue && resolvedSchema.Value.ValueKind == JsonValueKind.Object &&
                resolvedSchema.Value.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    var resolvedProp = rootSchema.HasValue ? ResolveSchema(prop.Value, rootSchema.Value) : prop.Value;
                    expectedProperties[prop.Name] = (prop.Name, resolvedProp);
                    expectedProperties[ToSnakeCase(prop.Name)] = (prop.Name, resolvedProp);
                    expectedProperties[ToCamelCase(prop.Name)] = (prop.Name, resolvedProp);
                    expectedProperties[ToPascalCase(prop.Name)] = (prop.Name, resolvedProp);
                }
            }

            foreach (var (rawKey, rawVal) in dict)
            {
                string canonicalKey = rawKey;
                JsonElement? propSchema = null;

                if (expectedProperties.TryGetValue(rawKey, out var entry))
                {
                    canonicalKey = entry.CanonicalName;
                    propSchema = entry.Schema;
                }
                else if (mapping?.TryGetNestedAlias(paramPath, rawKey, out var mappedAlias) == true)
                {
                    canonicalKey = mappedAlias;
                    if (expectedProperties.TryGetValue(canonicalKey, out var canonicalEntry))
                    {
                        canonicalKey = canonicalEntry.CanonicalName;
                        propSchema = canonicalEntry.Schema;
                    }
                }
                else if (expectedProperties.TryGetValue(ToSnakeCase(rawKey), out var snakeEntry))
                {
                    canonicalKey = snakeEntry.CanonicalName;
                    propSchema = snakeEntry.Schema;
                }
                else if (expectedProperties.TryGetValue(ToCamelCase(rawKey), out var camelEntry))
                {
                    canonicalKey = camelEntry.CanonicalName;
                    propSchema = camelEntry.Schema;
                }
                else if (expectedProperties.TryGetValue(ToPascalCase(rawKey), out var pascalEntry))
                {
                    canonicalKey = pascalEntry.CanonicalName;
                    propSchema = pascalEntry.Schema;
                }

                if (!string.Equals(canonicalKey, rawKey, StringComparison.Ordinal))
                {
                    if (string.Equals(canonicalKey, rawKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ToSnakeCase(canonicalKey), ToSnakeCase(rawKey), StringComparison.OrdinalIgnoreCase))
                    {
                        Telemetry?.Record(LenientDispatchEventType.CasingNormalized, rawKey, canonicalKey, SessionId);
                    }
                    else
                    {
                        Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, rawKey, canonicalKey, SessionId);
                    }
                }

                var normalizedSubVal = NormalizeValueDeep(rawVal, propSchema, mapping, paramPath + "." + canonicalKey, rootSchema);
                if (_options.EnableParameterCoercion && normalizedSubVal is string && propSchema.HasValue)
                {
                    normalizedSubVal = CoerceParameterValue(canonicalKey, normalizedSubVal, propSchema, mapping);
                }
                normalizedDict[canonicalKey] = normalizedSubVal;
            }

            return normalizedDict;
        }

        return value;
    }

    private void PromoteFlattenedParametersToArrays(
        string canonicalTool,
        AIFunctionArguments canonicalArgs,
        Dictionary<string, (string CanonicalName, JsonElement Schema)> propDict,
        JsonElement? rootSchema,
        ToolMapping? mapping)
    {
        foreach (var (propKey, (canonicalParamName, paramSchema)) in propDict)
        {
            var resolvedSchema = rootSchema.HasValue ? ResolveSchema(paramSchema, rootSchema.Value) : paramSchema;
            if (!SchemaTypeMatches(resolvedSchema, "array"))
            {
                continue;
            }

            if (resolvedSchema.ValueKind != JsonValueKind.Object ||
                !resolvedSchema.TryGetProperty("items", out var itemsSchemaEl))
            {
                continue;
            }

            var resolvedItemsSchema = rootSchema.HasValue ? ResolveSchema(itemsSchemaEl, rootSchema.Value) : itemsSchemaEl;
            if (resolvedItemsSchema.ValueKind != JsonValueKind.Object ||
                !resolvedItemsSchema.TryGetProperty("properties", out var itemPropertiesEl) ||
                itemPropertiesEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Check if canonicalArgs already has canonicalParamName
            bool hasValidCollection = false;
            string? matchedKey = canonicalParamName;
            if (canonicalArgs.TryGetValue(canonicalParamName, out var existingVal) ||
                TryGetValueCaseInsensitive(canonicalArgs, canonicalParamName, out existingVal, out matchedKey))
            {
                if (existingVal is System.Collections.IEnumerable enumerable and not string and not IDictionary<string, object?> and not System.Collections.IDictionary)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is IDictionary<string, object?> d && d.Count > 0)
                        {
                            hasValidCollection = true;
                            break;
                        }
                        else if (item is JsonElement je && je.ValueKind == JsonValueKind.Object && je.EnumerateObject().Any())
                        {
                            hasValidCollection = true;
                            break;
                        }
                        else if (item != null && item is not IDictionary<string, object?> and not System.Collections.IDictionary)
                        {
                            hasValidCollection = true;
                            break;
                        }
                    }
                }
                else if (existingVal is IDictionary<string, object?> singleDict && singleDict.Count > 0)
                {
                    hasValidCollection = true;
                }
                else if (existingVal != null)
                {
                    hasValidCollection = true;
                }
            }

            if (hasValidCollection)
            {
                continue;
            }

            // Collect item properties and aliases
            var itemPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in itemPropertiesEl.EnumerateObject())
            {
                itemPropNames.Add(prop.Name);
                itemPropNames.Add(ToSnakeCase(prop.Name));
                itemPropNames.Add(ToCamelCase(prop.Name));
                itemPropNames.Add(ToPascalCase(prop.Name));
            }

            if (mapping?.NestedParameterAliases != null)
            {
                var nestedPath = canonicalParamName + "[]";
                if (mapping.NestedParameterAliases.TryGetValue(nestedPath, out var nestedAliases))
                {
                    foreach (var (canonProp, aliases) in nestedAliases)
                    {
                        itemPropNames.Add(canonProp);
                        foreach (var alias in aliases)
                        {
                            itemPropNames.Add(alias);
                        }
                    }
                }
            }

            // Do not steal top-level tool parameters
            var topLevelParams = new HashSet<string>(propDict.Keys, StringComparer.OrdinalIgnoreCase);
            topLevelParams.Remove(canonicalParamName);
            topLevelParams.Remove(propKey);

            var extractedItem = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var matchedRootKeys = new List<string>();

            foreach (var (argKey, argVal) in canonicalArgs)
            {
                if (topLevelParams.Contains(argKey))
                {
                    continue;
                }

                if (itemPropNames.Contains(argKey) && argVal != null)
                {
                    extractedItem[argKey] = argVal;
                    matchedRootKeys.Add(argKey);
                }
            }

            if (extractedItem.Count > 0)
            {
                foreach (var k in matchedRootKeys)
                {
                    canonicalArgs.Remove(k);
                }

                if (matchedKey != null && canonicalArgs.ContainsKey(matchedKey))
                {
                    canonicalArgs.Remove(matchedKey);
                }

                canonicalArgs[canonicalParamName] = new List<object?> { extractedItem };
                Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, "<flattened>", canonicalParamName, SessionId);
            }
        }
    }

    private void UnpackSingleItemArraysToFlatParameters(
        string canonicalTool,
        AIFunctionArguments canonicalArgs,
        Dictionary<string, (string CanonicalName, JsonElement Schema)> propDict,
        JsonElement? rootSchema)
    {
        var currentKeys = canonicalArgs.Keys.ToList();
        foreach (var argKey in currentKeys)
        {
            if (propDict.ContainsKey(argKey))
            {
                continue;
            }

            var val = canonicalArgs[argKey];
            if (val is string strVal)
            {
                var trimmed = strVal.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        val = ConvertJsonElementToClr(doc.RootElement);
                    }
                    catch (JsonException) { }
                }
            }

            IDictionary<string, object?>? firstItemDict = null;
            int itemCount = 0;

            if (val is System.Collections.IEnumerable list and not string and not IDictionary<string, object?> and not System.Collections.IDictionary)
            {
                foreach (var item in list)
                {
                    itemCount++;
                    if (itemCount > 1)
                    {
                        break;
                    }

                    if (item is IDictionary<string, object?> d)
                    {
                        firstItemDict = d;
                    }
                    else if (item is JsonElement je && je.ValueKind == JsonValueKind.Object)
                    {
                        if (ConvertJsonElementToClr(je) is IDictionary<string, object?> parsedDict)
                        {
                            firstItemDict = parsedDict;
                        }
                    }
                }
            }
            else if (val is IDictionary<string, object?> singleDict)
            {
                firstItemDict = singleDict;
                itemCount = 1;
            }

            if (itemCount == 1 && firstItemDict != null && firstItemDict.Count > 0)
            {
                bool unpackedAny = false;
                foreach (var (itemK, itemV) in firstItemDict)
                {
                    if (propDict.TryGetValue(itemK, out var entry) && !canonicalArgs.ContainsKey(entry.CanonicalName))
                    {
                        canonicalArgs[entry.CanonicalName] = itemV;
                        unpackedAny = true;
                    }
                }

                if (unpackedAny)
                {
                    canonicalArgs.Remove(argKey);
                    Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, argKey, "<unpacked>", SessionId);
                }
            }
        }
    }

    private void PropagateUnmappedParametersToCollections(
        AIFunctionArguments canonicalArgs,
        Dictionary<string, (string CanonicalName, JsonElement Schema)> propDict,
        JsonElement? rootSchema,
        ToolMapping? mapping)
    {
        var rootKeys = canonicalArgs.Keys.ToList();
        foreach (var rootKey in rootKeys)
        {
            if (!canonicalArgs.TryGetValue(rootKey, out var rootVal) || rootVal == null)
            {
                continue;
            }

            bool isExplicitPropagated = (mapping != null && mapping.PropagatedParameters.Contains(rootKey)) ||
                                        _options.PropagatedParameters.Contains(rootKey);
            bool isUnmapped = !propDict.ContainsKey(rootKey);

            if (!isExplicitPropagated && !isUnmapped)
            {
                continue;
            }

            bool propagatedToAny = false;
            var seenArrays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (canonicalParamName, paramSchema) in propDict.Values)
            {
                if (!seenArrays.Add(canonicalParamName))
                {
                    continue;
                }

                var resolvedSchema = rootSchema.HasValue ? ResolveSchema(paramSchema, rootSchema.Value) : paramSchema;
                if (!SchemaTypeMatches(resolvedSchema, "array"))
                {
                    continue;
                }

                if (!canonicalArgs.TryGetValue(canonicalParamName, out var arrayVal) || arrayVal == null)
                {
                    continue;
                }

                if (arrayVal is string strVal)
                {
                    var trimmed = strVal.Trim();
                    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(trimmed);
                            arrayVal = ConvertJsonElementToClr(doc.RootElement);
                            canonicalArgs[canonicalParamName] = arrayVal;
                        }
                        catch (JsonException) { }
                    }
                }

                if (arrayVal is not System.Collections.IEnumerable list ||
                    arrayVal is string or IDictionary<string, object?> or System.Collections.IDictionary)
                {
                    continue;
                }

                if (!resolvedSchema.TryGetProperty("items", out var itemsSchema))
                {
                    continue;
                }

                var resolvedItemSchema = rootSchema.HasValue ? ResolveSchema(itemsSchema, rootSchema.Value) : itemsSchema;
                if (resolvedItemSchema.ValueKind != JsonValueKind.Object ||
                    !resolvedItemSchema.TryGetProperty("properties", out var itemProps) ||
                    itemProps.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? targetItemPropName = null;
                foreach (var prop in itemProps.EnumerateObject())
                {
                    var canonProp = prop.Name;
                    if (string.Equals(canonProp, rootKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ToSnakeCase(canonProp), rootKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ToCamelCase(canonProp), rootKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ToPascalCase(canonProp), rootKey, StringComparison.OrdinalIgnoreCase))
                    {
                        targetItemPropName = canonProp;
                        break;
                    }
                }

                if (targetItemPropName == null && mapping?.NestedParameterAliases != null)
                {
                    var nestedPath = canonicalParamName + "[]";
                    if (mapping.NestedParameterAliases.TryGetValue(nestedPath, out var nestedAliases))
                    {
                        foreach (var (canonProp, aliases) in nestedAliases)
                        {
                            if (aliases.Contains(rootKey))
                            {
                                targetItemPropName = canonProp;
                                break;
                            }
                        }
                    }
                }

                if (targetItemPropName == null)
                {
                    continue;
                }

                var newItemsList = new List<object?>();
                bool collectionModified = false;

                foreach (var item in list)
                {
                    var effectiveItem = item;

                    if (effectiveItem is string strItem)
                    {
                        var primaryProp = FindPrimaryStringProperty(itemProps, resolvedItemSchema, mapping, canonicalParamName + "[]");
                        if (primaryProp != null)
                        {
                            effectiveItem = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                [primaryProp] = strItem
                            };
                            collectionModified = true;
                        }
                    }

                    if (effectiveItem is IDictionary<string, object?> itemDict)
                    {
                        bool alreadyHas = (itemDict.TryGetValue(targetItemPropName, out var v1) && v1 != null) ||
                                          (itemDict.TryGetValue(ToSnakeCase(targetItemPropName), out var v2) && v2 != null) ||
                                          (itemDict.TryGetValue(ToCamelCase(targetItemPropName), out var v3) && v3 != null) ||
                                          (itemDict.TryGetValue(ToPascalCase(targetItemPropName), out var v4) && v4 != null);

                        if (!alreadyHas && mapping?.NestedParameterAliases != null &&
                            mapping.NestedParameterAliases.TryGetValue(canonicalParamName + "[]", out var nestedAliases) &&
                            nestedAliases.TryGetValue(targetItemPropName, out var aliases))
                        {
                            foreach (var a in aliases)
                            {
                                if (itemDict.TryGetValue(a, out var va) && va != null)
                                {
                                    alreadyHas = true;
                                    break;
                                }
                            }
                        }

                        if (!alreadyHas)
                        {
                            var mutableDict = new Dictionary<string, object?>(itemDict, StringComparer.OrdinalIgnoreCase);
                            object? effectiveVal = rootVal;
                            if (_options.EnableParameterCoercion && rootVal is string && itemProps.TryGetProperty(targetItemPropName, out var targetPropSchema))
                            {
                                effectiveVal = CoerceParameterValue(targetItemPropName, rootVal, targetPropSchema, mapping) ?? rootVal;
                            }
                            mutableDict[targetItemPropName] = effectiveVal;
                            effectiveItem = mutableDict;
                            propagatedToAny = true;
                            collectionModified = true;
                        }
                    }

                    newItemsList.Add(effectiveItem);
                }

                if (collectionModified)
                {
                    canonicalArgs[canonicalParamName] = newItemsList;
                }
            }

            if (propagatedToAny && isUnmapped)
            {
                canonicalArgs.Remove(rootKey);
                Telemetry?.Record(LenientDispatchEventType.ParameterAliasResolved, rootKey, "<propagated_to_collection>", SessionId);
            }
        }
    }

    private string? FindPrimaryStringProperty(
        JsonElement propertiesElement,
        JsonElement? itemSchema,
        ToolMapping? mapping,
        string? paramPath)
    {
        // 1. Explicit configuration in ToolMapping
        if (mapping?.PrimaryCollectionProperties != null && !string.IsNullOrEmpty(paramPath))
        {
            if (mapping.PrimaryCollectionProperties.TryGetValue(paramPath, out var explicitProp) &&
                !string.IsNullOrWhiteSpace(explicitProp))
            {
                return explicitProp;
            }

            var trimmedPath = paramPath.EndsWith("[]", StringComparison.Ordinal)
                ? paramPath[..^2]
                : paramPath;

            if (mapping.PrimaryCollectionProperties.TryGetValue(trimmedPath, out var explicitPropTrimmed) &&
                !string.IsNullOrWhiteSpace(explicitPropTrimmed))
            {
                return explicitPropTrimmed;
            }
        }

        // 2. Auto-discovery from JSON schema 'required'
        if (itemSchema.HasValue && itemSchema.Value.ValueKind == JsonValueKind.Object &&
            itemSchema.Value.TryGetProperty("required", out var requiredEl) &&
            requiredEl.ValueKind == JsonValueKind.Array)
        {
            string? singleRequiredString = null;
            int requiredStringCount = 0;

            foreach (var reqItem in requiredEl.EnumerateArray())
            {
                if (reqItem.ValueKind == JsonValueKind.String)
                {
                    var reqPropName = reqItem.GetString();
                    if (!string.IsNullOrEmpty(reqPropName) &&
                        propertiesElement.TryGetProperty(reqPropName, out var propDef) &&
                        propDef.ValueKind == JsonValueKind.Object &&
                        (!propDef.TryGetProperty("type", out _) || SchemaTypeMatches(propDef, "string")))
                    {
                        singleRequiredString = reqPropName;
                        requiredStringCount++;
                    }
                }
            }

            if (requiredStringCount == 1)
            {
                return singleRequiredString;
            }
        }

        // 3. Auto-discovery from JSON schema unique string property
        string? singleStringProp = null;
        int stringPropCount = 0;
        foreach (var p in propertiesElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Object &&
                SchemaTypeMatches(p.Value, "string"))
            {
                singleStringProp = p.Name;
                stringPropCount++;
            }
        }

        if (stringPropCount == 1)
        {
            return singleStringProp;
        }

        // 4. Configurable fallback list
        if (_options.DefaultPrimaryStringProperties != null)
        {
            foreach (var wk in _options.DefaultPrimaryStringProperties)
            {
                if (propertiesElement.TryGetProperty(wk, out var prop) &&
                    prop.ValueKind == JsonValueKind.Object &&
                    (!prop.TryGetProperty("type", out _) || SchemaTypeMatches(prop, "string")))
                {
                    return wk;
                }
            }
        }

        return null;
    }

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement rootSchema, int depth = 0)
    {
        if (depth > 10 || schema.ValueKind != JsonValueKind.Object)
        {
            return schema;
        }

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var refStr = refProp.GetString();
            if (!string.IsNullOrEmpty(refStr) && refStr.StartsWith("#/") && rootSchema.ValueKind == JsonValueKind.Object)
            {
                var parts = refStr.Substring(2).Split('/');
                var current = rootSchema;
                bool found = true;
                foreach (var rawPart in parts)
                {
                    var part = rawPart.Replace("~1", "/").Replace("~0", "~");
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                    {
                        current = next;
                    }
                    else
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return ResolveSchema(current, rootSchema, depth + 1);
                }
            }
        }

        // Handle anyOf / oneOf for nullable schemas: select non-null schema
        if (schema.TryGetProperty("anyOf", out var anyOfProp) && anyOfProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in anyOfProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (SchemaTypeMatches(item, "null") && !SchemaTypeMatches(item, "object") && !SchemaTypeMatches(item, "array"))
                    {
                        continue;
                    }
                    var resolved = ResolveSchema(item, rootSchema, depth + 1);
                    if (resolved.ValueKind == JsonValueKind.Object)
                    {
                        return resolved;
                    }
                }
            }
        }

        if (schema.TryGetProperty("oneOf", out var oneOfProp) && oneOfProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in oneOfProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (SchemaTypeMatches(item, "null") && !SchemaTypeMatches(item, "object") && !SchemaTypeMatches(item, "array"))
                    {
                        continue;
                    }
                    var resolved = ResolveSchema(item, rootSchema, depth + 1);
                    if (resolved.ValueKind == JsonValueKind.Object)
                    {
                        return resolved;
                    }
                }
            }
        }

        return schema;
    }

    private static object? ConvertJsonElementToClr(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElementToClr(prop.Value);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElementToClr(item));
                }
                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    if (l >= int.MinValue && l <= int.MaxValue)
                    {
                        return (int)l;
                    }
                    return l;
                }
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return element.Clone();
        }
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }
            else if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && name[i - 1] != '-' && !char.IsWhiteSpace(name[i - 1]) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = false;
        bool isFirst = true;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                capitalizeNext = true;
            }
            else
            {
                if (isFirst)
                {
                    sb.Append(char.ToLowerInvariant(c));
                    isFirst = false;
                    capitalizeNext = false;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }

    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = true;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                capitalizeNext = true;
            }
            else
            {
                if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Snaps a candidate string value to the closest matching schema enum or const member using deterministic rules.
    /// If no unique candidate matches, returns the original value untouched.
    /// </summary>
    public string SnapEnumValue(string value, JsonElement schema)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (TryGetEnumOrConstMembers(schema, out var candidates))
        {
            return SnapEnumValueCore(value, candidates, Telemetry, SessionId);
        }
        return value;
    }

    /// <summary>
    /// Snaps a candidate string value to the closest matching member among candidate strings using deterministic rules.
    /// If no unique candidate matches, returns the original value untouched.
    /// </summary>
    public string SnapEnumValue(string value, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(candidates);
        var list = candidates as IReadOnlyList<string> ?? candidates.ToList();
        return SnapEnumValueCore(value, list, Telemetry, SessionId);
    }

    internal static string SnapEnumValueCore(
        string value,
        IReadOnlyList<string> candidates,
        LenientDispatchTelemetry? telemetry = null,
        string? sessionId = null)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return value;
        }

        // Exact match check (Ordinal) - already a valid member
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], value, StringComparison.Ordinal))
            {
                return value;
            }
        }

        // 1. Ordinal case-insensitive match
        List<string>? rule1Matches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], value, StringComparison.OrdinalIgnoreCase))
            {
                rule1Matches ??= new List<string>(2);
                rule1Matches.Add(candidates[i]);
            }
        }

        if (rule1Matches != null)
        {
            if (rule1Matches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule1Matches[0], sessionId);
                return rule1Matches[0];
            }
            // Tie among ordinal case-insensitive matches -> do not guess
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 2. Normalization (trim, collapse whitespace/underscore/hyphen, naive plural stripping)
        var normValue = NormalizeEnumString(value);
        var normValueNoPlural = StripPluralSuffix(normValue);
        var normValueNoSep = normValue.Replace("_", "");
        var normValueNoSepNoPlural = StripPluralSuffix(normValueNoSep);

        // 2a. Normalized exact match (collapsed whitespace/underscore/hyphen)
        List<string>? rule2aMatches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var normC = NormalizeEnumString(candidates[i]);
            if (string.Equals(normC, normValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normC.Replace("_", ""), normValueNoSep, StringComparison.OrdinalIgnoreCase))
            {
                rule2aMatches ??= new List<string>(2);
                rule2aMatches.Add(candidates[i]);
            }
        }

        if (rule2aMatches != null)
        {
            if (rule2aMatches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule2aMatches[0], sessionId);
                return rule2aMatches[0];
            }
            // Tie among normalized matches
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 2b. Naive plural stripping
        List<string>? rule2bMatches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var normC = NormalizeEnumString(candidates[i]);
            var normCNoPlural = StripPluralSuffix(normC);
            var normCNoSep = normC.Replace("_", "");
            var normCNoSepNoPlural = StripPluralSuffix(normCNoSep);

            if (string.Equals(normCNoPlural, normValueNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normCNoSepNoPlural, normValueNoSepNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normC, normValueNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normCNoPlural, normValue, StringComparison.OrdinalIgnoreCase))
            {
                rule2bMatches ??= new List<string>(2);
                rule2bMatches.Add(candidates[i]);
            }
        }

        if (rule2bMatches != null)
        {
            if (rule2bMatches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule2bMatches[0], sessionId);
                return rule2bMatches[0];
            }
            // Tie among plural-stripped matches
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 3. Unique prefix/suffix/substring containment (e.g. Stalingrad in Battle of Stalingrad) — only when exactly one member matches.
        var trimVal = value.Trim();
        if (trimVal.Length >= 2)
        {
            List<string>? rule3Matches = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                var trimCand = cand.Trim();
                var normCand = NormalizeEnumString(cand);

                bool contains = (trimCand.Length >= 2 && (ContainsTokenOrAffix(trimCand, trimVal) || ContainsTokenOrAffix(trimVal, trimCand))) ||
                                (normCand.Length >= 2 && normValue.Length >= 2 &&
                                 (ContainsTokenOrAffix(normCand, normValue) ||
                                  ContainsTokenOrAffix(normValue, normCand)));

                if (contains)
                {
                    rule3Matches ??= new List<string>(2);
                    rule3Matches.Add(cand);
                }
            }

            if (rule3Matches != null)
            {
                if (rule3Matches.Count == 1)
                {
                    telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule3Matches[0], sessionId);
                    return rule3Matches[0];
                }
                // Ambiguous tie among multiple containing members
                telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
                return value;
            }
        }

        // 4. Bounded edit distance (<= 2 and <= 25% of length) with a unique best candidate.
        int minDistance = int.MaxValue;
        List<string>? bestCandidates = null;
        var lowerVal = trimVal.ToLowerInvariant();

        for (int i = 0; i < candidates.Count; i++)
        {
            var cand = candidates[i];
            var lowerCand = cand.Trim().ToLowerInvariant();
            int maxLen = Math.Max(lowerVal.Length, lowerCand.Length);
            if (maxLen == 0) continue;

            int d = EditDistance(lowerVal, lowerCand, maxDistance: 2);
            var normC = NormalizeEnumString(cand);
            if (normC.Length > 0 && normValue.Length > 0)
            {
                int dNorm = EditDistance(normValue, normC, maxDistance: 2);
                d = Math.Min(d, dNorm);
                maxLen = Math.Max(maxLen, Math.Max(normValue.Length, normC.Length));
            }

            // Bounded: <= 2 and <= 25% of max length
            if (d <= 2 && (double)d / maxLen <= 0.25 + 1e-9)
            {
                if (d < minDistance)
                {
                    minDistance = d;
                    bestCandidates ??= new List<string>(2);
                    bestCandidates.Clear();
                    bestCandidates.Add(cand);
                }
                else if (d == minDistance)
                {
                    bestCandidates?.Add(cand);
                }
            }
        }

        if (bestCandidates != null)
        {
            if (bestCandidates.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, bestCandidates[0], sessionId);
                return bestCandidates[0];
            }
            // Tie among best candidates at minDistance
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 5. Otherwise leave the value untouched (never guess among ties) and record a telemetry event.
        telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
        return value;
    }

    internal static bool TryGetEnumOrConstMembers(JsonElement schema, out List<string> candidates)
    {
        candidates = [];
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (schema.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in enumProp.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (s != null && !candidates.Contains(s))
                    {
                        candidates.Add(s);
                    }
                }
            }
        }
        else if (schema.TryGetProperty("const", out var constProp) && constProp.ValueKind == JsonValueKind.String)
        {
            var s = constProp.GetString();
            if (s != null)
            {
                candidates.Add(s);
            }
        }
        else if (schema.TryGetProperty("oneOf", out var oneOfProp) && oneOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(oneOfProp, candidates);
        }
        else if (schema.TryGetProperty("anyOf", out var anyOfProp) && anyOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(anyOfProp, candidates);
        }
        else if (schema.TryGetProperty("allOf", out var allOfProp) && allOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(allOfProp, candidates);
        }

        return candidates.Count > 0;
    }

    private static void ExtractFromCompoundSchema(JsonElement arrayProp, List<string> candidates)
    {
        foreach (var item in arrayProp.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("const", out var cp) && cp.ValueKind == JsonValueKind.String)
                {
                    var s = cp.GetString();
                    if (s != null && !candidates.Contains(s)) candidates.Add(s);
                }
                else if (item.TryGetProperty("enum", out var ep) && ep.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in ep.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (s != null && !candidates.Contains(s)) candidates.Add(s);
                        }
                    }
                }
            }
        }
    }

    private static string NormalizeEnumString(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return ToSnakeCase(s.Trim());
    }

    private static string StripPluralSuffix(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length > 2 && s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !s.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            return s[..^1];
        }
        return s;
    }

    internal static bool ContainsTokenOrAffix(string container, string part)
    {
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(part)) return false;
        var c = container.Trim();
        var p = part.Trim();
        if (p.Length < 2 || c.Length < p.Length) return false;

        // Exact match
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;

        // Prefix with separator
        if (c.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
            c.Length > p.Length && (c[p.Length] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[p.Length])))
        {
            return true;
        }

        // Suffix with separator
        if (c.EndsWith(p, StringComparison.OrdinalIgnoreCase) &&
            c.Length > p.Length && (c[c.Length - p.Length - 1] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[c.Length - p.Length - 1])))
        {
            return true;
        }

        // Substring bounded by separators
        var idx = c.IndexOf(p, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool startBound = idx == 0 || c[idx - 1] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[idx - 1]);
            int endPos = idx + p.Length;
            bool endBound = endPos == c.Length || c[endPos] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[endPos]);

            if (startBound && endBound)
            {
                return true;
            }

            idx = c.IndexOf(p, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static int EditDistance(string s, string t, int maxDistance = int.MaxValue)
    {
        int n = s.Length;
        int m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        if (Math.Abs(n - m) > maxDistance)
        {
            return maxDistance + 1;
        }

        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
            d[i, 0] = i;
        for (int j = 0; j <= m; j++)
            d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            int rowMin = int.MaxValue;
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }

                if (d[i, j] < rowMin)
                {
                    rowMin = d[i, j];
                }
            }

            if (rowMin > maxDistance)
            {
                return maxDistance + 1;
            }
        }

        return d[n, m];
    }
}

/// <summary>
/// Delegating chat client middleware that normalizes <see cref="FunctionCallContent"/> items
/// using <see cref="LenientToolDispatcher"/> so that downstream <c>FunctionInvokingChatClient</c> resolves canonical tools.
/// </summary>
public class LenientToolDispatchChatClient : DelegatingChatClient
{
    private readonly LenientToolDispatcher _dispatcher;

    public LenientToolDispatchChatClient(IChatClient innerClient, LenientToolDispatcher dispatcher)
        : base(innerClient)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    private void RegisterRequestTools(ChatOptions? options)
    {
        if (options?.Tools is { Count: > 0 })
        {
            _dispatcher.RegisterTools(options.Tools.OfType<AIFunction>());
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.SetToolDispatcher(_dispatcher);
        if (options.ConversationId != null)
        {
            _dispatcher.SessionId = options.ConversationId;
        }
        RegisterRequestTools(options);
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        if (response?.Messages != null)
        {
            foreach (var msg in response.Messages)
            {
                NormalizeMessageToolCalls(msg);
            }
        }
        return response!;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.SetToolDispatcher(_dispatcher);
        if (options.ConversationId != null)
        {
            _dispatcher.SessionId = options.ConversationId;
        }
        RegisterRequestTools(options);
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            if (update.Contents != null)
            {
                for (int i = 0; i < update.Contents.Count; i++)
                {
                    if (update.Contents[i] is FunctionCallContent fcc)
                    {
                        if (string.IsNullOrWhiteSpace(fcc.Name))
                        {
                            continue;
                        }

                        try
                        {
                            var dispatched = _dispatcher.NormalizeCall(fcc);
                            update.Contents[i] = new FunctionCallContent(fcc.CallId, dispatched.CanonicalToolName, dispatched.Arguments);
                        }
                        catch (KeyNotFoundException)
                        {
                            // In streaming, arguments might not be fully assembled yet for translations.
                        }
                    }
                }
            }
            yield return update;
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(LenientToolDispatcher))
        {
            return _dispatcher;
        }

        if (serviceType == typeof(LenientDispatchTelemetry) && _dispatcher.Telemetry != null)
        {
            return _dispatcher.Telemetry;
        }

        return base.GetService(serviceType, serviceKey);
    }

    private void NormalizeMessageToolCalls(ChatMessage message)
    {
        if (message.Contents == null) return;

        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is FunctionCallContent fcc && !string.IsNullOrWhiteSpace(fcc.Name))
            {
                var dispatched = _dispatcher.NormalizeCall(fcc);
                message.Contents[i] = new FunctionCallContent(fcc.CallId, dispatched.CanonicalToolName, dispatched.Arguments);
            }
        }
    }
}

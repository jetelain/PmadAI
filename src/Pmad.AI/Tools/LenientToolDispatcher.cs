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
/// Alias and parameter mapping configuration for a single canonical tool.
/// </summary>
public class ToolMapping
{
    /// <summary>Alternative names by which the tool can be invoked.</summary>
    public HashSet<string> Aliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parameter aliases mapping canonical parameter names to acceptable alternative names.</summary>
    public Dictionary<string, HashSet<string>> ParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ToolMapping() { }

    public ToolMapping(
        IEnumerable<string>? aliases = null,
        IDictionary<string, IEnumerable<string>>? parameterAliases = null)
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
    private ToolDispatcherConfiguration _configuration = new();
    private readonly Dictionary<string, AIFunction> _registeredTools = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TemplatePlaceholderRegex = new(@"\{([a-zA-Z0-9_]+)(?::([^}]*))?\}", RegexOptions.Compiled);

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
        _configuration = configuration;
    }

    /// <summary>
    /// Registers an executable <see cref="AIFunction"/> canonical tool.
    /// </summary>
    public void RegisterTool(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _registeredTools[tool.Name] = tool;
    }

    /// <summary>
    /// Registers a collection of executable <see cref="AIFunction"/> canonical tools.
    /// </summary>
    public void RegisterTools(IEnumerable<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var t in tools)
        {
            RegisterTool(t);
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
            toolName = StripNamespace(toolName);
        }

        // 1. Check if tool is a translation rule
        var translation = _configuration.Translations.FirstOrDefault(t =>
            string.Equals(t.SourceTool, toolName, StringComparison.OrdinalIgnoreCase));

        if (translation != null)
        {
            return RenderTranslationCall(translation, rawArgs, rawToolName);
        }

        // 2. Resolve canonical tool name (exact match always wins over aliases)
        string canonicalTool = toolName;
        ToolMapping? mapping = null;

        if (_registeredTools.ContainsKey(toolName))
        {
            canonicalTool = toolName;
            _configuration.Tools.TryGetValue(toolName, out mapping);
        }
        else if (_configuration.Tools.TryGetValue(toolName, out mapping))
        {
            canonicalTool = toolName;
        }
        else
        {
            // Search aliases
            foreach (var (canonicalKey, toolMap) in _configuration.Tools)
            {
                if (toolMap.Aliases.Contains(toolName))
                {
                    canonicalTool = canonicalKey;
                    mapping = toolMap;
                    break;
                }
            }
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

                // Check aliases
                foreach (var alias in aliases)
                {
                    if (rawArgs.TryGetValue(alias, out var aliasVal))
                    {
                        canonicalArgs[canonicalParam] = aliasVal;
                        matchedRawKeys.Add(alias);
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
        if (dict.TryGetValue(key, out value)) return true;
        foreach (var (k, v) in dict)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }
        value = null;
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

        // 1. Check if tool is a translation rule
        var translation = _configuration.Translations.FirstOrDefault(t =>
            string.Equals(t.SourceTool, toolName, StringComparison.OrdinalIgnoreCase));

        if (translation != null)
        {
            return translation.TargetTool;
        }

        // 2. Resolve canonical tool name (exact match always wins over aliases)
        if (_registeredTools.ContainsKey(toolName))
        {
            return toolName;
        }

        if (_configuration.Tools.ContainsKey(toolName))
        {
            return toolName;
        }

        // Search aliases
        foreach (var (canonicalKey, toolMap) in _configuration.Tools)
        {
            if (toolMap.Aliases.Contains(toolName))
            {
                return canonicalKey;
            }
        }

        return toolName;
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
        JsonElement schemaProperties = default;
        bool hasSchema = false;

        if (_registeredTools.TryGetValue(canonicalToolName, out var targetFn) &&
            targetFn.JsonSchema.ValueKind == JsonValueKind.Object &&
            targetFn.JsonSchema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            schemaProperties = props;
            hasSchema = true;
        }

        foreach (var key in arguments.Keys.ToList())
        {
            var value = arguments[key];
            if (value == null) continue;

            JsonElement paramSchema = default;
            bool paramSchemaFound = false;

            if (hasSchema)
            {
                if (schemaProperties.TryGetProperty(key, out paramSchema))
                {
                    paramSchemaFound = true;
                }
                else
                {
                    foreach (var prop in schemaProperties.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            paramSchema = prop.Value;
                            paramSchemaFound = true;
                            break;
                        }
                    }
                }
            }

            arguments[key] = CoerceParameterValue(key, value, paramSchemaFound ? paramSchema : null);
        }
    }

    private static object? CoerceParameterValue(string paramName, object value, JsonElement? schema)
    {
        if (value is not string strVal)
        {
            return value;
        }

        var trimmed = strVal.Trim();

        // 1. Schema-based Enum matching (case-insensitive)
        if (schema.HasValue && schema.Value.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var enumEl in enumProp.EnumerateArray())
            {
                if (enumEl.ValueKind == JsonValueKind.String)
                {
                    var enumStr = enumEl.GetString();
                    if (string.Equals(enumStr, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return enumStr;
                    }
                }
                else if (enumEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
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

        // 2. Boolean coercion (schema or heuristic)
        bool isBooleanTarget = schema.HasValue &&
            schema.Value.TryGetProperty("type", out var typeProp) &&
            typeProp.ValueEquals("boolean");

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
        bool isIntegerTarget = schema.HasValue &&
            schema.Value.TryGetProperty("type", out var numType) &&
            numType.ValueEquals("integer");

        bool isNumberTarget = schema.HasValue &&
            schema.Value.TryGetProperty("type", out var floatType) &&
            floatType.ValueEquals("number");

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
        bool isArrayTarget = schema.HasValue &&
            schema.Value.TryGetProperty("type", out var arrType) &&
            arrType.ValueEquals("array");

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

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.SetToolDispatcher(_dispatcher);
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

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

    private static string StripNamespace(string name)
    {
        var idx = name.LastIndexOf("__", StringComparison.Ordinal);
        if (idx >= 0) return name.Substring(idx + 2);

        idx = name.LastIndexOf(':');
        if (idx >= 0) return name.Substring(idx + 1);

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

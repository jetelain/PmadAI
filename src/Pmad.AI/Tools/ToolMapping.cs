using Pmad.AI.Common;

namespace Pmad.AI.Tools;

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

    /// <summary>
    /// Parameter aliases as a flat alias-to-canonical map (each alias, and the canonical name itself,
    /// maps to the canonical parameter name). Enables O(1) resolution of any spelling a model may emit.
    /// </summary>
    public Dictionary<string, string> ParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computed parameter factories evaluated when canonical parameters are missing.
    /// Allows multi-parameter synthesis (e.g. limit = end_line - start_line + 1).
    /// </summary>
    public Dictionary<string, ParameterComputer> ComputedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit nested property alias mappings (e.g. "edits[]" -> { "old_text" -> "oldText", "search" -> "oldText" }).
    /// Each path maps to a flat alias-to-canonical dictionary.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> NestedParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit parameter value aliases mapping parameter paths to a flat alias-to-canonical value dictionary
    /// (e.g. "species" -> { "wild tiger" -> "wild tiger", "tiger" -> "wild tiger", "tigers" -> "wild tiger" }
    /// or "resource_type" -> { "MemoryMb" -> "MemoryMb", "memory" -> "MemoryMb", "ram" -> "MemoryMb" }).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ParameterValueAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit list of root-level parameters that should be propagated to nested collection items.
    /// </summary>
    public HashSet<string> PropagatedParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit mappings of collection parameter paths (e.g. "questions[]" or "questions")
    /// to the primary string property name used when wrapping raw strings into objects.
    /// </summary>
    public Dictionary<string, string> PrimaryCollectionProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Declarative rules for computing limit/count parameters from start and end parameters (e.g. offset + limit from start_line + end_line).
    /// </summary>
    public List<RangeToLimitRule> RangeToLimitRules { get; init; } = [];

    /// <summary>
    /// Explicit list of top-level parameter names or aliases that should be promoted into collection items
    /// (e.g. "edits" -> ["old_text", "new_text", "replace_all"]), even if declared in the top-level schema.
    /// </summary>
    public Dictionary<string, HashSet<string>> PromotedCollectionParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parameters or nested property paths whose explicit null value should be coerced to string.Empty ("").
    /// Supports top-level names (e.g. "new_text") and nested paths (e.g. "edits[].newText").
    /// </summary>
    public HashSet<string> NullAsEmptyStringParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ToolMapping() { }

    public ToolMapping(
        IEnumerable<string>? aliases = null,
        IDictionary<string, IEnumerable<string>>? parameterAliases = null,
        IDictionary<string, ParameterComputer>? computedParameters = null,
        IDictionary<string, IDictionary<string, IEnumerable<string>>>? nestedParameterAliases = null,
        IDictionary<string, IDictionary<string, IEnumerable<string>>>? parameterValueAliases = null,
        IEnumerable<string>? propagatedParameters = null,
        IDictionary<string, string>? primaryCollectionProperties = null,
        IEnumerable<RangeToLimitRule>? rangeToLimitRules = null,
        IDictionary<string, IEnumerable<string>>? promotedCollectionParameters = null,
        IEnumerable<string>? nullAsEmptyStringParameters = null)
    {
        if (aliases != null)
        {
            foreach (var alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    Aliases.Add(alias.Trim());
                }
            }
        }

        if (parameterAliases != null)
        {
            ParameterAliases = ToFlatAliasMap(parameterAliases);
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
                NestedParameterAliases[path] = ToFlatAliasMap(propDict);
            }
        }

        if (parameterValueAliases != null)
        {
            foreach (var (path, valDict) in parameterValueAliases)
            {
                ParameterValueAliases[path] = ToFlatAliasMap(valDict);
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

        if (rangeToLimitRules != null)
        {
            RangeToLimitRules.AddRange(rangeToLimitRules);
        }

        if (promotedCollectionParameters != null)
        {
            foreach (var (k, v) in promotedCollectionParameters)
            {
                PromotedCollectionParameters[k] = new HashSet<string>(v, StringComparer.OrdinalIgnoreCase);
            }
        }

        if (nullAsEmptyStringParameters != null)
        {
            foreach (var p in nullAsEmptyStringParameters)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    NullAsEmptyStringParameters.Add(p.Trim());
                }
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of this mapping so that mutations to the clone (or to the original)
    /// do not affect the other. Parameter computer delegates are shared by reference.
    /// </summary>
    public ToolMapping Clone()
    {
        return new ToolMapping
        {
            Aliases = new HashSet<string>(Aliases, StringComparer.OrdinalIgnoreCase),
            ParameterAliases = new Dictionary<string, string>(ParameterAliases, StringComparer.OrdinalIgnoreCase),
            ComputedParameters = new Dictionary<string, ParameterComputer>(ComputedParameters, StringComparer.OrdinalIgnoreCase),
            NestedParameterAliases = CloneNestedDictionaries(NestedParameterAliases),
            ParameterValueAliases = CloneNestedDictionaries(ParameterValueAliases),
            PropagatedParameters = new HashSet<string>(PropagatedParameters, StringComparer.OrdinalIgnoreCase),
            PrimaryCollectionProperties = new Dictionary<string, string>(PrimaryCollectionProperties, StringComparer.OrdinalIgnoreCase),
            RangeToLimitRules = RangeToLimitRules.Select(r => new RangeToLimitRule
            {
                StartParameter = r.StartParameter,
                CountParameter = r.CountParameter,
                EndParameterAliases = new HashSet<string>(r.EndParameterAliases, StringComparer.OrdinalIgnoreCase),
                DefaultStart = r.DefaultStart,
                Inclusive = r.Inclusive
            }).ToList(),
            PromotedCollectionParameters = PromotedCollectionParameters.ToDictionary(
                kvp => kvp.Key,
                kvp => new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase),
            NullAsEmptyStringParameters = new HashSet<string>(NullAsEmptyStringParameters, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Configures declarative range-to-limit pagination computation (e.g. converting start_line + end_line to offset + limit).
    /// </summary>
    public ToolMapping MapEndLineToLimit(
        string startParameter,
        string limitParameter,
        IEnumerable<string>? endAliases = null,
        int defaultStart = 1,
        bool inclusive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(limitParameter);

        var rule = new RangeToLimitRule
        {
            StartParameter = startParameter.Trim(),
            CountParameter = limitParameter.Trim(),
            DefaultStart = defaultStart,
            Inclusive = inclusive
        };
        if (endAliases != null)
        {
            foreach (var alias in endAliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    rule.EndParameterAliases.Add(alias.Trim());
                }
            }
        }
        RangeToLimitRules.Add(rule);
        return this;
    }

    /// <summary>
    /// Configures top-level parameter names or aliases that should be promoted into collection items
    /// (e.g. "edits" -> ["old_text", "new_text", "replace_all"]).
    /// </summary>
    public ToolMapping AddPromotedCollectionParameter(string collectionParam, params string[] shortcutParams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionParam);
        var targetKey = collectionParam.Trim();
        if (!PromotedCollectionParameters.TryGetValue(targetKey, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PromotedCollectionParameters[targetKey] = set;
        }

        foreach (var p in shortcutParams)
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                var trimmed = p.Trim();
                set.Add(trimmed);
                set.Add(StringCaseConverter.ToSnakeCase(trimmed));
                set.Add(StringCaseConverter.ToCamelCase(trimmed));
                set.Add(StringCaseConverter.ToPascalCase(trimmed));
            }
        }
        return this;
    }

    /// <summary>
    /// Configures parameters or nested property paths whose explicit null value should be coerced to string.Empty ("").
    /// </summary>
    public ToolMapping AddNullAsEmptyStringParameter(params string[] parameters)
    {
        foreach (var p in parameters)
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                var trimmed = p.Trim();
                NullAsEmptyStringParameters.Add(trimmed);
                NullAsEmptyStringParameters.Add(StringCaseConverter.ToSnakeCase(trimmed));
                NullAsEmptyStringParameters.Add(StringCaseConverter.ToCamelCase(trimmed));
                NullAsEmptyStringParameters.Add(StringCaseConverter.ToPascalCase(trimmed));
            }
        }
        return this;
    }

    /// <summary>
    /// Determines whether an explicit null value for the specified parameter path and property name
    /// should be coerced to string.Empty ("").
    /// </summary>
    public bool ShouldCoerceNullToEmptyString(string paramPath, string propName)
    {
        if (NullAsEmptyStringParameters == null || NullAsEmptyStringParameters.Count == 0 || string.IsNullOrWhiteSpace(propName))
        {
            return false;
        }

        var prop = propName.Trim();
        var snakeProp = StringCaseConverter.ToSnakeCase(prop);
        var camelProp = StringCaseConverter.ToCamelCase(prop);
        var pascalProp = StringCaseConverter.ToPascalCase(prop);

        // Check bare property names
        if (NullAsEmptyStringParameters.Contains(prop) ||
            NullAsEmptyStringParameters.Contains(snakeProp) ||
            NullAsEmptyStringParameters.Contains(camelProp) ||
            NullAsEmptyStringParameters.Contains(pascalProp))
        {
            return true;
        }

        // Check path + prop (e.g. "edits[].newText", "edits.newText")
        if (!string.IsNullOrWhiteSpace(paramPath))
        {
            var p = paramPath.Trim();
            var candidates = new[]
            {
                $"{p}.{prop}",
                $"{p}.{snakeProp}",
                $"{p}.{camelProp}",
                $"{p}.{pascalProp}",
                $"{p.TrimEnd('[', ']')}.{prop}",
                $"{p.TrimEnd('[', ']')}.{snakeProp}",
                $"{p.TrimEnd('[', ']')}.{camelProp}",
                $"{p.TrimEnd('[', ']')}.{pascalProp}",
                $"{p.TrimEnd('[', ']')}[].{prop}",
                $"{p.TrimEnd('[', ']')}[].{snakeProp}",
                $"{p.TrimEnd('[', ']')}[].{camelProp}",
                $"{p.TrimEnd('[', ']')}[].{pascalProp}"
            };

            foreach (var cand in candidates)
            {
                if (NullAsEmptyStringParameters.Contains(cand))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, Dictionary<string, string>> CloneNestedDictionaries(
        Dictionary<string, Dictionary<string, string>> source)
    {
        var copy = new Dictionary<string, Dictionary<string, string>>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, inner) in source)
        {
            copy[key] = new Dictionary<string, string>(inner, StringComparer.OrdinalIgnoreCase);
        }

        return copy;
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
    /// Registers alias names for a nested property of a collection item (e.g. mapping "optionsJson" -> "options").
    /// Each alias (plus its casing variants) maps to the canonical name, so resolution works regardless
    /// of the spelling the model emits or the schema declares. The canonical -> canonical self-entry is
    /// an internal convention added by the dispatcher at configure time.
    /// </summary>
    public ToolMapping AddNestedParameterAlias(string paramPath, string canonicalName, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        var path = paramPath.Trim();
        var canonical = canonicalName.Trim();
        if (!NestedParameterAliases.TryGetValue(path, out var inner))
        {
            inner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            NestedParameterAliases[path] = inner;
        }

        void Add(string name, string target)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var trimmed = name.Trim();
            inner[trimmed] = target;
            inner[StringCaseConverter.ToSnakeCase(trimmed)] = target;
            inner[StringCaseConverter.ToCamelCase(trimmed)] = target;
            inner[StringCaseConverter.ToPascalCase(trimmed)] = target;
        }

        // Canonical name (and its casing variants) maps to itself: this self-entry is added
        // automatically, callers never have to declare it.
        Add(canonical, canonical);

        // Every alias (and its casing variants) maps to the canonical name.
        foreach (var alias in aliases)
        {
            Add(alias, canonical);
        }

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
            valDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ParameterValueAliases[paramPath] = valDict;
        }

        var canonical = canonicalValue.Trim();

        // Canonical value maps to itself: this self-entry is added automatically,
        // callers never have to declare it.
        valDict[canonical] = canonical;

        foreach (var a in aliases)
        {
            if (!string.IsNullOrWhiteSpace(a))
            {
                valDict[a.Trim()] = canonical;
            }
        }

        return this;
    }

    /// <summary>
    /// Discovers and registers <see cref="ToolValueAliasAttribute"/> aliases defined on enum members of <typeparamref name="TEnum"/>.
    /// This method relies on runtime reflection over enum fields and custom attributes; in Native AOT /
    /// trimmed deployments prefer the source-generated tool configuration path (see
    /// <c>Pmad.AI.SourceGenerators</c>), which is reflection-free.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Uses reflection (GetFields and Attribute.GetCustomAttribute) to discover [ToolValueAlias] members of TEnum; " +
        "use the source-generated tool configuration in AOT/trimmed scenarios.")]
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
        if (!ParameterValueAliases.TryGetValue(path, out var valMap) &&
            !ParameterValueAliases.TryGetValue(StringCaseConverter.ToSnakeCase(path), out valMap) &&
            !ParameterValueAliases.TryGetValue(StringCaseConverter.ToCamelCase(path), out valMap) &&
            !ParameterValueAliases.TryGetValue(StringCaseConverter.ToPascalCase(path), out valMap))
        {
            return false;
        }

        // 1. Direct or registered alias match (case-insensitive)
        if (valMap.TryGetValue(rawValue, out var target))
        {
            canonicalValue = target;
            return true;
        }

        // 2. Plural / singular heuristic (e.g. "wild tigers" -> "wild tiger" or "tigers" -> "tiger")
        var variant = rawValue.EndsWith("s", StringComparison.OrdinalIgnoreCase) && rawValue.Length > 1
            ? rawValue.Substring(0, rawValue.Length - 1)
            : rawValue + "s";
        if (valMap.TryGetValue(variant, out target))
        {
            canonicalValue = target;
            return true;
        }

        // 3. Snake_case normalization against every registered key
        // (e.g. raw "memory_mb" matching registered "MemoryMb", or plural "wild_tigers" matching "wild tiger")
        var snakeRaw = StringCaseConverter.ToSnakeCase(rawValue);
        var snakeVariant = StringCaseConverter.ToSnakeCase(variant);
        foreach (var (key, value) in valMap)
        {
            if (string.Equals(StringCaseConverter.ToSnakeCase(key), snakeRaw, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(StringCaseConverter.ToSnakeCase(key), snakeVariant, StringComparison.OrdinalIgnoreCase))
            {
                canonicalValue = value;
                return true;
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
        if (NestedParameterAliases.TryGetValue(path, out var aliasMap))
        {
            if (aliasMap.TryGetValue(aliasOrKey, out var canonical))
            {
                canonicalName = canonical;
                return true;
            }

            // Casing-variant fallback (e.g. schema declares "OldText" but the map holds "oldText").
            var snakeKey = StringCaseConverter.ToSnakeCase(aliasOrKey);
            if (aliasMap.TryGetValue(snakeKey, out canonical))
            {
                canonicalName = canonical;
                return true;
            }
        }
        canonicalName = aliasOrKey;
        return false;
    }

    /// <summary>
    /// Converts a canonical-to-alias collection (as supplied by callers) into the flat alias-to-canonical
    /// representation: the canonical name automatically maps to itself and every alias maps to it.
    /// </summary>
    private static Dictionary<string, string> ToFlatAliasMap(IDictionary<string, IEnumerable<string>> canonicalToAliases)
    {
        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, aliases) in canonicalToAliases)
        {
            if (string.IsNullOrWhiteSpace(canonical))
            {
                continue;
            }

            var trimmed = canonical.Trim();
            flat[trimmed] = trimmed;
            if (aliases != null)
            {
                foreach (var alias in aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        flat[alias.Trim()] = trimmed;
                    }
                }
            }
        }
        return flat;
    }

    /// <summary>
    /// Configures aliases for a top-level parameter name.
    /// </summary>
    public ToolMapping AddParameterAliases(string paramName, IEnumerable<string> aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramName);
        ArgumentNullException.ThrowIfNull(aliases);

        var trimmedParamName = paramName.Trim();
        if (!ParameterAliases.TryGetValue(trimmedParamName, out var canonicalParamName))
        {
            canonicalParamName = trimmedParamName;
            ParameterAliases[canonicalParamName] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToSnakeCase(canonicalParamName)] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToCamelCase(canonicalParamName)] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToPascalCase(canonicalParamName)] = canonicalParamName;
        }
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }
            var trimmedAlias = alias.Trim();
            ParameterAliases[trimmedAlias] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToSnakeCase(trimmedAlias)] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToCamelCase(trimmedAlias)] = canonicalParamName;
            ParameterAliases[StringCaseConverter.ToPascalCase(trimmedAlias)] = canonicalParamName;
        }
        return this;
    }

    /// <summary>
    /// Configures aliases for a top-level parameter name.
    /// </summary>
    public ToolMapping AddParameterAliases(string paramName, params string[] aliases)
    {
        return AddParameterAliases(paramName, (IEnumerable<string>)aliases);
    }
}

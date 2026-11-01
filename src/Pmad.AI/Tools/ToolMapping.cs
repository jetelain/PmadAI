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
    /// Registers alias names for a nested property of a collection item (e.g. mapping "optionsJson" -> "options").
    /// The alias is registered in both directions so it can be resolved regardless of which spelling the model emits.
    /// </summary>
    public ToolMapping AddNestedParameterAlias(string paramPath, string canonicalName, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        var path = paramPath.Trim();
        if (!NestedParameterAliases.TryGetValue(path, out var inner))
        {
            inner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            NestedParameterAliases[path] = inner;
        }

        void AddSet(string key, IEnumerable<string> names)
        {
            if (!inner.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                inner[key] = set;
            }
            foreach (var n in names)
            {
                if (!string.IsNullOrWhiteSpace(n))
                {
                    set.Add(n.Trim());
                }
            }
        }

        AddSet(canonicalName, aliases);
        AddSet(StringCaseConverter.ToSnakeCase(canonicalName), aliases);
        AddSet(StringCaseConverter.ToCamelCase(canonicalName), aliases);
        AddSet(StringCaseConverter.ToPascalCase(canonicalName), aliases);

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
            var snakePath = StringCaseConverter.ToSnakeCase(path);
            if (!ParameterValueAliases.TryGetValue(snakePath, out valMap))
            {
                var camelPath = StringCaseConverter.ToCamelCase(path);
                if (!ParameterValueAliases.TryGetValue(camelPath, out valMap))
                {
                    var pascalPath = StringCaseConverter.ToPascalCase(path);
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
        var snakeRaw = StringCaseConverter.ToSnakeCase(rawValue);
        foreach (var (canonical, aliases) in valMap)
        {
            if (string.Equals(StringCaseConverter.ToSnakeCase(canonical), snakeRaw, StringComparison.OrdinalIgnoreCase))
            {
                canonicalValue = canonical;
                return true;
            }
            foreach (var alias in aliases)
            {
                if (string.Equals(StringCaseConverter.ToSnakeCase(alias), snakeRaw, StringComparison.OrdinalIgnoreCase))
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
            var snakeSingular = StringCaseConverter.ToSnakeCase(singular);
            foreach (var (canonical, aliases) in valMap)
            {
                if (string.Equals(canonical, singular, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(StringCaseConverter.ToSnakeCase(canonical), snakeSingular, StringComparison.OrdinalIgnoreCase) ||
                    aliases.Contains(singular))
                {
                    canonicalValue = canonical;
                    return true;
                }
                foreach (var alias in aliases)
                {
                    if (string.Equals(StringCaseConverter.ToSnakeCase(alias), snakeSingular, StringComparison.OrdinalIgnoreCase))
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
            var snakePlural = StringCaseConverter.ToSnakeCase(plural);
            foreach (var (canonical, aliases) in valMap)
            {
                if (string.Equals(canonical, plural, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(StringCaseConverter.ToSnakeCase(canonical), snakePlural, StringComparison.OrdinalIgnoreCase) ||
                    aliases.Contains(plural))
                {
                    canonicalValue = canonical;
                    return true;
                }
                foreach (var alias in aliases)
                {
                    if (string.Equals(StringCaseConverter.ToSnakeCase(alias), snakePlural, StringComparison.OrdinalIgnoreCase))
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

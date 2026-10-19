namespace Pmad.AI.Tools;

/// <summary>
/// Declares tool aliases for a method or lambda expression.
/// Consumed at compile time by the Pmad.AI.SourceGenerators package to generate a static
/// <see cref="ToolDispatcherConfiguration"/> factory.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ToolAliasAttribute : Attribute
{
    /// <summary>Alternative names for the tool.</summary>
    public string[] Aliases { get; }

    /// <summary>
    /// Explicit canonical tool name matching the registered AIFunction.Name.
    /// If null or omitted, defaults to the method name with a trailing "Async" stripped.
    /// </summary>
    public string? Name { get; set; }

    public ToolAliasAttribute(params string[] aliases)
    {
        Aliases = aliases;
    }
}

/// <summary>
/// Declares parameter or nested property aliases.
/// Consumed at compile time by the Pmad.AI.SourceGenerators package.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class ToolParameterAliasAttribute(params string[] aliases) : Attribute
{
    public string[] Aliases => aliases;
}

/// <summary>
/// Declares a shell translation rule mapping a virtual tool to an executable shell command template.
/// Consumed at compile time by the Pmad.AI.SourceGenerators package.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ToolTranslationAttribute(string sourceToolName, string commandTemplate) : Attribute
{
    public string SourceToolName => sourceToolName;
    public string CommandTemplate => commandTemplate;
    public ParameterEscapeMethod EscapeMethod { get; set; } = ParameterEscapeMethod.Bash;
}

/// <summary>
/// Specifies alternative names, aliases, or common SLM misnomers for an enum member or property value.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class ToolValueAliasAttribute : Attribute
{
    public string[] Aliases { get; }

    public ToolValueAliasAttribute(params string[] aliases)
    {
        Aliases = aliases ?? [];
    }
}

/// <summary>
/// Maps argument value aliases (synonyms, casing variations, plurals) to canonical values.
/// Can be applied to a tool method, parameter, or property.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ToolParameterValueAliasAttribute : Attribute
{
    public string ParameterName { get; }
    public string CanonicalValue { get; }
    public string[] Aliases { get; }

    public ToolParameterValueAliasAttribute(string parameterName, string canonicalValue, params string[] aliases)
    {
        ParameterName = parameterName ?? string.Empty;
        CanonicalValue = canonicalValue ?? string.Empty;
        Aliases = aliases ?? [];
    }

    public ToolParameterValueAliasAttribute(string canonicalValue, params string[] aliases)
        : this(string.Empty, canonicalValue, aliases)
    {
    }
}

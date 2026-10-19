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

namespace Pmad.AI.Tools;

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

    internal ToolDispatcherConfigurationSnapshot Snapshot()
    {
        return new ToolDispatcherConfigurationSnapshot(Tools, Translations);
    }
}

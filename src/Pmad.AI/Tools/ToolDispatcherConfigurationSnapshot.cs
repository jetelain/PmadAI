using System.Diagnostics.CodeAnalysis;

namespace Pmad.AI.Tools;

internal class ToolDispatcherConfigurationSnapshot
{
    public Dictionary<string, ToolMapping> Tools { get; }

    public List<ToolTranslation> Translations { get; }

    private readonly Dictionary<string, string> _aliasToCanonical = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ToolTranslation> _translationsBySource = new(StringComparer.OrdinalIgnoreCase);

    public ToolDispatcherConfigurationSnapshot(Dictionary<string, ToolMapping> tools, List<ToolTranslation> translations)
    {
        // Clone each ToolMapping so the snapshot is a safe, immutable-in-practice copy:
        // later mutations of the live configuration cannot be observed through the snapshot.
        Tools = new Dictionary<string, ToolMapping>(tools.Count, tools.Comparer);
        foreach (var (canonical, mapping) in tools)
        {
            Tools[canonical] = mapping.Clone();
        }

        Translations = translations.ToList();
        BuildLookupTables();
    }

    public bool TryGetAlias(string toolName, [MaybeNullWhen(false)] out string alias)
    {
        return _aliasToCanonical.TryGetValue(toolName, out alias);
    }

    public bool TryGetTranslation(string toolName, [MaybeNullWhen(false)] out ToolTranslation translation)
    {
        return _translationsBySource.TryGetValue(toolName, out translation);
    }

    private void BuildLookupTables()
    {
        if (Translations != null)
        {
            foreach (var t in Translations)
            {
                if (!string.IsNullOrWhiteSpace(t.SourceTool))
                {
                    _translationsBySource[t.SourceTool] = t;
                }
            }
        }

        if (Tools != null)
        {
            foreach (var (canonical, mapping) in Tools)
            {
                // The flat alias -> canonical maps are an internal convention owned by the
                // dispatcher: the canonical name maps to itself, every alias maps to it.
                // Self-entries are added here so callers never have to set them up.
                _aliasToCanonical[canonical] = canonical;
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

                NormalizeParameterAliasMap(mapping.ParameterAliases);
                if (mapping.NestedParameterAliases != null)
                {
                    foreach (var inner in mapping.NestedParameterAliases.Values)
                    {
                        NormalizeParameterAliasMap(inner);
                    }
                }
                if (mapping.ParameterValueAliases != null)
                {
                    foreach (var inner in mapping.ParameterValueAliases.Values)
                    {
                        NormalizeParameterAliasMap(inner);
                    }
                }
            }
        }
    }

    private static void NormalizeParameterAliasMap(Dictionary<string, string> flatAliasMap)
    {
        // Enforce the internal "canonical -> canonical" convention: every target value that is
        // not itself a key must self-map, so callers never have to declare self-entries.
        foreach (var target in flatAliasMap.Values.ToArray())
        {
            flatAliasMap.TryAdd(target, target);
        }
    }

}
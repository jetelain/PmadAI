using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;

using static Pmad.AI.Common.StringCaseConverter;
using static Pmad.AI.Tools.SchemaHelpers;

namespace Pmad.AI.Tools;

internal class RegistredToolsMetadata
{
    private readonly Dictionary<string, AIFunction> _registeredTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _toolRootSchemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (string CanonicalName, JsonElement Schema)>> _toolSchemaProperties = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _registeredTools.Count;

    public RegistredToolsMetadata(IEnumerable<AIFunction>? aIFunctions)
    {
        if (aIFunctions != null)
        {
            foreach (var aIFunction in aIFunctions)
            {
                RegisterToolInternal(aIFunction);
            }
        }
    }

    private RegistredToolsMetadata(RegistredToolsMetadata baseline, IEnumerable<AIFunction>? aIFunctions)
    {
        // Copy with the original comparer: LINQ ToDictionary() would fall back to the
        // default (ordinal) comparer and silently break case-insensitive lookups.
        _registeredTools = new Dictionary<string, AIFunction>(baseline._registeredTools, baseline._registeredTools.Comparer);
        _toolRootSchemas = new Dictionary<string, JsonElement>(baseline._toolRootSchemas, baseline._toolRootSchemas.Comparer);
        _toolSchemaProperties = new Dictionary<string, Dictionary<string, (string CanonicalName, JsonElement Schema)>>(baseline._toolSchemaProperties, baseline._toolSchemaProperties.Comparer);
        if (aIFunctions != null)
        {
            foreach (var aIFunction in aIFunctions)
            {
                RegisterToolInternal(aIFunction);
            }
        }
    }

    public RegistredToolsMetadata WithTools(IEnumerable<AIFunction> aIFunctions)
    {
        if (aIFunctions.All(IsExistingAndSame))
        {
            return this;
        }
        return new RegistredToolsMetadata(this, aIFunctions);
    }

    public RegistredToolsMetadata WithTool(AIFunction aIFunction)
    {
        if (IsExistingAndSame(aIFunction))
        {
            return this;
        }
        return new RegistredToolsMetadata(this, [ aIFunction ]);
    }

    private bool IsExistingAndSame(AIFunction tool)
    {
        return _registeredTools.TryGetValue(tool.Name, out var existing) && ReferenceEquals(existing, tool);
    }

    private void RegisterToolInternal(AIFunction tool)
    {
        if (_registeredTools.TryGetValue(tool.Name, out var existing))
        {
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

    internal bool TryGetValue(string toolName, [MaybeNullWhen(false)] out AIFunction aiFunction)
    {
        return _registeredTools.TryGetValue(toolName, out aiFunction);
    }

    internal bool TryGetSchema(string canonicalToolName, [MaybeNullWhen(false)] out Dictionary<string, (string CanonicalName, JsonElement Schema)> propDict, [MaybeNullWhen(false)] out JsonElement rootSchema)
    {
        _toolSchemaProperties.TryGetValue(canonicalToolName, out propDict);
        return _toolRootSchemas.TryGetValue(canonicalToolName, out rootSchema);
    }

    internal bool ContainsKey(string toolName)
    {
        return _registeredTools.ContainsKey(toolName);
    }
}
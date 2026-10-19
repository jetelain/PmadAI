using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pmad.AI.SourceGenerators;

[Generator]
public class ToolDispatcherGenerator : IIncrementalGenerator
{
    private sealed record DiscoveredToolMapping(
        string CanonicalName,
        EquatableArray<string> Aliases,
        EquatableArray<DiscoveredParamMapping> ParamMappings,
        EquatableArray<DiscoveredNestedParamMapping> NestedParamMappings);

    private sealed record DiscoveredParamMapping(
        string CanonicalParam,
        EquatableArray<string> Aliases);

    private sealed record DiscoveredNestedParamMapping(
        string ParamPath,
        string PropertyName,
        EquatableArray<string> Aliases);

    private sealed record DiscoveredTranslation(
        string SourceTool,
        string TargetTool,
        string CommandTemplate,
        int EscapeMethod);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Discover methods with attributes
        var methodDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                transform: static (ctx, _) => TransformMethod(ctx))
            .Where(static m => m != null);

        // 2. Discover lambdas with attributes
        var lambdaDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ParenthesizedLambdaExpressionSyntax l && l.AttributeLists.Count > 0,
                transform: static (ctx, _) => TransformLambda(ctx))
            .Where(static l => l != null);

        // Combine both sources
        var allToolsAndTranslations = methodDeclarations.Collect().Combine(lambdaDeclarations.Collect());

        context.RegisterSourceOutput(allToolsAndTranslations, static (spc, source) =>
        {
            var (methods, lambdas) = source;
            GenerateSource(spc, methods!, lambdas!);
        });
    }

    private static (DiscoveredToolMapping? Tool, List<DiscoveredTranslation>? Translations)? TransformMethod(GeneratorSyntaxContext ctx)
    {
        var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
        var model = ctx.SemanticModel;
        var methodSymbol = model.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        if (methodSymbol == null) return null;

        var aliasAttrs = methodSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolAliasAttribute" or "Pmad.AI.ToolAliasAttribute")
            .ToList();

        var transAttrs = methodSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolTranslationAttribute" or "Pmad.AI.ToolTranslationAttribute")
            .ToList();

        if (aliasAttrs.Count == 0 && transAttrs.Count == 0) return null;

        DiscoveredToolMapping? toolMapping = null;
        if (aliasAttrs.Count > 0)
        {
            string canonicalName = ResolveMethodToolName(methodSymbol, aliasAttrs);
            var aliases = new List<string>();

            foreach (var attr in aliasAttrs)
            {
                foreach (var arg in attr.ConstructorArguments)
                {
                    if (arg.Kind == TypedConstantKind.Array)
                    {
                        foreach (var elem in arg.Values)
                        {
                            if (elem.Value is string s) aliases.Add(s);
                        }
                    }
                }
            }

            var paramMappings = new List<DiscoveredParamMapping>();
            var nestedParamMappings = new List<DiscoveredNestedParamMapping>();
            foreach (var param in methodSymbol.Parameters)
            {
                var pAttrs = param.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolParameterAliasAttribute" or "Pmad.AI.ToolParameterAliasAttribute");

                var pAliases = new List<string>();
                foreach (var pa in pAttrs)
                {
                    foreach (var arg in pa.ConstructorArguments)
                    {
                        if (arg.Kind == TypedConstantKind.Array)
                        {
                            foreach (var elem in arg.Values)
                            {
                                if (elem.Value is string s) pAliases.Add(s);
                            }
                        }
                    }
                }

                if (pAliases.Count > 0)
                {
                    paramMappings.Add(new DiscoveredParamMapping(param.Name, new EquatableArray<string>(pAliases.ToArray())));
                }

                nestedParamMappings.AddRange(ExtractNestedParamMappings(param));
            }

            toolMapping = new DiscoveredToolMapping(
                canonicalName,
                new EquatableArray<string>(aliases.ToArray()),
                new EquatableArray<DiscoveredParamMapping>(paramMappings.ToArray()),
                new EquatableArray<DiscoveredNestedParamMapping>(nestedParamMappings.ToArray()));
        }

        List<DiscoveredTranslation>? translations = null;
        if (transAttrs.Count > 0)
        {
            translations = [];
            string targetTool = ResolveMethodToolName(methodSymbol, aliasAttrs);

            foreach (var attr in transAttrs)
            {
                if (attr.ConstructorArguments.Length >= 2 &&
                    attr.ConstructorArguments[0].Value is string src &&
                    attr.ConstructorArguments[1].Value is string tmpl)
                {
                    int escapeMethod = 1; // Bash default
                    var escapeNamed = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "EscapeMethod");
                    if (escapeNamed.Key != null && escapeNamed.Value.Value is int em)
                    {
                        escapeMethod = em;
                    }

                    translations.Add(new DiscoveredTranslation(src, targetTool, tmpl, escapeMethod));
                }
            }
        }

        return (toolMapping, translations);
    }

    private static (DiscoveredToolMapping? Tool, List<DiscoveredTranslation>? Translations)? TransformLambda(GeneratorSyntaxContext ctx)
    {
        var lambda = (ParenthesizedLambdaExpressionSyntax)ctx.Node;
        var model = ctx.SemanticModel;

        string? explicitName = null;
        var aliases = new List<string>();
        bool hasToolAlias = false;

        foreach (var attrList in lambda.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrType = model.GetTypeInfo(attr).Type;
                if (attrType?.ToDisplayString() is "Pmad.AI.Tools.ToolAliasAttribute" or "Pmad.AI.ToolAliasAttribute")
                {
                    hasToolAlias = true;
                    if (attr.ArgumentList != null)
                    {
                        foreach (var arg in attr.ArgumentList.Arguments)
                        {
                            if (arg.NameEquals?.Name.Identifier.Text == "Name")
                            {
                                var constant = model.GetConstantValue(arg.Expression);
                                if (constant.HasValue && constant.Value is string s) explicitName = s;
                            }
                            else if (arg.NameEquals == null)
                            {
                                var constant = model.GetConstantValue(arg.Expression);
                                if (constant.HasValue && constant.Value is string s) aliases.Add(s);
                            }
                        }
                    }
                }
            }
        }

        if (!hasToolAlias) return null;

        string? canonicalName = explicitName;
        if (string.IsNullOrEmpty(canonicalName))
        {
            canonicalName = TryExtractAIFunctionFactoryName(lambda, model);
        }

        if (string.IsNullOrEmpty(canonicalName)) return null;

        var paramMappings = new List<DiscoveredParamMapping>();
        var nestedParamMappings = new List<DiscoveredNestedParamMapping>();
        if (lambda.ParameterList != null)
        {
            foreach (var param in lambda.ParameterList.Parameters)
            {
                var pAliases = new List<string>();
                foreach (var attrList in param.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var attrType = model.GetTypeInfo(attr).Type;
                        if ((attrType?.ToDisplayString() is "Pmad.AI.Tools.ToolParameterAliasAttribute" or "Pmad.AI.ToolParameterAliasAttribute") && attr.ArgumentList != null)
                        {
                            foreach (var arg in attr.ArgumentList.Arguments)
                            {
                                var constant = model.GetConstantValue(arg.Expression);
                                if (constant.HasValue && constant.Value is string s) pAliases.Add(s);
                            }
                        }
                    }
                }

                if (pAliases.Count > 0)
                {
                    paramMappings.Add(new DiscoveredParamMapping(param.Identifier.Text, new EquatableArray<string>(pAliases.ToArray())));
                }

                if (model.GetDeclaredSymbol(param) is IParameterSymbol paramSymbol)
                {
                    nestedParamMappings.AddRange(ExtractNestedParamMappings(paramSymbol));
                }
            }
        }

        var tool = new DiscoveredToolMapping(
            canonicalName!,
            new EquatableArray<string>(aliases.ToArray()),
            new EquatableArray<DiscoveredParamMapping>(paramMappings.ToArray()),
            new EquatableArray<DiscoveredNestedParamMapping>(nestedParamMappings.ToArray()));

        return (tool, null);
    }

    private static void GenerateSource(
        SourceProductionContext spc,
        ImmutableArray<(DiscoveredToolMapping? Tool, List<DiscoveredTranslation>? Translations)?> methods,
        ImmutableArray<(DiscoveredToolMapping? Tool, List<DiscoveredTranslation>? Translations)?> lambdas)
    {
        var allTools = new Dictionary<string, ToolMappingBuilder>(StringComparer.OrdinalIgnoreCase);
        var allTranslations = new List<DiscoveredTranslation>();

        var allItems = methods.Concat(lambdas).Where(x => x.HasValue).Select(x => x!.Value);

        foreach (var (tool, translations) in allItems)
        {
            if (tool != null)
            {
                if (!allTools.TryGetValue(tool.CanonicalName, out var builder))
                {
                    builder = new ToolMappingBuilder(tool.CanonicalName);
                    allTools[tool.CanonicalName] = builder;
                }

                foreach (var a in tool.Aliases) builder.Aliases.Add(a);
                foreach (var pm in tool.ParamMappings)
                {
                    if (!builder.ParamAliases.TryGetValue(pm.CanonicalParam, out var pSet))
                    {
                        pSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        builder.ParamAliases[pm.CanonicalParam] = pSet;
                    }
                    foreach (var a in pm.Aliases) pSet.Add(a);
                }
                foreach (var npm in tool.NestedParamMappings)
                {
                    if (!builder.NestedParamAliases.TryGetValue(npm.ParamPath, out var propDict))
                    {
                        propDict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                        builder.NestedParamAliases[npm.ParamPath] = propDict;
                    }
                    if (!propDict.TryGetValue(npm.PropertyName, out var pSet))
                    {
                        pSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        propDict[npm.PropertyName] = pSet;
                    }
                    foreach (var a in npm.Aliases) pSet.Add(a);
                }
            }

            if (translations != null)
            {
                allTranslations.AddRange(translations);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Pmad.AI;");
        sb.AppendLine();
        sb.AppendLine("using Pmad.AI.Tools;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class GeneratedToolMappings");
        sb.AppendLine("{");
        sb.AppendLine("    public static ToolDispatcherConfiguration Create()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new ToolDispatcherConfiguration");
        sb.AppendLine("        {");
        sb.AppendLine("            Tools =");
        sb.AppendLine("            {");

        foreach (var toolKvp in allTools)
        {
            var name = toolKvp.Key;
            var tool = toolKvp.Value;

            sb.AppendLine($"                [\"{EscapeString(name)}\"] = new ToolMapping(");
            sb.Append("                    aliases: [");
            sb.Append(string.Join(", ", tool.Aliases.Select(a => $"\"{EscapeString(a)}\"")));
            sb.AppendLine("],");
            sb.AppendLine("                    parameterAliases: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("                    {");

            foreach (var pKvp in tool.ParamAliases)
            {
                var pName = pKvp.Key;
                var pAliases = pKvp.Value;

                sb.Append($"                        [\"{EscapeString(pName)}\"] = [");
                sb.Append(string.Join(", ", pAliases.Select(a => $"\"{EscapeString(a)}\"")));
                sb.AppendLine("],");
            }

            sb.Append("                    }");

            if (tool.NestedParamAliases.Count > 0)
            {
                sb.AppendLine(",");
                sb.AppendLine("                    nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>(StringComparer.OrdinalIgnoreCase)");
                sb.AppendLine("                    {");
                foreach (var pathKvp in tool.NestedParamAliases.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var path = pathKvp.Key;
                    var propDict = pathKvp.Value;
                    sb.AppendLine($"                        [\"{EscapeString(path)}\"] = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)");
                    sb.AppendLine("                        {");
                    foreach (var propKvp in propDict.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        var propName = propKvp.Key;
                        var pAliases = propKvp.Value;
                        sb.Append($"                            [\"{EscapeString(propName)}\"] = [");
                        sb.Append(string.Join(", ", pAliases.OrderBy(a => a, StringComparer.Ordinal).Select(a => $"\"{EscapeString(a)}\"")));
                        sb.AppendLine("],");
                    }
                    sb.AppendLine("                        },");
                }
                sb.Append("                    }");
            }

            sb.AppendLine("),");
        }

        sb.AppendLine("            },");
        sb.AppendLine("            Translations =");
        sb.AppendLine("            [");

        foreach (var t in allTranslations)
        {
            var escapeStr = t.EscapeMethod switch
            {
                0 => "ParameterEscapeMethod.None",
                2 => "ParameterEscapeMethod.PowerShell",
                _ => "ParameterEscapeMethod.Bash"
            };

            sb.AppendLine("                new ToolTranslation");
            sb.AppendLine("                {");
            sb.AppendLine($"                    SourceTool = \"{EscapeString(t.SourceTool)}\",");
            sb.AppendLine($"                    TargetTool = \"{EscapeString(t.TargetTool)}\",");
            sb.AppendLine($"                    CommandTemplate = \"{EscapeString(t.CommandTemplate)}\",");
            sb.AppendLine($"                    EscapeMethod = {escapeStr}");
            sb.AppendLine("                },");
        }

        sb.AppendLine("            ]");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("GeneratedToolMappings.g.cs", sb.ToString());
    }

    private sealed class ToolMappingBuilder(string name)
    {
        public string Name { get; } = name;
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> ParamAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, HashSet<string>>> NestedParamAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\r", "\\r")
         .Replace("\n", "\\n")
         .Replace("\t", "\\t");

    private static string ResolveMethodToolName(IMethodSymbol method, List<AttributeData> aliasAttributes)
    {
        foreach (var attr in aliasAttributes)
        {
            var nameNamedArg = attr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Name");
            if (nameNamedArg.Key != null && nameNamedArg.Value.Value is string explicitName && !string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
        }

        var methodName = method.Name;
        if (methodName.EndsWith("Async", StringComparison.Ordinal) && methodName.Length > 5)
        {
            return methodName.Substring(0, methodName.Length - 5);
        }
        return methodName;
    }

    private static string? TryExtractAIFunctionFactoryName(ParenthesizedLambdaExpressionSyntax lambda, SemanticModel model)
    {
        var parent = lambda.Parent;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax invocation)
            {
                bool isCreate = false;
                var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol != null && symbol.ContainingType?.Name == "AIFunctionFactory" && symbol.Name == "Create")
                {
                    isCreate = true;
                }
                else if (invocation.Expression is MemberAccessExpressionSyntax mae && mae.Name.Identifier.Text == "Create")
                {
                    var exprStr = mae.Expression.ToString();
                    if (exprStr.EndsWith("AIFunctionFactory", StringComparison.Ordinal))
                    {
                        isCreate = true;
                    }
                }

                if (isCreate)
                {
                    for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
                    {
                        var arg = invocation.ArgumentList.Arguments[i];
                        if (arg.NameColon?.Name.Identifier.Text == "name" ||
                            (arg.NameColon == null && i == 1))
                        {
                            var constant = model.GetConstantValue(arg.Expression);
                            if (constant.HasValue && constant.Value is string s)
                            {
                                return s;
                            }
                        }
                    }
                }
            }
            parent = parent.Parent;
        }
        return null;
    }

    private static List<DiscoveredNestedParamMapping> ExtractNestedParamMappings(IParameterSymbol param)
    {
        var list = new List<DiscoveredNestedParamMapping>();
        ITypeSymbol targetType = param.Type;
        string path = param.Name;

        if (targetType is IArrayTypeSymbol arrayType)
        {
            targetType = arrayType.ElementType;
            path = param.Name + "[]";
        }
        else if (targetType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            var typeName = namedType.Name;
            if (typeName is "IEnumerable" or "List" or "IList" or "IReadOnlyList" or "ICollection" or "Collection" or "IReadOnlyCollection" or "HashSet" or "ISet" or "ImmutableArray")
            {
                targetType = namedType.TypeArguments[0];
                path = param.Name + "[]";
            }
        }

        if (targetType is INamedTypeSymbol complexType &&
            complexType.SpecialType == SpecialType.None &&
            complexType.ContainingNamespace?.ToDisplayString() != "System" &&
            complexType.ContainingNamespace?.ToDisplayString() != "System.Collections.Generic")
        {
            foreach (var member in complexType.GetMembers())
            {
                if (member is IPropertySymbol prop)
                {
                    var aliases = ExtractMemberAliases(prop, complexType);
                    if (aliases.Count > 0)
                    {
                        list.Add(new DiscoveredNestedParamMapping(path, prop.Name, new EquatableArray<string>(aliases.ToArray())));
                    }
                }
                else if (member is IFieldSymbol field)
                {
                    var aliases = ExtractFieldAliases(field);
                    if (aliases.Count > 0)
                    {
                        list.Add(new DiscoveredNestedParamMapping(path, field.Name, new EquatableArray<string>(aliases.ToArray())));
                    }
                }
            }
        }

        return list;
    }

    private static List<string> ExtractMemberAliases(IPropertySymbol prop, INamedTypeSymbol containingType)
    {
        var aliases = new List<string>();

        // 1. Direct attributes on property
        var propAttrs = prop.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolParameterAliasAttribute" or "Pmad.AI.ToolParameterAliasAttribute");
        ExtractAliasesFromAttributes(propAttrs, aliases);

        // 2. Primary constructor parameter attributes if record / primary ctor
        foreach (var ctor in containingType.Constructors)
        {
            var ctorParam = ctor.Parameters.FirstOrDefault(p => string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase));
            if (ctorParam != null)
            {
                var paramAttrs = ctorParam.GetAttributes()
                    .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolParameterAliasAttribute" or "Pmad.AI.ToolParameterAliasAttribute");
                ExtractAliasesFromAttributes(paramAttrs, aliases);
            }
        }

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractFieldAliases(IFieldSymbol field)
    {
        var aliases = new List<string>();
        var fieldAttrs = field.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() is "Pmad.AI.Tools.ToolParameterAliasAttribute" or "Pmad.AI.ToolParameterAliasAttribute");
        ExtractAliasesFromAttributes(fieldAttrs, aliases);
        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractAliasesFromAttributes(IEnumerable<AttributeData> attributes, List<string> aliases)
    {
        foreach (var pa in attributes)
        {
            foreach (var arg in pa.ConstructorArguments)
            {
                if (arg.Kind == TypedConstantKind.Array)
                {
                    foreach (var elem in arg.Values)
                    {
                        if (elem.Value is string s && !string.IsNullOrWhiteSpace(s))
                        {
                            aliases.Add(s);
                        }
                    }
                }
                else if (arg.Value is string singleStr && !string.IsNullOrWhiteSpace(singleStr))
                {
                    aliases.Add(singleStr);
                }
            }
        }
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[] array) => _array = array;

    public int Count => _array?.Length ?? 0;
    public T[] Array => _array ?? [];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? [])).GetEnumerator();

    public bool Equals(EquatableArray<T> other)
    {
        if (_array == null && other._array == null) return true;
        if (_array == null || other._array == null) return false;
        if (_array.Length != other._array.Length) return false;
        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array == null) return 0;
        int hash = 17;
        foreach (var item in _array)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }
        return hash;
    }
}

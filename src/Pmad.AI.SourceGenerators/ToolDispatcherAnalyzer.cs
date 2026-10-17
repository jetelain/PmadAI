using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pmad.AI.SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ToolDispatcherAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticIdPmad001 = "PMAD001";
    public const string DiagnosticIdPmad002 = "PMAD002";
    public const string DiagnosticIdPmad003 = "PMAD003";

    private static readonly DiagnosticDescriptor RulePmad001 = new(
        DiagnosticIdPmad001,
        "Invalid or duplicate parameter alias",
        "Parameter alias '{0}' on tool '{1}' is invalid: it conflicts with another parameter name or is duplicated",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RulePmad002 = new(
        DiagnosticIdPmad002,
        "Duplicate tool alias across distinct tools",
        "Duplicate tool alias '{0}' declared across distinct tools ('{1}' and '{2}')",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RulePmad003 = new(
        DiagnosticIdPmad003,
        "Unnamed lambda tool alias",
        "Lambda expression with [ToolAlias] must specify explicit 'Name' property or be passed directly to AIFunctionFactory.Create with a constant tool name",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RulePmad001, RulePmad002, RulePmad003);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var aliasSymbol = compilationContext.Compilation.GetTypeByMetadataName("Pmad.AI.Tools.ToolAliasAttribute")
                ?? compilationContext.Compilation.GetTypeByMetadataName("Pmad.AI.ToolAliasAttribute");
            var paramAliasSymbol = compilationContext.Compilation.GetTypeByMetadataName("Pmad.AI.Tools.ToolParameterAliasAttribute")
                ?? compilationContext.Compilation.GetTypeByMetadataName("Pmad.AI.ToolParameterAliasAttribute");

            if (aliasSymbol == null && paramAliasSymbol == null)
            {
                return;
            }

            var aliasLocations = new Dictionary<string, (string ToolName, Location Location)>(StringComparer.OrdinalIgnoreCase);

            compilationContext.RegisterSyntaxNodeAction(ctx =>
            {
                if (ctx.Node is MethodDeclarationSyntax methodDeclaration)
                {
                    AnalyzeMethod(ctx, methodDeclaration, aliasSymbol, paramAliasSymbol, aliasLocations);
                }
                else if (ctx.Node is ParenthesizedLambdaExpressionSyntax lambdaDeclaration)
                {
                    AnalyzeLambda(ctx, lambdaDeclaration, aliasSymbol, paramAliasSymbol, aliasLocations);
                }
            }, SyntaxKind.MethodDeclaration, SyntaxKind.ParenthesizedLambdaExpression);
        });
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext ctx,
        MethodDeclarationSyntax method,
        INamedTypeSymbol? aliasAttrSymbol,
        INamedTypeSymbol? paramAliasAttrSymbol,
        Dictionary<string, (string ToolName, Location Location)> aliasLocations)
    {
        var model = ctx.SemanticModel;
        var methodSymbol = model.GetDeclaredSymbol(method) as IMethodSymbol;
        if (methodSymbol == null) return;

        var aliasAttributes = methodSymbol.GetAttributes()
            .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, aliasAttrSymbol))
            .ToList();

        if (aliasAttributes.Count == 0) return;

        string toolName = ResolveMethodToolName(methodSymbol, aliasAttributes);

        CheckDuplicateToolAliases(ctx, toolName, aliasAttributes, aliasLocations);
        CheckParameterAliases(ctx, methodSymbol.Parameters, paramAliasAttrSymbol, toolName);
    }

    private static void AnalyzeLambda(
        SyntaxNodeAnalysisContext ctx,
        ParenthesizedLambdaExpressionSyntax lambda,
        INamedTypeSymbol? aliasAttrSymbol,
        INamedTypeSymbol? paramAliasAttrSymbol,
        Dictionary<string, (string ToolName, Location Location)> aliasLocations)
    {
        var model = ctx.SemanticModel;
        var lambdaSymbol = model.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
        if (lambdaSymbol == null)
        {
            lambdaSymbol = model.GetSymbolInfo(lambda).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }

        // Check attributes on lambda syntax if present
        var attributeLists = lambda.AttributeLists;
        if (attributeLists.Count == 0) return;

        var hasToolAlias = false;
        string? explicitName = null;
        var aliasStrings = new List<(string Alias, Location Loc)>();

        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrType = model.GetTypeInfo(attr).Type;
                if (SymbolEqualityComparer.Default.Equals(attrType, aliasAttrSymbol))
                {
                    hasToolAlias = true;

                    // Extract explicit Name if present
                    if (attr.ArgumentList != null)
                    {
                        foreach (var arg in attr.ArgumentList.Arguments)
                        {
                            if (arg.NameEquals?.Name.Identifier.Text == "Name")
                            {
                                var constant = model.GetConstantValue(arg.Expression);
                                if (constant.HasValue && constant.Value is string s)
                                {
                                    explicitName = s;
                                }
                            }
                            else if (arg.NameEquals == null)
                            {
                                var constant = model.GetConstantValue(arg.Expression);
                                if (constant.HasValue && constant.Value is string s)
                                {
                                    aliasStrings.Add((s, arg.GetLocation()));
                                }
                            }
                        }
                    }
                }
            }
        }

        if (!hasToolAlias) return;

        string? resolvedToolName = explicitName;
        if (string.IsNullOrEmpty(resolvedToolName))
        {
            // Contextual inference: check enclosing AIFunctionFactory.Create(..., name: "constant", ...)
            resolvedToolName = TryExtractAIFunctionFactoryName(lambda, model);
        }

        if (string.IsNullOrEmpty(resolvedToolName))
        {
            // PMAD003
            ctx.ReportDiagnostic(Diagnostic.Create(RulePmad003, lambda.GetLocation()));
            return;
        }

        // Check tool aliases
        lock (aliasLocations)
        {
            foreach (var (alias, loc) in aliasStrings)
            {
                if (aliasLocations.TryGetValue(alias, out var existing))
                {
                    if (!string.Equals(existing.ToolName, resolvedToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(RulePmad002, loc, alias, existing.ToolName, resolvedToolName));
                    }
                }
                else
                {
                    aliasLocations[alias] = (resolvedToolName!, loc);
                }
            }
        }

        // Check parameter aliases
        if (lambda.ParameterList != null)
        {
            var paramNames = new HashSet<string>(lambda.ParameterList.Parameters.Select(p => p.Identifier.Text), StringComparer.OrdinalIgnoreCase);
            var seenAliasesInTool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var param in lambda.ParameterList.Parameters)
            {
                var paramName = param.Identifier.Text;
                foreach (var attrList in param.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var attrType = model.GetTypeInfo(attr).Type;
                        if (SymbolEqualityComparer.Default.Equals(attrType, paramAliasAttrSymbol))
                        {
                            if (attr.ArgumentList != null)
                            {
                                foreach (var arg in attr.ArgumentList.Arguments)
                                {
                                    var val = model.GetConstantValue(arg.Expression);
                                    if (val.HasValue && val.Value is string aliasStr)
                                    {
                                        // Conflicts with another parameter name?
                                        if (paramNames.Contains(aliasStr) && !string.Equals(aliasStr, paramName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            ctx.ReportDiagnostic(Diagnostic.Create(RulePmad001, arg.GetLocation(), aliasStr, resolvedToolName));
                                        }
                                        // Duplicate within tool?
                                        if (!seenAliasesInTool.Add(aliasStr))
                                        {
                                            ctx.ReportDiagnostic(Diagnostic.Create(RulePmad001, arg.GetLocation(), aliasStr, resolvedToolName));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

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

    private static void CheckDuplicateToolAliases(
        SyntaxNodeAnalysisContext ctx,
        string toolName,
        List<AttributeData> aliasAttributes,
        Dictionary<string, (string ToolName, Location Location)> aliasLocations)
    {
        lock (aliasLocations)
        {
            foreach (var attr in aliasAttributes)
            {
                var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? ctx.Node.GetLocation();
                foreach (var c in attr.ConstructorArguments)
                {
                    if (c.Kind == TypedConstantKind.Array)
                    {
                        foreach (var elem in c.Values)
                        {
                            if (elem.Value is string aliasStr)
                            {
                                if (aliasLocations.TryGetValue(aliasStr, out var existing))
                                {
                                    if (!string.Equals(existing.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        ctx.ReportDiagnostic(Diagnostic.Create(RulePmad002, loc, aliasStr, existing.ToolName, toolName));
                                    }
                                }
                                else
                                {
                                    aliasLocations[aliasStr] = (toolName, loc);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static void CheckParameterAliases(
        SyntaxNodeAnalysisContext ctx,
        ImmutableArray<IParameterSymbol> parameters,
        INamedTypeSymbol? paramAliasAttrSymbol,
        string toolName)
    {
        var paramNames = new HashSet<string>(parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var seenAliasesInTool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in parameters)
        {
            var paramName = param.Name;
            var paramAttrs = param.GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, paramAliasAttrSymbol));

            foreach (var attr in paramAttrs)
            {
                var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? ctx.Node.GetLocation();
                foreach (var c in attr.ConstructorArguments)
                {
                    if (c.Kind == TypedConstantKind.Array)
                    {
                        foreach (var elem in c.Values)
                        {
                            if (elem.Value is string aliasStr)
                            {
                                if (paramNames.Contains(aliasStr) && !string.Equals(aliasStr, paramName, StringComparison.OrdinalIgnoreCase))
                                {
                                    ctx.ReportDiagnostic(Diagnostic.Create(RulePmad001, loc, aliasStr, toolName));
                                }
                                if (!seenAliasesInTool.Add(aliasStr))
                                {
                                    ctx.ReportDiagnostic(Diagnostic.Create(RulePmad001, loc, aliasStr, toolName));
                                }
                            }
                        }
                    }
                }
            }
        }
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
                    // Find name argument
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
}

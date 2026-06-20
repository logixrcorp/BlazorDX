using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorDX.Analyzers;

/// <summary>
/// DX1001: flags <c>MarkupString</c> values built from non-constant data, whether
/// via <c>new MarkupString(x)</c> or the <c>(MarkupString)x</c> cast. Constant
/// markup (literals, consts) is allowed because it cannot carry injected input.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawHtmlAnalyzer : DiagnosticAnalyzer
{
    private const string MarkupStringTypeName = "MarkupString";
    private const string MarkupStringNamespace = "Microsoft.AspNetCore.Components";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.RawHtmlInjection);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        ExpressionSyntax? argument = creation.ArgumentList?.Arguments.Count == 1
            ? creation.ArgumentList.Arguments[0].Expression
            : null;

        if (argument is null || !IsMarkupString(context, creation))
        {
            return;
        }

        ReportIfNotConstant(context, argument, creation.GetLocation());
    }

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        if (!IsMarkupString(context, cast))
        {
            return;
        }

        ReportIfNotConstant(context, cast.Expression, cast.GetLocation());
    }

    private static bool IsMarkupString(SyntaxNodeAnalysisContext context, ExpressionSyntax node)
    {
        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type;
        return type is not null
            && type.Name == MarkupStringTypeName
            && type.ContainingNamespace?.ToDisplayString() == MarkupStringNamespace;
    }

    private static void ReportIfNotConstant(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax argument,
        Location location)
    {
        // A compile-time constant cannot carry user-supplied markup, so it is safe.
        if (context.SemanticModel.GetConstantValue(argument, context.CancellationToken).HasValue)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.RawHtmlInjection, location));
    }
}

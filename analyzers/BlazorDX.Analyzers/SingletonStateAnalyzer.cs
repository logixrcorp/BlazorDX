using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorDX.Analyzers;

/// <summary>
/// DX1002: flags <c>AddSingleton&lt;TState&gt;()</c> registrations whose service
/// type name looks like UI state ("State" or "Store" suffix). On Blazor Server a
/// Singleton is shared across every connected user, so UI state stored that way
/// leaks between sessions. The fix is a scoped lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonStateAnalyzer : DiagnosticAnalyzer
{
    private const string SingletonMethodName = "AddSingleton";

    private static readonly string[] StateSuffixes = { "State", "Store" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.SingletonState);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        if (method.Name != SingletonMethodName || method.TypeArguments.Length == 0)
        {
            return;
        }

        // The first type argument is the service type for every AddSingleton overload.
        ITypeSymbol serviceType = method.TypeArguments[0];
        if (!LooksLikeState(serviceType.Name))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.SingletonState,
            invocation.GetLocation(),
            serviceType.Name));
    }

    private static bool LooksLikeState(string typeName)
    {
        foreach (string suffix in StateSuffixes)
        {
            if (typeName.EndsWith(suffix, System.StringComparison.Ordinal) && typeName.Length > suffix.Length)
            {
                return true;
            }
        }

        return false;
    }
}

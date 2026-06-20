using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorDX.Analyzers;

/// <summary>
/// DX1000: reports any C# or Razor-generated syntax tree that exceeds the
/// 1000-line cap. Non-C# files (.rs, .ts, .css) are covered separately by the
/// FileLength MSBuild target, since the compiler never sees them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileLengthAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The hard cap. Mirrors the rule documented in CONTRIBUTING.md.</summary>
    public const int MaxLines = 1000;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.FileTooLong);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        SourceText text = context.Tree.GetText(context.CancellationToken);
        int lineCount = text.Lines.Count;
        if (lineCount <= MaxLines)
        {
            return;
        }

        // Point the diagnostic at the first line over the cap so the author is
        // taken straight to where the file should have been split.
        TextLine firstOverflowLine = text.Lines[MaxLines];
        Location location = Location.Create(context.Tree, firstOverflowLine.Span);
        string fileName = Path.GetFileName(context.Tree.FilePath);

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.FileTooLong,
            location,
            fileName,
            lineCount,
            MaxLines));
    }
}

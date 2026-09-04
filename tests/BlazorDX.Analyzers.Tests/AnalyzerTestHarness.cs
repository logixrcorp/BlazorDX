using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorDX.Analyzers.Tests;

/// <summary>
/// Minimal in-memory harness: compiles a source string against the running
/// framework's reference set and runs one analyzer over it, returning the
/// analyzer diagnostics. Avoids extra test-only Roslyn packages.
/// </summary>
internal static class AnalyzerTestHarness
{
    private static readonly MetadataReference[] References = LoadFrameworkReferences();

    /// <param name="assemblyName">
    /// Defaults to <c>BlazorDX.Components</c> because <c>HardcodedStringAnalyzer</c> only reports
    /// there — <c>DxStrings</c> exists in no other assembly, so the fix DX1003 suggests cannot be
    /// written elsewhere. Under the old default every DX1003 test would pass by reporting
    /// nothing, which is exactly the failure mode a scoped analyzer invites. Pass a different
    /// name to test the scoping itself.
    /// </param>
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source, DiagnosticAnalyzer analyzer, string assemblyName = "BlazorDX.Components")
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [tree],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer));

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference[] LoadFrameworkReferences()
    {
        // The trusted platform assemblies list is every framework DLL the test
        // host loaded against — enough to give the source a real semantic model.
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trusted
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}

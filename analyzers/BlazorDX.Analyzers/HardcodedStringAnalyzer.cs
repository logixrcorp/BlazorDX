using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorDX.Analyzers;

/// <summary>
/// DX1003: flags hardcoded user-facing text in a component — visible content, the accessible-name
/// attributes, and defaulted <c>[Parameter]</c> strings on text-carrying parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>The per-type ratchet is closed.</b> This used to inspect only types holding a
/// <c>DxStrings&lt;T&gt;</c> member, so localizing a component was what switched the rule on for
/// it. That was scaffolding: with 83 components to convert and <c>TreatWarningsAsErrors</c>
/// repo-wide, a rule that fired everywhere would have reported the whole backlog at once. The
/// rollout is finished, so it now applies to every type in every package —
/// including a brand-new component that localizes nothing, which the old scoping let opt out.
/// See docs/adr/0021-optional-localization-and-rollout-guardrails.md.
/// </para>
/// <para>
/// What it still cannot see is text that reaches the DOM through a variable — a lookup table, a
/// switch arm, an argument to a local helper. That is the majority of this library's user-facing
/// text, and it is covered by convention plus <c>LocalizedStringConsistencyTests</c> rather than
/// by this rule; see the ADR's first amendment.
/// </para>
/// <para>
/// Glyph-only literals ("✓", "▾", "×", " *") are never flagged: they carry no language. The test
/// is simply whether the text contains a letter.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HardcodedStringAnalyzer : DiagnosticAnalyzer
{
    private const string RenderTreeBuilderTypeName = "RenderTreeBuilder";
    private const string ParameterAttributeName = "Parameter";

    // Attributes whose value a user reads or hears. "class"/"style"/"role"/"type"/"id" and the
    // rest are machine-facing and must never be flagged.
    private static readonly ImmutableHashSet<string> UserFacingAttributes = ImmutableHashSet.Create(
        "aria-label",
        "aria-description",
        "aria-roledescription",
        "aria-valuetext",
        "alt",
        "placeholder",
        "title");

    // Suffixes that make a [Parameter] property's default *text* rather than a token. The
    // distinction is real and this rule originally missed it: DxAlert's
    // `[Parameter] public string Severity { get; set; } = "info"` is a variant name that ends up
    // in a CSS class, not something a user reads — flagging it (as the first CI run did) would
    // have forced a localizer call on a machine-facing value. Matching on the name is the same
    // shape as UserFacingAttributes above: an allow-list, wrong only by omission.
    private static readonly ImmutableArray<string> UserFacingParameterSuffixes = ImmutableArray.Create(
        "Label",
        "Text",
        "Title",
        "Message",
        "Placeholder",
        "Description",
        "Caption",
        "Heading",
        "Hint",
        "Tooltip",
        "Prompt");


    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.HardcodedUserFacingString);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeParameterProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return;
        }

        string method = member.Name.Identifier.ValueText;
        if (method is not ("AddContent" or "AddAttribute"))
        {
            return;
        }

        if (!IsInLocalizedType(context) || !IsRenderTreeBuilder(context, member.Expression))
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;

        // AddContent(sequence, value) — the value is rendered text.
        if (method == "AddContent" && args.Count == 2)
        {
            ReportIfUserFacingText(context, args[1].Expression);
            return;
        }

        // AddAttribute(sequence, name, value) — only the accessible-name attributes carry text.
        if (method == "AddAttribute"
            && args.Count == 3
            && args[1].Expression is LiteralExpressionSyntax { Token.ValueText: { } attribute }
            && UserFacingAttributes.Contains(attribute))
        {
            ReportIfUserFacingText(context, args[2].Expression);
        }
    }

    private static void AnalyzeParameterProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;

        // A defaulted [Parameter] string is the component's own text until a caller overrides it --
        // but only when the parameter names text at all. See UserFacingParameterSuffixes.
        if (property.Initializer is null
            || !IsInLocalizedType(context)
            || !HasParameterAttribute(property)
            || !NamesUserFacingText(property.Identifier.ValueText))
        {
            return;
        }

        ReportIfUserFacingText(context, property.Initializer.Value);
    }

    private static bool HasParameterAttribute(PropertyDeclarationSyntax property) =>
        property.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() is ParameterAttributeName or ParameterAttributeName + "Attribute");

    private static bool NamesUserFacingText(string propertyName) =>
        UserFacingParameterSuffixes.Any(suffix => propertyName.EndsWith(suffix, StringComparison.Ordinal));

    private static void ReportIfUserFacingText(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        string? text = expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                literal.Token.ValueText,

            // An interpolated string is user-facing if its *literal* segments carry words — the
            // interpolated holes are data. $"{Count} ▾" is fine; $"Chart of {Count} points" is not.
            InterpolatedStringExpressionSyntax interpolated =>
                string.Concat(interpolated.Contents.OfType<InterpolatedStringTextSyntax>()
                    .Select(part => part.TextToken.ValueText)),

            _ => null,
        };

        if (text is null || !ContainsLetter(text))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HardcodedUserFacingString, expression.GetLocation(), text.Trim()));
    }

    private static bool ContainsLetter(string text) => text.Any(char.IsLetter);

    /// <summary>
    /// Every assembly that ships UI — which is all of them except the test projects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two earlier scopings are gone. The rule first required the enclosing type to hold a
    /// <c>DxStrings&lt;…&gt;</c> member, so it covered exactly the components already converted and
    /// grew with each rollout batch — scaffolding for a migration under
    /// <c>TreatWarningsAsErrors</c>. It was then scoped to <c>BlazorDX.Components</c>, because
    /// <c>DxStrings</c> lived there and nowhere else, so reporting elsewhere would have demanded a
    /// fix that could not be written. The helper is now shared source compiled into every package
    /// that renders text, and both scopings are retired.
    /// </para>
    /// <para>
    /// Test projects stay excluded, and that is a statement about what the rule is for rather than
    /// a convenience. DX1003 governs text a user reads in the product. A test that builds a render
    /// tree out of <c>"Alpha body"</c> and <c>"Trigger text"</c> is writing fixture data; asking it
    /// to route those through a localizer would be asking it to translate its own inputs.
    /// </para>
    /// </remarks>
    private static bool IsInLocalizedType(SyntaxNodeAnalysisContext context) =>
        context.Compilation.AssemblyName?.EndsWith(".Tests", StringComparison.Ordinal) != true;

    private static bool IsRenderTreeBuilder(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver) =>
        context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type?.Name
            == RenderTreeBuilderTypeName;
}

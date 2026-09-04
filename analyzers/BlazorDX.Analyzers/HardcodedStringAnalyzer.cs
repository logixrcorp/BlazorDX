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
/// rollout is finished, so it now applies to every type in <c>BlazorDX.Components</c> —
/// including a brand-new component that localizes nothing, which the old scoping let opt out.
/// See docs/adr/0021-optional-localization-and-rollout-guardrails.md.
/// </para>
/// <para>
/// It is still scoped to one assembly, for a different and narrower reason — see
/// <see cref="LocalizableAssembly"/>.
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

    /// <summary>
    /// The only assembly where <c>DxStrings</c> exists, and therefore the only one where this
    /// rule's suggested fix can be written.
    /// </summary>
    /// <remarks>
    /// Retiring the per-type ratchet surfaced 17 hardcoded strings in four other packages —
    /// <c>BlazorDX.Primitives</c> (four placeholder defaults), <c>BlazorDX.Htmx</c>,
    /// <c>BlazorDX.Integrations.PowerBI</c> and <c>BlazorDX.Integrations.Reporting</c>. They are
    /// real findings, but none of those packages references <c>BlazorDX.Components</c>, so
    /// <c>DxStrings</c> is unreachable from all of them and the diagnostic would demand a fix
    /// that cannot be written — the same trap the defaulted-<c>[Parameter]</c> rule fell into.
    /// <para>
    /// Making the helper available there is a packaging decision (a new shared package, or new
    /// dependencies on published packages), not a retrofit, so the rule is scoped to where its
    /// advice holds. See docs/localization.md for the finding and what it would take to widen.
    /// </para>
    /// </remarks>
    private const string LocalizableAssembly = "BlazorDX.Components";

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
    /// Always true now — the ratchet is closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to require the enclosing type to hold a <c>DxStrings&lt;…&gt;</c> member, so the
    /// rule covered exactly the components already localized and grew with each rollout batch.
    /// That was scaffolding for a migration: with 83 components to convert and
    /// <c>TreatWarningsAsErrors</c> repo-wide, a rule that fired everywhere would have broken the
    /// build on day one.
    /// </para>
    /// <para>
    /// The rollout is finished — every component with user-facing text at a render call site now
    /// routes it through a localizer — so the scoping is retired rather than left in place. It
    /// was the rule's one hole: a brand-new component could opt out of the check simply by not
    /// localizing anything, which is precisely the component most likely to need it.
    /// </para>
    /// <para>
    /// Kept as a method rather than deleted at every call site: it names the decision, and this
    /// remark is where the history belongs.
    /// </para>
    /// </remarks>
    private static bool IsInLocalizedType(SyntaxNodeAnalysisContext context) =>
        context.Compilation.AssemblyName == LocalizableAssembly;

    private static bool IsRenderTreeBuilder(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver) =>
        context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type?.Name
            == RenderTreeBuilderTypeName;
}

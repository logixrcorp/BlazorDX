using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorDX.Analyzers;

/// <summary>
/// DX1003: flags hardcoded user-facing text in a component that already localizes — visible
/// content, the accessible-name attributes, and defaulted <c>[Parameter]</c> strings on
/// text-carrying parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ratchet.</b> This only inspects types that hold a <c>DxStrings&lt;T&gt;</c> member, so
/// localizing a component is what switches the rule on for it. Firing on every component instead
/// would report the entire not-yet-localized backlog at once — and since the repo builds with
/// <c>TreatWarningsAsErrors</c>, even a Warning severity would break the build. Scoping it this
/// way means each rollout batch extends coverage automatically and the rule is silent until then.
/// </para>
/// <para>
/// Known hole, deliberate: a brand-new component with no localizer at all is unguarded. Closing it
/// is the rollout's completion criterion — once every component is localized, this check can widen
/// to all of them. See docs/adr/0021-optional-localization-and-rollout-guardrails.md.
/// </para>
/// <para>
/// Glyph-only literals ("✓", "▾", "×", " *") are never flagged: they carry no language. The test
/// is simply whether the text contains a letter.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HardcodedStringAnalyzer : DiagnosticAnalyzer
{
    private const string LocalizerTypeName = "DxStrings";
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

        if (!IsInLocalizedType(invocation) || !IsRenderTreeBuilder(context, member.Expression))
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
            || !IsInLocalizedType(property)
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

    // Syntactic on purpose: the check is "does this type localize at all", and a member typed
    // DxStrings<...> answers that without needing the symbol resolved.
    private static bool IsInLocalizedType(SyntaxNode node)
    {
        TypeDeclarationSyntax? type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        return type.Members.Any(member => member switch
        {
            FieldDeclarationSyntax field => MentionsLocalizer(field.Declaration.Type),
            PropertyDeclarationSyntax property => MentionsLocalizer(property.Type),
            _ => false,
        });
    }

    private static bool MentionsLocalizer(TypeSyntax type) =>
        type.DescendantNodesAndSelf()
            .OfType<GenericNameSyntax>()
            .Any(generic => generic.Identifier.ValueText == LocalizerTypeName);

    private static bool IsRenderTreeBuilder(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver) =>
        context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type?.Name
            == RenderTreeBuilderTypeName;
}

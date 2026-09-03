using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorDX.SourceGen;

/// <summary>
/// Diagnostics for a form field's <c>DependsOn</c> reference. IDs are DX2001-DX2004 —
/// a range deliberately separate from BlazorDX.Analyzers' DX10xx block (those are a
/// different project, reported by a Roslyn analyzer; these are reported by this
/// source generator) to avoid any future collision between the two.
/// </summary>
internal static class FormModelDiagnostics
{
    private const string Category = "BlazorDX.Forms";

    /// <summary>DX2001 — DependsOn names a property that isn't an opted-in form field.</summary>
    public static readonly DiagnosticDescriptor UnknownField = new(
        id: "DX2001",
        title: "DependsOn does not name a form field",
        messageFormat: "'{0}'.DependsOn = '{1}' does not name a form field on this type — " +
            "it must be a property carrying [DxField] or a recognized DataAnnotations attribute",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A conditional field's DependsOn must reference another field the generator has also discovered.");

    /// <summary>DX2002 — DependsOn names a field the AI is never told exists.</summary>
    public static readonly DiagnosticDescriptor SensitiveField = new(
        id: "DX2002",
        title: "DependsOn cannot name a Sensitive field",
        messageFormat: "'{0}'.DependsOn = '{1}' names a Sensitive/[AiHidden] field — an AI is never told " +
            "'{1}' exists, so it could never legally activate '{0}' via a tool call",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A field an AI cannot see cannot be a legal AI-facing gate for another field.");

    /// <summary>DX2003 — DependsOn names another conditional field (chaining, out of scope for v1).</summary>
    public static readonly DiagnosticDescriptor ChainedField = new(
        id: "DX2003",
        title: "DependsOn cannot chain to another conditional field",
        messageFormat: "'{0}'.DependsOn = '{1}' names a field that is itself conditional — " +
            "chained/transitive dependencies are not supported; depend on an unconditional field",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Conditional fields support a flat, single-level dependency only.");

    /// <summary>DX2004 — DependsOn names the field itself.</summary>
    public static readonly DiagnosticDescriptor SelfReference = new(
        id: "DX2004",
        title: "DependsOn cannot reference the field itself",
        messageFormat: "'{0}'.DependsOn cannot name '{0}' itself",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A field cannot be conditional on its own value.");

    /// <summary>
    /// Validates every field's DependsOn reference against the full field set for one
    /// model type. Pure data-in/diagnostics-out — no compilation involved, so this is
    /// directly unit-testable against hand-built <see cref="FormFieldDef"/> lists.
    /// </summary>
    public static IEnumerable<(FormFieldDef Field, DiagnosticDescriptor Descriptor)> Validate(
        ImmutableArray<FormFieldDef> fields)
    {
        Dictionary<string, FormFieldDef> byName = fields.ToDictionary(f => f.PropertyName);

        foreach (FormFieldDef field in fields)
        {
            if (field.DependsOn is null)
            {
                continue;
            }

            if (field.DependsOn == field.PropertyName)
            {
                yield return (field, SelfReference);
                continue;
            }

            if (!byName.TryGetValue(field.DependsOn, out FormFieldDef? target))
            {
                yield return (field, UnknownField);
                continue;
            }

            if (target.Sensitive)
            {
                yield return (field, SensitiveField);
                continue;
            }

            if (target.DependsOn is not null)
            {
                yield return (field, ChainedField);
            }
        }
    }
}

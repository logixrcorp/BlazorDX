using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorDX.SourceGen;

/// <summary>
/// Diagnostics for a form field's <c>DependsOn</c> reference (DX2001-DX2004) and for
/// array/nested-object field shape (DX2005, DX2007-DX2009 — DX2006, cycle detection,
/// is a separate whole-compilation pass, see <see cref="FormModelCycles"/>). A range
/// deliberately separate from BlazorDX.Analyzers' DX10xx block (those are a different
/// project, reported by a Roslyn analyzer; these are reported by this source
/// generator) to avoid any future collision between the two.
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

    /// <summary>DX2005 — an Array field's element type is neither [DxFormModel]-tagged nor a recognized scalar.</summary>
    public static readonly DiagnosticDescriptor InvalidCollectionElement = new(
        id: "DX2005",
        title: "Unsupported List<T> element type, or unsupported collection shape",
        messageFormat: "'{0}' is a List<T> whose element type is neither [DxFormModel]-tagged nor a " +
            "recognized scalar, or is typed as some other collection shape (T[]/IList<T>/etc.) — " +
            "array fields must be exactly List<T>, with T either [DxFormModel]-tagged or a supported scalar",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An array field's declared shape must be List<T> with a supported element type.");

    /// <summary>DX2007 — DependsOn crosses a nested/array field boundary, in either direction.</summary>
    public static readonly DiagnosticDescriptor CrossBoundaryDependsOn = new(
        id: "DX2007",
        title: "DependsOn cannot cross a nested/array field boundary",
        messageFormat: "'{0}'.DependsOn = '{1}' crosses a nested/array field boundary — an Object or Array " +
            "field can neither be conditional nor gate another field's conditional visibility",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Conditional-field evaluation reads a flat scalar value; it cannot traverse into or out of a nested/array field.");

    /// <summary>DX2008 — an Object/array-of-nested field's target type has no discovered form fields.</summary>
    public static readonly DiagnosticDescriptor ZeroFieldsTarget = new(
        id: "DX2008",
        title: "Nested form model has no fields",
        messageFormat: "'{0}' references '{1}', which has no discovered form fields — confirm it carries " +
            "[DxFormModel] and its properties carry [DxField] or a recognized DataAnnotations attribute",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A nested/array-element type with zero fields is almost always a missing [DxField]/[DxFormModel], not an intentional empty sub-form.");

    /// <summary>DX2009 — an Object/array-of-nested field's target type has no accessible parameterless constructor.</summary>
    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        id: "DX2009",
        title: "Nested form model has no accessible parameterless constructor",
        messageFormat: "'{0}' references '{1}', which has no accessible public parameterless constructor — " +
            "one is required to materialize a new nested instance (a null Object field, or a new Array row)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Nested/array-element instances are constructed with `new T()`, never reflection.");

    /// <summary>
    /// Validates every field's DependsOn reference and array-element shape against the
    /// full field set for one model type. Pure data-in/diagnostics-out — no
    /// compilation involved, so this is directly unit-testable against hand-built
    /// <see cref="FormFieldDef"/> lists. DX2006 (cycle detection) is not here — it
    /// needs the whole compilation's type graph, not one type's field list; see
    /// <see cref="FormModelCycles"/>. DX2008/DX2009 are also not here — they need
    /// symbol-level access to the referenced type, computed directly in
    /// <c>FormModelAnalysis.ReadFields</c> while the symbol is still in hand.
    /// </summary>
    public static IEnumerable<(FormFieldDef Field, DiagnosticDescriptor Descriptor)> Validate(
        ImmutableArray<FormFieldDef> fields)
    {
        Dictionary<string, FormFieldDef> byName = fields.ToDictionary(f => f.PropertyName);

        foreach (FormFieldDef field in fields)
        {
            if (field.Kind == "Array" && field.NestedTypeFqn is null && field.ArrayElementKind is null)
            {
                yield return (field, InvalidCollectionElement);
            }

            if (field.Kind is "Object" or "Array" && field.DependsOn is not null)
            {
                yield return (field, CrossBoundaryDependsOn);
            }

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

            if (target.Kind is "Object" or "Array")
            {
                yield return (field, CrossBoundaryDependsOn);
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

/// <summary>
/// DX2006 — a nesting/array reference cycle in the <c>[DxFormModel]</c> type graph
/// (e.g. A nests B, B nests A). Unlike DX2001-DX2005/2007-2009, this cannot be
/// checked per-type: it needs the full set of models discovered across the whole
/// compilation, wired in <see cref="FormModelGenerator"/> via a <c>.Collect()</c>
/// provider distinct from the per-type diagnostics/emission pipeline. A real cycle
/// must be a compile-time error — <c>FormTool</c>'s JSON-Schema builder recurses over
/// the field-kind graph (not live instance data), so an uncaught cycle would recurse
/// unconditionally forever regardless of what data anyone ever constructs.
/// </summary>
internal static class FormModelCycles
{
    /// <summary>DX2006 — see the type-level summary above.</summary>
    public static readonly DiagnosticDescriptor Cycle = new(
        id: "DX2006",
        title: "Nesting/array cycle between form models",
        messageFormat: "'{0}' is part of a nesting/array reference cycle among [DxFormModel] types ({1}) — " +
            "a form model's Object/Array fields must not (transitively) reference back to itself",
        category: "BlazorDX.Forms",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The Object/Array field-kind graph must be acyclic; FormTool's schema builder and DxForm's nested rendering both recurse over it unconditionally.");

    /// <summary>
    /// 3-color (white/gray/black) DFS cycle detection over the whole-compilation set
    /// of <see cref="FormModelDef"/>s, keyed by fully-qualified type name. Pure,
    /// directly unit-testable against hand-built <see cref="FormModelDef"/> lists —
    /// no Roslyn compilation involved. A node is marked cyclic only via a back-edge
    /// (revisiting a node still gray/on the current DFS stack) — deliberately NOT by
    /// propagating "a descendant is cyclic" up through every ancestor, which would
    /// wrongly flag a type that merely references into a cycle as being part of it.
    /// </summary>
    public static IEnumerable<Diagnostic> Validate(ImmutableArray<FormModelDef> models)
    {
        Dictionary<string, FormModelDef> byFqn = new();
        foreach (FormModelDef model in models)
        {
            byFqn[Fqn(model)] = model;
        }

        Dictionary<string, int> color = new(); // 0/absent = white, 1 = gray (on stack), 2 = black (done)
        List<string> stack = new();
        HashSet<string> cyclic = new();

        foreach (FormModelDef model in models)
        {
            string fqn = Fqn(model);
            if (!color.ContainsKey(fqn))
            {
                Visit(fqn, byFqn, color, stack, cyclic);
            }
        }

        foreach (FormModelDef model in models)
        {
            string fqn = Fqn(model);
            if (!cyclic.Contains(fqn))
            {
                continue;
            }

            // Approximation: lists every model this DFS pass found cyclic, not
            // necessarily only this node's own cycle (a rare over-broad message in a
            // graph with multiple independent cycles) — acceptable for compile-error
            // text, since the diagnostic's job is "this must be fixed," not a precise path.
            string others = string.Join(", ", cyclic.Where(f => f != fqn)
                .Select(f => byFqn.TryGetValue(f, out FormModelDef? m) ? m.TypeName : f));
            yield return Diagnostic.Create(Cycle, Location.None, model.TypeName, others);
        }
    }

    private static void Visit(
        string fqn, Dictionary<string, FormModelDef> byFqn, Dictionary<string, int> color,
        List<string> stack, HashSet<string> cyclic)
    {
        color[fqn] = 1;
        stack.Add(fqn);

        if (byFqn.TryGetValue(fqn, out FormModelDef? model))
        {
            foreach (string referenced in ReferencedFqns(model))
            {
                if (!color.TryGetValue(referenced, out int c))
                {
                    Visit(referenced, byFqn, color, stack, cyclic);
                }
                else if (c == 1)
                {
                    // Back-edge to a node still on the stack: every node on the stack
                    // from `referenced` onward (inclusive) is part of this cycle.
                    int start = stack.IndexOf(referenced);
                    for (int i = start; i < stack.Count; i++)
                    {
                        cyclic.Add(stack[i]);
                    }
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        color[fqn] = 2;
    }

    private static IEnumerable<string> ReferencedFqns(FormModelDef model)
    {
        foreach (FormFieldDef field in model.Fields)
        {
            if (field.NestedTypeFqn is { } fqn)
            {
                yield return fqn;
            }
        }
    }

    // Matches the "global::Ns.Type" shape FormModelAnalysis.ReadFields captures into
    // FormFieldDef.NestedTypeFqn via SymbolDisplayFormat.FullyQualifiedFormat, so a
    // model's own key and its fields' NestedTypeFqn references compare equal.
    private static string Fqn(FormModelDef model) =>
        model.Namespace is null ? $"global::{model.TypeName}" : $"global::{model.Namespace}.{model.TypeName}";
}

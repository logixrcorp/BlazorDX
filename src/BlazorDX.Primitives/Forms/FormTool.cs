using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazorDX.Primitives.Forms;

/// <summary>
/// Projects a <see cref="IFormModel{TModel}"/> into an AI tool and back: it emits a
/// JSON-Schema tool definition an AI host can call (the shape shared by the Model
/// Context Protocol and OpenAI/Anthropic function-calling), and applies a tool call's
/// JSON arguments onto a model instance — then validates with the very same rules the
/// rendered form uses. So a form a person fills and a tool an AI invokes are one thing.
/// All JSON is built by hand / parsed with <see cref="JsonDocument"/> — no reflection,
/// AOT- and trim-safe.
/// </summary>
public static class FormTool
{
    // Cycle safety net: DX2006 guarantees the Object/Array field-kind graph is
    // acyclic for anything that actually compiles, so this recursion always
    // terminates in practice. The depth guard is defensive insurance only — e.g.
    // against a hand-rolled IFormModelUntyped implementer that bypasses the
    // generator entirely — not something a normal [DxFormModel] graph can reach.
    private const int MaxRecursionDepth = 16;

    /// <summary>
    /// Builds the JSON-Schema <c>object</c> describing the model's parameters
    /// (types, constraints, descriptions, and the required set) — recursing into
    /// nested/array-of-nested descriptors for an Object/Array field.
    /// </summary>
    public static string BuildInputSchema<TModel>(IFormModel<TModel> model)
    {
        StringBuilder sb = new();
        AppendObjectSchema(sb, model, 0, null);
        return sb.ToString();
    }

    // The full JSON-Schema "object" shape (properties/required/allOf) — shared by the
    // top-level tool schema and, recursively, by an Object field's own nested schema,
    // since both are structurally identical.
    private static void AppendObjectSchema(StringBuilder sb, IFormModelUntyped model, int depth, string? description)
    {
        sb.Append("{\"type\":\"object\"");
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append(",\"description\":");
            AppendString(sb, description!);
        }

        sb.Append(",\"properties\":{");
        bool first = true;
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive)
            {
                continue;   // never describe a sensitive field to the AI
            }

            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            AppendProperty(sb, field, model, depth);
        }

        sb.Append("},\"required\":[");
        bool firstReq = true;
        foreach (FormFieldInfo field in model.Fields)
        {
            // A conditionally-required field is never in the unconditional "required" set —
            // its requiredness is expressed per-condition below instead.
            if (!field.Required || field.Sensitive || field.DependsOn is not null)
            {
                continue;
            }

            if (!firstReq)
            {
                sb.Append(',');
            }

            firstReq = false;
            AppendString(sb, field.Name);
        }

        sb.Append(']');
        AppendConditionalRequired(sb, model);
        sb.Append('}');
    }

    // Fields that are both conditional and Required get a JSON-Schema "if this
    // dependency holds, then this field is required" clause. Only draft-07+'s if/then
    // idiom expresses "required when X has a specific value" (dependentRequired only
    // expresses co-presence, not value-conditioned requirement) -- and since
    // BuildInputSchema declares no "$schema" draft, adding it is not a version bump:
    // any consumer that only reads properties/required (today's whole shape) simply
    // ignores the unknown keyword and the field looks like any other optional field, a
    // graceful degrade for hosts that don't evaluate conditionals. JSON Schema allows
    // only one top-level "if" per schema object, so multiple such fields collect under
    // one "allOf". This is advisory, not the enforcement boundary -- see
    // ApplyArguments, which is.
    private static void AppendConditionalRequired(StringBuilder sb, IFormModelUntyped model)
    {
        bool any = false;
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive || !field.Required || field.DependsOn is null)
            {
                continue;
            }

            FormFieldInfo? dependsOn = Find(model, field.DependsOn);
            if (dependsOn is null || dependsOn.Sensitive)
            {
                continue;   // shouldn't happen (DX2001/DX2002 forbid it at compile time); skip defensively
            }

            // The leading comma separates "allOf" from the preceding "required" property;
            // subsequent iterations need a comma between allOf entries instead.
            sb.Append(any ? ',' : ",\"allOf\":[");
            any = true;
            sb.Append("{\"if\":{\"properties\":{");
            AppendString(sb, dependsOn.Name);
            sb.Append(':');
            AppendConditionLiteral(sb, dependsOn, field.DependsOnOperator, field.DependsOnValue);
            sb.Append("}},\"then\":{\"required\":[");
            AppendString(sb, field.Name);
            sb.Append("]}}");
        }

        if (any)
        {
            sb.Append(']');
        }
    }

    // The JSON literal form of a DependsOn comparison, matching the dependency field's
    // own declared JSON type (string for Enum/Text/Multiline/Date, bare true/false for
    // Bool, bare number for Integer/Number).
    private static void AppendConditionLiteral(
        StringBuilder sb, FormFieldInfo dependsOn, FormFieldDependsOnOperator op, string? value)
    {
        if (op == FormFieldDependsOnOperator.NotEmpty)
        {
            // No exact JSON-Schema equivalent of "non-whitespace" -- minLength is the
            // closest native constraint. Documented divergence: a whitespace-only value
            // satisfies the runtime's IsNullOrWhiteSpace check but not this schema.
            sb.Append("{\"minLength\":1}");
            return;
        }

        if (op == FormFieldDependsOnOperator.NotEquals)
        {
            sb.Append("{\"not\":{\"const\":");
            AppendConstValue(sb, dependsOn.Kind, value);
            sb.Append("}}");
            return;
        }

        sb.Append("{\"const\":");
        AppendConstValue(sb, dependsOn.Kind, value);
        sb.Append('}');
    }

    private static void AppendConstValue(StringBuilder sb, FormFieldKind kind, string? value)
    {
        switch (kind)
        {
            case FormFieldKind.Bool:
                sb.Append(string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ? "true" : "false");
                break;
            case FormFieldKind.Integer or FormFieldKind.Number:
                sb.Append(value is null ? "null" : value);
                break;
            default:
                AppendString(sb, value ?? string.Empty);
                break;
        }
    }

    private static string ConditionDescription(FormFieldInfo dependsOn, FormFieldDependsOnOperator op, string? value) =>
        op switch
        {
            FormFieldDependsOnOperator.NotEmpty => $"Only applicable when {dependsOn.Label} is set.",
            FormFieldDependsOnOperator.NotEquals => $"Only applicable when {dependsOn.Label} is not {value}.",
            _ => $"Only applicable when {dependsOn.Label} is {value}.",
        };

    private static FormFieldInfo? Find(IFormModelUntyped model, string name)
    {
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the full tool definition envelope <c>{ name, description, input_schema }</c>
    /// (MCP / Anthropic shape; OpenAI nests the same schema under <c>parameters</c>).
    /// </summary>
    public static string BuildToolDefinition<TModel>(IFormModel<TModel> model)
    {
        StringBuilder sb = new();
        sb.Append('{');
        sb.Append("\"name\":");
        AppendString(sb, model.ToolName);
        sb.Append(",\"description\":");
        AppendString(sb, model.ToolDescription ?? string.Empty);
        sb.Append(",\"input_schema\":");
        sb.Append(BuildInputSchema(model));
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Applies an AI tool call's JSON arguments to <paramref name="target"/> (only the
    /// fields present are set), then returns the validation result. Invalid JSON yields
    /// a single error rather than throwing.
    /// </summary>
    public static IReadOnlyList<FormValidationError> ApplyArguments<TModel>(
        IFormModel<TModel> model, TModel target, string argumentsJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException ex)
        {
            return new[] { new FormValidationError(string.Empty, $"Invalid tool arguments: {ex.Message}") };
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { new FormValidationError(string.Empty, "Tool arguments must be a JSON object.") };
            }

            ApplyObject(model, target!, root, 0);
        }

        return model.Validate(target);
    }

    // The recursive core: three passes over IFormModelUntyped/object so it can call
    // itself for a nested/array-element descriptor without generic gymnastics.
    private static void ApplyObject(IFormModelUntyped model, object target, JsonElement root, int depth)
    {
        // Pass 1, unconditional scalar fields: a conditional field's activity depends
        // on its DependsOn field's value *from this same call*, not a stale
        // previously-persisted one, so every unconditional field must be applied
        // first. No chained DependsOn can reach here (DX2003 forbids it at compile
        // time), so pass 2 never depends on another pass-2 result.
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive || field.DependsOn is not null || field.Kind is FormFieldKind.Object or FormFieldKind.Array)
            {
                continue;
            }

            SetScalarFromJson(model, target, root, field);
        }

        // Pass 2, conditional scalar fields, re-checked against the now-updated
        // target -- same posture as the Sensitive skip: the schema's if/then is
        // advisory (many function-calling hosts don't guarantee they evaluate it),
        // this is the real enforcement boundary.
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive || field.DependsOn is null || field.Kind is FormFieldKind.Object or FormFieldKind.Array)
            {
                continue;
            }

            if (!FormFieldActivity.IsActive(model, target, field))
            {
                continue;   // conditionally inactive -- the AI cannot set it via this call
            }

            SetScalarFromJson(model, target, root, field);
        }

        if (depth >= MaxRecursionDepth)
        {
            return;
        }

        // Pass 3, Object/Array fields. DX2007 guarantees neither kind ever
        // participates in DependsOn (as source or target), so this pass has no
        // activity gating and no ordering dependency on passes 1-2.
        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive)
            {
                continue;
            }

            if (field.Kind == FormFieldKind.Object)
            {
                ApplyNestedField(model, target, root, field, depth);
            }
            else if (field.Kind == FormFieldKind.Array)
            {
                ApplyArrayField(model, target, root, field, depth);
            }
        }
    }

    // Materializes a currently-null nested instance the same way rendering does
    // (new TNested(), guaranteed constructible by DX2009), then recurses.
    private static void ApplyNestedField(IFormModelUntyped model, object target, JsonElement root, FormFieldInfo field, int depth)
    {
        if (!root.TryGetProperty(field.Name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        IFormModelUntyped? nestedDescriptor = model.GetNestedDescriptor(field.Name);
        if (nestedDescriptor is null)
        {
            return;
        }

        object instance = model.GetNestedInstance(target, field.Name) ?? model.NewNestedInstance(field.Name);
        model.SetNestedInstance(target, field.Name, instance);
        ApplyObject(nestedDescriptor, instance, value, depth + 1);
    }

    // Whole-collection replacement: the simplest correct semantic for a JSON payload
    // that carries no natural per-element identity to merge against.
    private static void ApplyArrayField(IFormModelUntyped model, object target, JsonElement root, FormFieldInfo field, int depth)
    {
        if (!root.TryGetProperty(field.Name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        if (field.NestedType is not null)
        {
            IFormModelUntyped? elementDescriptor = model.GetArrayElementDescriptor(field.Name);
            if (elementDescriptor is null)
            {
                return;
            }

            List<object> items = new();
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                object item = model.NewArrayElement(field.Name);
                ApplyObject(elementDescriptor, item, element, depth + 1);
                items.Add(item);
            }

            model.SetArrayInstances(target, field.Name, items);
        }
        else
        {
            List<string> items = new();
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                items.Add(element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.GetRawText());
            }

            model.SetArrayStrings(target, field.Name, items);
        }
    }

    private static void SetScalarFromJson(IFormModelUntyped model, object target, JsonElement root, FormFieldInfo field)
    {
        if (!root.TryGetProperty(field.Name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        // String values come through unquoted; numbers/booleans use their JSON
        // literal text, which the generated typed setter parses invariantly.
        string raw = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        model.SetString(target, field.Name, raw);
    }

    private static void AppendProperty(StringBuilder sb, FormFieldInfo field, IFormModelUntyped model, int depth)
    {
        AppendString(sb, field.Name);
        sb.Append(':');

        if (field.Kind == FormFieldKind.Object)
        {
            AppendObjectPropertySchema(sb, field, model, depth);
            return;
        }

        if (field.Kind == FormFieldKind.Array)
        {
            AppendArrayPropertySchema(sb, field, model, depth);
            return;
        }

        sb.Append('{');

        // type (+ format for dates)
        sb.Append("\"type\":");
        AppendString(sb, JsonType(field.Kind));
        if (field.Kind == FormFieldKind.Date)
        {
            sb.Append(",\"format\":\"date\"");
        }

        // A conditional field's own description gets a plain-English clause appended
        // regardless of whether it's Required -- cheap, and often the more reliable
        // signal in practice: many function-calling hosts (OpenAI/Anthropic) implement
        // only a subset of JSON Schema and don't guarantee they evaluate the allOf/if/
        // then clause below at all, but every host reads "description".
        string? description = field.Description;
        if (field.DependsOn is not null && Find(model, field.DependsOn) is { } dependsOn)
        {
            string clause = ConditionDescription(dependsOn, field.DependsOnOperator, field.DependsOnValue);
            description = string.IsNullOrEmpty(description) ? clause : $"{description} {clause}";
        }

        if (!string.IsNullOrEmpty(description))
        {
            sb.Append(",\"description\":");
            AppendString(sb, description!);
        }

        if (field.Kind is FormFieldKind.Integer or FormFieldKind.Number)
        {
            if (field.Min is { } min)
            {
                sb.Append(",\"minimum\":").Append(min.ToString(CultureInfo.InvariantCulture));
            }

            if (field.Max is { } max)
            {
                sb.Append(",\"maximum\":").Append(max.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (field.Kind is FormFieldKind.Text or FormFieldKind.Multiline)
        {
            if (field.MaxLength is { } maxLength)
            {
                sb.Append(",\"maxLength\":").Append(maxLength.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(field.Pattern))
            {
                sb.Append(",\"pattern\":");
                AppendString(sb, field.Pattern!);
            }
        }

        if (field.Kind == FormFieldKind.Enum && field.Choices is { Count: > 0 })
        {
            sb.Append(",\"enum\":[");
            for (int i = 0; i < field.Choices.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                AppendString(sb, field.Choices[i]);
            }

            sb.Append(']');
        }

        sb.Append('}');
    }

    // An Object field's schema: its nested descriptor's own object schema, recursed —
    // DX2006/2008/2009 guarantee GetNestedDescriptor never returns null for a field
    // that actually compiled, but a defensive fallback keeps this from ever emitting
    // invalid JSON if a hand-rolled IFormModelUntyped implementer violates that.
    private static void AppendObjectPropertySchema(StringBuilder sb, FormFieldInfo field, IFormModelUntyped model, int depth)
    {
        IFormModelUntyped? nested = depth < MaxRecursionDepth ? model.GetNestedDescriptor(field.Name) : null;
        if (nested is null)
        {
            sb.Append("{\"type\":\"object\"");
            if (!string.IsNullOrEmpty(field.Description))
            {
                sb.Append(",\"description\":");
                AppendString(sb, field.Description!);
            }

            sb.Append('}');
            return;
        }

        AppendObjectSchema(sb, nested, depth + 1, field.Description);
    }

    // An Array field's schema: "items" is either the array-of-nested element's own
    // object schema (recursed the same way as an Object field) or a plain scalar
    // schema built from ArrayElementKind/Choices.
    private static void AppendArrayPropertySchema(StringBuilder sb, FormFieldInfo field, IFormModelUntyped model, int depth)
    {
        sb.Append("{\"type\":\"array\"");
        if (!string.IsNullOrEmpty(field.Description))
        {
            sb.Append(",\"description\":");
            AppendString(sb, field.Description!);
        }

        sb.Append(",\"items\":");

        if (field.NestedType is not null)
        {
            IFormModelUntyped? element = depth < MaxRecursionDepth ? model.GetArrayElementDescriptor(field.Name) : null;
            if (element is null)
            {
                sb.Append("{\"type\":\"object\"}");
            }
            else
            {
                AppendObjectSchema(sb, element, depth + 1, null);
            }
        }
        else
        {
            FormFieldKind elementKind = field.ArrayElementKind ?? FormFieldKind.Text;
            sb.Append("{\"type\":");
            AppendString(sb, JsonType(elementKind));
            if (elementKind == FormFieldKind.Date)
            {
                sb.Append(",\"format\":\"date\"");
            }

            if (elementKind == FormFieldKind.Enum && field.Choices is { Count: > 0 })
            {
                sb.Append(",\"enum\":[");
                for (int i = 0; i < field.Choices.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    AppendString(sb, field.Choices[i]);
                }

                sb.Append(']');
            }

            sb.Append('}');
        }

        sb.Append('}');
    }

    private static string JsonType(FormFieldKind kind) => kind switch
    {
        FormFieldKind.Integer => "integer",
        FormFieldKind.Number => "number",
        FormFieldKind.Bool => "boolean",
        FormFieldKind.Object => "object",
        FormFieldKind.Array => "array",
        _ => "string",
    };

    // Minimal RFC 8259 string escaping.
    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}

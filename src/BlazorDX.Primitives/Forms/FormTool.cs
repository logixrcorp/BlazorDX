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
    /// <summary>
    /// Builds the JSON-Schema <c>object</c> describing the model's parameters
    /// (types, constraints, descriptions, and the required set).
    /// </summary>
    public static string BuildInputSchema<TModel>(IFormModel<TModel> model)
    {
        StringBuilder sb = new();
        sb.Append("{\"type\":\"object\",\"properties\":{");

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
            AppendProperty(sb, field, model);
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
        return sb.ToString();
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
    private static void AppendConditionalRequired<TModel>(StringBuilder sb, IFormModel<TModel> model)
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

            sb.Append(any ? ',' : "\"allOf\":[");
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

    private static FormFieldInfo? Find<TModel>(IFormModel<TModel> model, string name)
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

            // Two passes, deliberately in this order: a conditional field's activity
            // depends on its DependsOn field's value *from this same call*, not a stale
            // previously-persisted one, so every unconditional field must be applied
            // first. Pass 2 then re-checks activity against the now-updated target and
            // silently skips a conditionally-inactive field -- same posture as the
            // Sensitive skip above: the schema's if/then is advisory (many function-
            // calling hosts don't guarantee they evaluate it), this is the real
            // enforcement boundary. No chained DependsOn can reach here (DX2003 forbids
            // it at compile time), so pass 2 never depends on another pass-2 result.
            foreach (FormFieldInfo field in model.Fields)
            {
                if (field.Sensitive || field.DependsOn is not null)
                {
                    continue;
                }

                SetFromJson(model, target, root, field);
            }

            foreach (FormFieldInfo field in model.Fields)
            {
                if (field.Sensitive || field.DependsOn is null)
                {
                    continue;
                }

                if (!FormFieldActivity.IsActive(model, target, field))
                {
                    continue;   // conditionally inactive -- the AI cannot set it via this call
                }

                SetFromJson(model, target, root, field);
            }
        }

        return model.Validate(target);
    }

    private static void SetFromJson<TModel>(IFormModel<TModel> model, TModel target, JsonElement root, FormFieldInfo field)
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

    private static void AppendProperty<TModel>(StringBuilder sb, FormFieldInfo field, IFormModel<TModel> model)
    {
        AppendString(sb, field.Name);
        sb.Append(":{");

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

    private static string JsonType(FormFieldKind kind) => kind switch
    {
        FormFieldKind.Integer => "integer",
        FormFieldKind.Number => "number",
        FormFieldKind.Bool => "boolean",
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

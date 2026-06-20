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
            AppendProperty(sb, field);
        }

        sb.Append("},\"required\":[");
        bool firstReq = true;
        foreach (FormFieldInfo field in model.Fields)
        {
            if (!field.Required || field.Sensitive)
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

        sb.Append("]}");
        return sb.ToString();
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

            foreach (FormFieldInfo field in model.Fields)
            {
                if (field.Sensitive)
                {
                    continue;   // the hard gate: AI arguments can never set a sensitive field
                }

                if (!root.TryGetProperty(field.Name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                // String values come through unquoted; numbers/booleans use their JSON
                // literal text, which the generated typed setter parses invariantly.
                string raw = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.GetRawText();
                model.SetString(target, field.Name, raw);
            }
        }

        return model.Validate(target);
    }

    private static void AppendProperty(StringBuilder sb, FormFieldInfo field)
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

        if (!string.IsNullOrEmpty(field.Description))
        {
            sb.Append(",\"description\":");
            AppendString(sb, field.Description!);
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

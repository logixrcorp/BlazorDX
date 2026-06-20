using System.Linq;

namespace BlazorDX.Primitives.Forms;

/// <summary>The outcome of an AI tool invocation: a text payload and whether it is an error.</summary>
public sealed record AiToolResult(bool IsError, string Text);

/// <summary>
/// A callable AI tool: a name, a description, a JSON-Schema for its arguments, and an
/// async invocation. <see cref="McpToolServer"/> exposes a set of these over the Model
/// Context Protocol so an assistant can call them.
/// </summary>
public interface IAiTool
{
    string Name { get; }
    string? Description { get; }

    /// <summary>The JSON-Schema <c>object</c> describing the tool's arguments.</summary>
    string InputSchemaJson { get; }

    /// <summary>Runs the tool with a JSON object of arguments.</summary>
    Task<AiToolResult> InvokeAsync(string argumentsJson);
}

/// <summary>
/// Turns a <see cref="IFormModel{TModel}"/> into a callable <see cref="IAiTool"/>: an AI
/// call's arguments are applied to a fresh model and validated with the form's own rules;
/// if valid, <paramref name="handler"/> runs and its text is returned, otherwise the
/// validation errors are returned as an error result so the model can correct itself.
/// </summary>
public sealed class FormAiTool<TModel> : IAiTool
{
    private readonly IFormModel<TModel> descriptor;
    private readonly Func<TModel> factory;
    private readonly Func<TModel, Task<string>> handler;

    public FormAiTool(IFormModel<TModel> descriptor, Func<TModel> factory, Func<TModel, Task<string>> handler)
    {
        this.descriptor = descriptor;
        this.factory = factory;
        this.handler = handler;
    }

    public string Name => descriptor.ToolName;
    public string? Description => descriptor.ToolDescription;
    public string InputSchemaJson => FormTool.BuildInputSchema(descriptor);

    public async Task<AiToolResult> InvokeAsync(string argumentsJson)
    {
        TModel target = factory();
        IReadOnlyList<FormValidationError> errors = FormTool.ApplyArguments(descriptor, target, argumentsJson);
        if (errors.Count > 0)
        {
            string detail = string.Join("; ", errors.Select(e =>
                string.IsNullOrEmpty(e.Field) ? e.Message : $"{e.Field}: {e.Message}"));
            return new AiToolResult(true, "Validation failed: " + detail);
        }

        return new AiToolResult(false, await handler(target));
    }
}

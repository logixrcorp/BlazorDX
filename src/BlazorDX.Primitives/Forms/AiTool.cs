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

    /// <summary>Whether the tool only reads (safe to auto-run) vs. mutates state (treat with care).</summary>
    bool IsReadOnly { get; }

    /// <summary>Runs the tool with a JSON object of arguments. The token cancels in-flight work.</summary>
    Task<AiToolResult> InvokeAsync(string argumentsJson, CancellationToken cancellationToken);
}

/// <summary>
/// Decides whether the current caller may see and invoke a given <see cref="IAiTool"/>. A
/// host implements this over its own auth (e.g. an ASP.NET Core <c>ClaimsPrincipal</c> +
/// authorization policies). <see cref="McpToolServer"/> consults it for every
/// <c>tools/list</c> and <c>tools/call</c>, so a tool the caller may not use is never
/// advertised and never runs. When no authorizer is supplied, all tools are allowed.
/// </summary>
public interface IAiToolAuthorizer
{
    ValueTask<bool> IsAllowedAsync(IAiTool tool, CancellationToken cancellationToken);
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
    private readonly Func<TModel, CancellationToken, Task<string>> handler;

    public FormAiTool(
        IFormModel<TModel> descriptor,
        Func<TModel> factory,
        Func<TModel, CancellationToken, Task<string>> handler,
        bool isReadOnly = false)
    {
        this.descriptor = descriptor;
        this.factory = factory;
        this.handler = handler;
        IsReadOnly = isReadOnly;
    }

    public string Name => descriptor.ToolName;
    public string? Description => descriptor.ToolDescription;
    public string InputSchemaJson => FormTool.BuildInputSchema(descriptor);

    /// <summary>A form tool writes by default; pass <c>isReadOnly: true</c> for query-only forms.</summary>
    public bool IsReadOnly { get; }

    public async Task<AiToolResult> InvokeAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        TModel target = factory();
        IReadOnlyList<FormValidationError> errors = FormTool.ApplyArguments(descriptor, target, argumentsJson);
        if (errors.Count > 0)
        {
            string detail = string.Join("; ", errors.Select(e =>
                string.IsNullOrEmpty(e.Field) ? e.Message : $"{e.Field}: {e.Message}"));
            return new AiToolResult(true, "Validation failed: " + detail);
        }

        return new AiToolResult(false, await handler(target, cancellationToken));
    }
}

using System.Globalization;
using System.Text;

namespace BlazorDX.Primitives.Forms;

/// <summary>One argument a prompt accepts.</summary>
/// <param name="Name">Argument name, as the client sends it.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Required">Whether the client must supply it.</param>
public sealed record AiPromptArgument(string Name, string? Description, bool Required = false);

/// <summary>One message in an expanded prompt.</summary>
/// <param name="Role">Either <c>user</c> or <c>assistant</c>.</param>
/// <param name="Text">The message text.</param>
public sealed record AiPromptMessage(string Role, string Text)
{
    /// <summary>A message from the user — what nearly every prompt expands to.</summary>
    public static AiPromptMessage User(string text) => new("user", text);

    /// <summary>A message attributed to the assistant, for priming a a multi-turn exchange.</summary>
    public static AiPromptMessage Assistant(string text) => new("assistant", text);
}

/// <summary>An expanded prompt: what the client inserts into the conversation.</summary>
/// <param name="Description">Shown to the user alongside the expansion.</param>
/// <param name="Messages">The messages, in order.</param>
public sealed record AiPromptResult(string? Description, IReadOnlyList<AiPromptMessage> Messages);

/// <summary>
/// A named, user-invoked template — what a client surfaces as a slash-command or a "prompts" menu.
/// </summary>
/// <remarks>
/// The direction of control is what separates this from <see cref="IAiTool"/>. A tool is chosen by
/// the model; a prompt is chosen by the <em>person</em>, and expands into the conversation before
/// the model does anything. So a prompt's job is to set a task up well — including pointing at the
/// tools that should do the work.
/// </remarks>
public interface IAiPrompt
{
    /// <summary>The name the client shows and sends, e.g. <c>schedule_meeting</c>.</summary>
    string Name { get; }

    /// <summary>What invoking it does.</summary>
    string? Description { get; }

    /// <summary>Arguments the client may collect from the user first.</summary>
    IReadOnlyList<AiPromptArgument> Arguments { get; }

    /// <summary>Expands the prompt. Missing optional arguments are absent from the dictionary.</summary>
    Task<AiPromptResult> GetAsync(IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken);
}

/// <summary>A prompt built by a delegate.</summary>
/// <param name="name">The prompt name.</param>
/// <param name="description">What invoking it does.</param>
/// <param name="arguments">Arguments the client may collect.</param>
/// <param name="expand">Builds the messages from the supplied arguments.</param>
public sealed class TextAiPrompt(
    string name,
    string? description,
    IReadOnlyList<AiPromptArgument> arguments,
    Func<IReadOnlyDictionary<string, string>, CancellationToken, Task<IReadOnlyList<AiPromptMessage>>> expand)
    : IAiPrompt
{
    /// <inheritdoc />
    public string Name { get; } = !string.IsNullOrWhiteSpace(name)
        ? name
        : throw new ArgumentException("A prompt name is required.", nameof(name));

    /// <inheritdoc />
    public string? Description { get; } = description;

    /// <inheritdoc />
    public IReadOnlyList<AiPromptArgument> Arguments { get; } = arguments ?? [];

    /// <inheritdoc />
    public async Task<AiPromptResult> GetAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken) =>
        new(Description, await expand(arguments, cancellationToken).ConfigureAwait(false));
}

/// <summary>
/// Turns a <c>[DxFormModel]</c> into a user-invokable prompt that sets up filling that form —
/// the same descriptor that renders the form and projects the tool, on a third surface.
/// </summary>
/// <remarks>
/// <para>
/// Pair it with the matching <see cref="FormAiTool{TModel}"/>. The prompt is what a person reaches
/// for ("/schedule a meeting"); it states the task, lists the fields with their real constraints,
/// and names the tool that submits. Without it a user has to know the tool exists and describe the
/// fields themselves.
/// </para>
/// <para>
/// <b>Sensitive fields are omitted</b>, exactly as they are from the tool schema. A field marked
/// <c>[DxField(Sensitive = true)]</c> is one the model must never see or ask for, and a prompt
/// that helpfully listed it would reintroduce the leak the tool surface was careful to avoid.
/// </para>
/// </remarks>
/// <typeparam name="TModel">The annotated model type.</typeparam>
/// <param name="model">The source-generated descriptor.</param>
/// <param name="name">Prompt name; defaults to the model's tool name.</param>
public sealed class FormAiPrompt<TModel>(IFormModel<TModel> model, string? name = null) : IAiPrompt
{
    /// <inheritdoc />
    public string Name { get; } = name ?? model.ToolName;

    /// <inheritdoc />
    public string? Description { get; } = model.ToolDescription;

    /// <inheritdoc />
    public IReadOnlyList<AiPromptArgument> Arguments { get; } =
    [
        .. model.Fields
            .Where(field => !field.Sensitive)
            // Every argument is optional: the prompt's purpose is to start the task, and a client
            // that demanded each required field up front would just be the form again, in a worse
            // renderer. Whatever the user does not supply, the assistant asks for.
            .Select(field => new AiPromptArgument(field.Name, field.Description ?? field.Label)),
    ];

    /// <inheritdoc />
    public Task<AiPromptResult> GetAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder sb = new();
        sb.Append("Help me complete ").Append(Description ?? Name).Append('.').Append('\n');
        sb.Append("Call the \"").Append(model.ToolName).Append("\" tool once every required value is known.\n\n");
        sb.Append("Fields:\n");

        foreach (FormFieldInfo field in model.Fields)
        {
            if (field.Sensitive)
            {
                continue;
            }

            sb.Append("- ").Append(field.Label);
            if (field.Required)
            {
                sb.Append(" (required)");
            }

            AppendConstraints(sb, field);

            if (arguments.TryGetValue(field.Name, out string? supplied) && !string.IsNullOrWhiteSpace(supplied))
            {
                sb.Append(" — already given: ").Append(supplied);
            }

            sb.Append('\n');
        }

        return Task.FromResult(new AiPromptResult(Description, [AiPromptMessage.User(sb.ToString())]));
    }

    // The constraints come from the descriptor rather than being restated in prose, so a rule
    // changed on the model cannot drift out of sync with what the prompt claims.
    private static void AppendConstraints(StringBuilder sb, FormFieldInfo field)
    {
        List<string> parts = [];

        if (field.Choices is { Count: > 0 })
        {
            parts.Add("one of: " + string.Join(", ", field.Choices));
        }

        if (field.Min is not null || field.Max is not null)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"range {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "any"}–{field.Max?.ToString(CultureInfo.InvariantCulture) ?? "any"}"));
        }

        if (field.MaxLength is not null)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"at most {field.MaxLength} characters"));
        }

        if (!string.IsNullOrWhiteSpace(field.Description))
        {
            parts.Add(field.Description);
        }

        if (parts.Count > 0)
        {
            sb.Append(": ").Append(string.Join("; ", parts));
        }
    }
}

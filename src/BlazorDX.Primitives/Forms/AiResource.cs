namespace BlazorDX.Primitives.Forms;

/// <summary>
/// The body of a resource: either text or bytes, never both, with the MIME type that says which
/// to expect.
/// </summary>
/// <remarks>
/// A record with two nullable payloads would let a caller construct a content that is neither or
/// both, so the constructor is private and the two factories are the only way in.
/// </remarks>
public sealed record AiResourceContent
{
    private AiResourceContent(string mimeType, string? text, ReadOnlyMemory<byte>? bytes)
    {
        MimeType = mimeType;
        Text = text;
        Bytes = bytes;
    }

    /// <summary>The MIME type, e.g. <c>text/markdown</c> or <c>image/png</c>.</summary>
    public string MimeType { get; }

    /// <summary>The text body, or <see langword="null"/> for a binary resource.</summary>
    public string? Text { get; }

    /// <summary>The binary body, or <see langword="null"/> for a text resource.</summary>
    public ReadOnlyMemory<byte>? Bytes { get; }

    /// <summary>A text resource.</summary>
    public static AiResourceContent FromText(string text, string mimeType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new AiResourceContent(mimeType, text, null);
    }

    /// <summary>A binary resource. Sent to the client base64-encoded, as the protocol requires.</summary>
    public static AiResourceContent FromBytes(ReadOnlyMemory<byte> bytes, string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        return new AiResourceContent(mimeType, null, bytes);
    }
}

/// <summary>
/// Something an assistant can <em>read</em> by URI — a document, a spec, a config, a report.
/// </summary>
/// <remarks>
/// <para>
/// The distinction from <see cref="IAiTool"/> is who decides to use it. A tool is called by the
/// model when it judges the moment right; a resource is attached by the <em>user</em>, or read on
/// request, and never runs anything. That is why a resource has no argument schema: there is
/// nothing to parameterise, only something to fetch.
/// </para>
/// <para>
/// The <see cref="Uri"/> is an identifier, not a path. Nothing here dereferences it, so a host
/// that maps URIs onto files owns the traversal check — the safe shape is a fixed dictionary of
/// URIs it chose, which is what <see cref="TextAiResource"/> encourages by taking a delegate
/// rather than a directory.
/// </para>
/// </remarks>
public interface IAiResource
{
    /// <summary>Stable identifier the client uses to read this resource, e.g. <c>docs://pricing</c>.</summary>
    string Uri { get; }

    /// <summary>Human-readable name, shown when a user picks a resource to attach.</summary>
    string Name { get; }

    /// <summary>What it contains. This is what tells a user, or a model, whether it is relevant.</summary>
    string? Description { get; }

    /// <summary>Declared MIME type for listings. The read may still answer a different one.</summary>
    string? MimeType { get; }

    /// <summary>Fetches the body.</summary>
    Task<AiResourceContent> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A resource backed by a delegate — the common case, where the body is produced on demand from
/// something the app already has.
/// </summary>
/// <param name="uri">Stable identifier, e.g. <c>docs://onboarding</c>.</param>
/// <param name="name">Human-readable name.</param>
/// <param name="description">What it contains.</param>
/// <param name="read">Produces the body. Called on every read, so it sees current data.</param>
/// <param name="mimeType">Declared MIME type.</param>
public sealed class TextAiResource(
    string uri,
    string name,
    string? description,
    Func<CancellationToken, Task<string>> read,
    string mimeType = "text/plain") : IAiResource
{
    /// <inheritdoc />
    public string Uri { get; } = !string.IsNullOrWhiteSpace(uri)
        ? uri
        : throw new ArgumentException("A resource URI is required.", nameof(uri));

    /// <inheritdoc />
    public string Name { get; } = !string.IsNullOrWhiteSpace(name)
        ? name
        : throw new ArgumentException("A resource name is required.", nameof(name));

    /// <inheritdoc />
    public string? Description { get; } = description;

    // Held as a field rather than read back off the property: MimeType is nullable on the
    // interface, and ReadAsync needs the non-null value it was constructed with.
    private readonly string mime = mimeType;

    /// <inheritdoc />
    public string? MimeType => mime;

    /// <inheritdoc />
    public async Task<AiResourceContent> ReadAsync(CancellationToken cancellationToken) =>
        AiResourceContent.FromText(await read(cancellationToken).ConfigureAwait(false), mime);
}

/// <summary>
/// Decides whether the current caller may see and read a given resource or prompt.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IAiToolAuthorizer"/> deliberately. Adding these to that interface
/// would break every host already implementing it, and the two decisions are not the same
/// question anyway: one is "may you run this", the other "may you read this".
/// </para>
/// <para>
/// <see cref="McpToolServer"/> consults it on every list and every fetch, and a disallowed item is
/// answered exactly as a non-existent one — so the surface never reveals that a privileged
/// resource exists, matching how tools already behave.
/// </para>
/// </remarks>
public interface IAiContentAuthorizer
{
    /// <summary>Whether the caller may list and read <paramref name="resource"/>.</summary>
    ValueTask<bool> IsAllowedAsync(IAiResource resource, CancellationToken cancellationToken);

    /// <summary>Whether the caller may list and fetch <paramref name="prompt"/>.</summary>
    ValueTask<bool> IsAllowedAsync(IAiPrompt prompt, CancellationToken cancellationToken);
}

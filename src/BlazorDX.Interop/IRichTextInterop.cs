namespace BlazorDX.Interop;

/// <summary>
/// Browser bridge for the WYSIWYG editor: applies formatting commands to a
/// contentEditable region and reads its live HTML back out. Only functional under
/// WebAssembly; the server uses a no-op implementation.
/// </summary>
public interface IRichTextInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Applies a formatting command (e.g. <c>bold</c>, <c>insertUnorderedList</c>).</summary>
    ValueTask ExecAsync(string command, string value);

    /// <summary>Returns the current inner HTML of the editor element.</summary>
    ValueTask<string> GetHtmlAsync(string elementId);

    /// <summary>Replaces the editor element's inner HTML (used to seed initial content).</summary>
    ValueTask SetHtmlAsync(string elementId, string html);

    /// <summary>Moves focus into the editor element.</summary>
    ValueTask FocusAsync(string elementId);
}

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the rich-text TypeScript module
/// (<c>richtext.js</c>) using <see cref="JSImportAttribute"/>. Only functional
/// under WebAssembly; the server uses <see cref="NullRichTextInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class RichTextInterop : IRichTextInterop
{
    private const string ModuleName = "dx/richtext.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/richtext.js";

    private bool isLoaded;

    public async ValueTask EnsureLoadedAsync()
    {
        if (isLoaded)
        {
            return;
        }

        await JSHost.ImportAsync(ModuleName, ModulePath);
        isLoaded = true;
    }

    public async ValueTask ExecAsync(string command, string value)
    {
        await EnsureLoadedAsync();
        Exec(command, value);
    }

    public async ValueTask CreateLinkAsync()
    {
        await EnsureLoadedAsync();
        CreateLink();
    }

    public async ValueTask<string> PromptLinkAsync()
    {
        await EnsureLoadedAsync();
        return PromptLink();
    }

    public async ValueTask ApplyColorAsync(string command, string color)
    {
        await EnsureLoadedAsync();
        ApplyColor(command, color);
    }

    public async ValueTask<int> FindInEditorAsync(string elementId, string query, bool forward, bool caseSensitive)
    {
        await EnsureLoadedAsync();
        return FindInEditor(elementId, query, forward, caseSensitive);
    }

    public async ValueTask<string> GetTableCellAsync(string elementId)
    {
        await EnsureLoadedAsync();
        return GetTableCell(elementId);
    }

    public async ValueTask<string> GetSelectionRangeAsync(string elementId)
    {
        await EnsureLoadedAsync();
        return GetSelectionRange(elementId);
    }

    public async ValueTask SetSelectionRangeAsync(string elementId, int containerIndex, int start, int end)
    {
        await EnsureLoadedAsync();
        SetSelectionRange(elementId, containerIndex, start, end);
    }

    public async ValueTask<string> PickImageAsync()
    {
        await EnsureLoadedAsync();
        return await PickImage();
    }

    public async ValueTask<string> GetHtmlAsync(string elementId)
    {
        await EnsureLoadedAsync();
        return GetHtml(elementId);
    }

    public async ValueTask SetHtmlAsync(string elementId, string html)
    {
        await EnsureLoadedAsync();
        SetHtml(elementId, html);
    }

    public async ValueTask FocusAsync(string elementId)
    {
        await EnsureLoadedAsync();
        FocusEditor(elementId);
    }

    public async ValueTask SubscribeShortcutsAsync(string elementId, Action<string> onShortcut)
    {
        await EnsureLoadedAsync();
        SubscribeShortcuts(elementId, onShortcut);
    }

    [JSImport("exec", ModuleName)]
    private static partial void Exec(string command, string value);

    [JSImport("createLink", ModuleName)]
    private static partial void CreateLink();

    [JSImport("promptLink", ModuleName)]
    private static partial string PromptLink();

    [JSImport("applyColor", ModuleName)]
    private static partial void ApplyColor(string command, string color);

    [JSImport("findInEditor", ModuleName)]
    private static partial int FindInEditor(string elementId, string query, bool forward, bool caseSensitive);

    [JSImport("getTableCell", ModuleName)]
    private static partial string GetTableCell(string elementId);

    [JSImport("getSelectionRange", ModuleName)]
    private static partial string GetSelectionRange(string elementId);

    [JSImport("setSelectionRange", ModuleName)]
    private static partial void SetSelectionRange(string elementId, int containerIndex, int start, int end);

    [JSImport("pickImage", ModuleName)]
    private static partial Task<string> PickImage();

    [JSImport("getHtml", ModuleName)]
    private static partial string GetHtml(string elementId);

    [JSImport("setHtml", ModuleName)]
    private static partial void SetHtml(string elementId, string html);

    [JSImport("focusEditor", ModuleName)]
    private static partial void FocusEditor(string elementId);

    [JSImport("subscribeShortcuts", ModuleName)]
    private static partial void SubscribeShortcuts(
        string elementId,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onShortcut);
}

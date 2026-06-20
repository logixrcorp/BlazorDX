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

    [JSImport("exec", ModuleName)]
    private static partial void Exec(string command, string value);

    [JSImport("getHtml", ModuleName)]
    private static partial string GetHtml(string elementId);

    [JSImport("setHtml", ModuleName)]
    private static partial void SetHtml(string elementId, string html);

    [JSImport("focusEditor", ModuleName)]
    private static partial void FocusEditor(string elementId);
}

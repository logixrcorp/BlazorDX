using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the file-manager drag-and-drop TypeScript module
/// (<c>file-dnd.js</c>) using <see cref="JSImportAttribute"/>. Only functional under
/// WebAssembly; the server uses <see cref="NullFileDndInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class FileDndInterop : IFileDndInterop
{
    private const string ModuleName = "dx/file-dnd.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/file-dnd.js";

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

    public async ValueTask RegisterDraggableAsync(string elementId)
    {
        await EnsureLoadedAsync();
        RegisterDraggable(elementId);
    }

    public async ValueTask RegisterDropTargetAsync(
        string elementId,
        Action<string, string> onMove,
        Action<IReadOnlyList<DroppedFile>> onFiles)
    {
        await EnsureLoadedAsync();

        // JS reports dropped OS files as a JSON array of {name,size,type} (the only
        // shape the [JSImport] function marshaler takes here — array-of-array params
        // are unsupported). Deserialized via a source-generated context, so no
        // reflection-based JSON enters the trimmed/AOT path.
        void OnFilesRaw(string filesJson)
        {
            DroppedFile[]? files;
            try
            {
                files = JsonSerializer.Deserialize(filesJson, FileDndJson.Default.DroppedFileArray);
            }
            catch (JsonException)
            {
                // A malformed payload from the browser must never crash the host; an
                // empty list is the safe equivalent of "nothing usable was dropped".
                onFiles([]);
                return;
            }

            if (files is null || files.Length == 0)
            {
                onFiles([]);
                return;
            }

            // Defensively filter the browser-reported names: an empty name, a name
            // carrying a path separator (a path-traversal vector if a host ever joins
            // it to a directory), or one with a null byte is dropped, not forwarded.
            List<DroppedFile> safe = new(files.Length);
            foreach (DroppedFile file in files)
            {
                if (IsSafeName(file.Name))
                {
                    safe.Add(file);
                }
            }

            onFiles(safe);
        }

        RegisterDropTarget(elementId, onMove, OnFilesRaw);
    }

    public async ValueTask UnregisterAsync(string elementId)
    {
        if (isLoaded)
        {
            Unregister(elementId);
        }

        await ValueTask.CompletedTask;
    }

    public async ValueTask FocusElementAsync(string elementId)
    {
        await EnsureLoadedAsync();
        FocusElement(elementId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // A browser-reported file name is usable only if it is non-empty and free of
    // path separators and null bytes. The name is untrusted host-side input (see the
    // remarks on DroppedFile), so this is the boundary that keeps a crafted name from
    // ever reaching code that might treat it as a path component.
    private static bool IsSafeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c is '/' or '\\' or '\0')
            {
                return false;
            }
        }

        return true;
    }

    [JSImport("registerDraggable", ModuleName)]
    private static partial void RegisterDraggable(string elementId);

    [JSImport("registerDropTarget", ModuleName)]
    private static partial void RegisterDropTarget(
        string elementId,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Action<string, string> onMove,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onFiles);

    [JSImport("unregister", ModuleName)]
    private static partial void Unregister(string elementId);

    [JSImport("focusElement", ModuleName)]
    private static partial void FocusElement(string elementId);
}

[JsonSerializable(typeof(DroppedFile[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FileDndJson : JsonSerializerContext;

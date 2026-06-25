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
            DroppedFile[]? files = JsonSerializer.Deserialize(filesJson, FileDndJson.Default.DroppedFileArray);
            onFiles(files ?? []);
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("registerDraggable", ModuleName)]
    private static partial void RegisterDraggable(string elementId);

    [JSImport("registerDropTarget", ModuleName)]
    private static partial void RegisterDropTarget(
        string elementId,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Action<string, string> onMove,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onFiles);

    [JSImport("unregister", ModuleName)]
    private static partial void Unregister(string elementId);
}

[JsonSerializable(typeof(DroppedFile[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FileDndJson : JsonSerializerContext;

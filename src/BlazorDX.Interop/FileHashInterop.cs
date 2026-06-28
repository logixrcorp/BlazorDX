using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the browser file-hash TypeScript module (<c>file-hash.js</c>) using
/// <see cref="JSImportAttribute"/>. Only functional under WebAssembly; the server uses
/// <see cref="NullFileHashInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class FileHashInterop : IFileHashInterop
{
    private const string ModuleName = "dx/file-hash.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/file-hash.js";

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

    public async ValueTask<IReadOnlyList<FileHashResult>> HashInputFilesAsync(string elementId, string algorithm)
    {
        await EnsureLoadedAsync();

        // JS returns a JSON array of {name,size,hash}; deserialized via a source-generated context so
        // no reflection-based JSON enters the trimmed/AOT path, and defensively so a malformed payload
        // yields an empty list rather than crashing the host.
        string json = await HashInputFiles(elementId, algorithm);
        try
        {
            FileHashResult[]? results = JsonSerializer.Deserialize(json, FileHashJson.Default.FileHashResultArray);
            return results ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("hashInputFiles", ModuleName)]
    private static partial Task<string> HashInputFiles(string elementId, string algorithm);
}

[JsonSerializable(typeof(FileHashResult[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FileHashJson : JsonSerializerContext;

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the scheduler drag TypeScript module (<c>scheduler.js</c>) using
/// <see cref="JSImportAttribute"/>. Only functional under WebAssembly; the server uses
/// <see cref="NullSchedulerInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class SchedulerInterop : ISchedulerInterop
{
    private const string ModuleName = "dx/scheduler.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/scheduler.js";

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

    public async ValueTask RegisterTimeGridAsync(
        string gridId,
        int dayCount,
        int startHour,
        int endHour,
        int hourHeight,
        Action<SchedulerDragResult> onDrag)
    {
        await EnsureLoadedAsync();

        // JS reports the completed gesture as a JSON object (a single string is the shape the
        // [JSImport] function marshaler accepts here). It is deserialized via a source-generated
        // context, so no reflection-based JSON enters the trimmed/AOT path, and parsed defensively:
        // a malformed payload from the browser must never crash the host.
        void OnDragRaw(string resultJson)
        {
            SchedulerDragPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize(resultJson, SchedulerInteropJson.Default.SchedulerDragPayload);
            }
            catch (JsonException)
            {
                return;
            }

            SchedulerDragKind kind = payload.Kind == "create" ? SchedulerDragKind.Create : SchedulerDragKind.Move;
            onDrag(new SchedulerDragResult(
                kind,
                payload.SourceIndex,
                payload.DayIndex,
                payload.StartHour,
                payload.EndHour));
        }

        RegisterTimeGrid(gridId, dayCount, startHour, endHour, hourHeight, OnDragRaw);
    }

    public async ValueTask UnregisterAsync(string gridId)
    {
        if (isLoaded)
        {
            Unregister(gridId);
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("registerTimeGrid", ModuleName)]
    private static partial void RegisterTimeGrid(
        string gridId,
        int dayCount,
        int startHour,
        int endHour,
        int hourHeight,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onDrag);

    [JSImport("unregister", ModuleName)]
    private static partial void Unregister(string gridId);
}

// The wire shape of a drag result. Kind is carried as a string ("move"/"create") and mapped to the
// public enum host-side, avoiding any enum-converter dependency on the trimmed JSON path.
internal readonly record struct SchedulerDragPayload(
    string Kind,
    int SourceIndex,
    int DayIndex,
    double StartHour,
    double EndHour);

[JsonSerializable(typeof(SchedulerDragPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SchedulerInteropJson : JsonSerializerContext;

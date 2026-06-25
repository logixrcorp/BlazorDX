using BlazorDX.Compute;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// HttpClient pointed at the app origin, so the docs site can fetch the staged
// XML doc comments (/_content/<lib>/api-docs.xml) for parameter descriptions.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Registers IGridCompute; in the browser this resolves to the Rust/wasm backend.
builder.Services.AddBlazorDXCompute();

// DxPowerBiReport's browser bridge (the [JSImport] wrapper over dx-powerbi.js).
// The embed token + embedUrl come from the server's /powerbi/embedconfig endpoint;
// only the interop is needed client-side — the AAD token never reaches WASM. Scoped,
// never Singleton, per the state-isolation rule. Guarded by IsBrowser so the
// browser-only [JSImport] type is registered only where it is supported (CA1416).
if (OperatingSystem.IsBrowser())
{
    builder.Services.AddScoped<BlazorDX.Integrations.PowerBI.IPowerBiInterop,
        BlazorDX.Integrations.PowerBI.PowerBiInterop>();
}

// Scoped toast service (never Singleton — per the state-isolation rule).
builder.Services.AddScoped<BlazorDX.Components.ToastService>();

// TicketDesk demo data — Scoped for the same state-isolation reason.
builder.Services.AddScoped<BlazorDX.Demo.Client.TicketDesk.TicketStore>();

// Demo observability sink: BlazorDX components report failures here (the /errors page shows them).
builder.Services.AddScoped<BlazorDX.Demo.Client.DemoDiagnosticsLog>();
builder.Services.AddScoped<BlazorDX.Primitives.Diagnostics.IDxDiagnostics>(
    sp => sp.GetRequiredService<BlazorDX.Demo.Client.DemoDiagnosticsLog>());

await builder.Build().RunAsync();

using BlazorDX.Compute;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// HttpClient pointed at the app origin, so the docs site can fetch the staged
// XML doc comments (/_content/<lib>/api-docs.xml) for parameter descriptions.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Registers IGridCompute; in the browser this resolves to the Rust/wasm backend.
builder.Services.AddBlazorDXCompute();

// Scoped toast service (never Singleton — per the state-isolation rule).
builder.Services.AddScoped<BlazorDX.Components.ToastService>();

// Demo observability sink: BlazorDX components report failures here (the /errors page shows them).
builder.Services.AddScoped<BlazorDX.Demo.Client.DemoDiagnosticsLog>();
builder.Services.AddScoped<BlazorDX.Primitives.Diagnostics.IDxDiagnostics>(
    sp => sp.GetRequiredService<BlazorDX.Demo.Client.DemoDiagnosticsLog>());

await builder.Build().RunAsync();

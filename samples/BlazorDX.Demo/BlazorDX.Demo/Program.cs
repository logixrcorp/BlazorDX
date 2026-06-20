using System.Text.Encodings.Web;
using BlazorDX.Compute;
using BlazorDX.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// IGridCompute for server-side prerender of the grid (resolves to managed C#).
builder.Services.AddBlazorDXCompute();

// Scoped toast service, also needed for server-side prerender of WASM pages.
builder.Services.AddScoped<BlazorDX.Components.ToastService>();

// Same observability sink registered server-side so the /errors page prerenders.
builder.Services.AddScoped<BlazorDX.Demo.Client.DemoDiagnosticsLog>();
builder.Services.AddScoped<BlazorDX.Primitives.Diagnostics.IDxDiagnostics>(
    sp => sp.GetRequiredService<BlazorDX.Demo.Client.DemoDiagnosticsLog>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorDX.Demo.Client._Imports).Assembly);

// Endpoint backing the HTMX tier demo: returns an HTML fragment that htmx swaps
// into the page. Antiforgery is disabled here only to keep the demo self-contained.
app.MapPost("/htmx/echo", (HttpContext http) =>
{
    string message = http.Request.Form["message"].ToString();
    string safe = HtmlEncoder.Default.Encode(message);
    string fragment = string.IsNullOrWhiteSpace(safe)
        ? "<p class=\"dx-echo\">Type a message and submit — no circuit, no WASM.</p>"
        : $"<p class=\"dx-echo\">Server received: <strong>{safe}</strong></p>";
    return Results.Content(fragment, "text/html");
}).DisableAntiforgery();

app.Run();

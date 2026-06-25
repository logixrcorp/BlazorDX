using BlazorDX.MockReportServer;

// Standalone host for the mock SSRS report server. It mounts the same endpoint
// extensions the demo app will mount in-process, so the emulation is identical
// in both places. Auth is off by default for easy local use; set the
// MOCKRS_AUTH=true environment variable (with optional MOCKRS_USER / MOCKRS_PASS)
// to exercise a client's Basic-auth pass-through.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var authEnabled = string.Equals(
    Environment.GetEnvironmentVariable("MOCKRS_AUTH"), "true", StringComparison.OrdinalIgnoreCase);

var auth = new MockReportServerAuthOptions
{
    Enabled = authEnabled,
    Username = Environment.GetEnvironmentVariable("MOCKRS_USER") ?? "report",
    Password = Environment.GetEnvironmentVariable("MOCKRS_PASS") ?? "viewer",
};

var catalog = new ReportCatalog();

app.MapMockReportServer("/ReportServer", catalog, auth);
app.MapMockReportsRestApi("/reports", catalog, auth);

app.MapGet("/", () => Results.Text(
    "Mock SSRS Report Server\n" +
    "  Render:       GET /ReportServer?/Sales/Monthly&rs:Command=Render&rs:Format=HTML5&Region=West\n" +
    "  ListChildren: GET /ReportServer?/Sales&rs:Command=ListChildren\n" +
    "  Parameters:   GET /reports/api/v2.0/reports(/Sales/Monthly)/parameterdefinitions\n" +
    "                GET /mock/parameters?report=/Sales/Monthly\n",
    "text/plain"));

app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot the app.
public partial class Program;

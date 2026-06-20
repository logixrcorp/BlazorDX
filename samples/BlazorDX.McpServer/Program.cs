using BlazorDX.McpServer;
using BlazorDX.Primitives.Forms;

// Register the [DxFormModel] as a callable tool and serve MCP over stdio. A real server would
// also pass an IAiToolAuthorizer (to gate tools per caller) and an IDxDiagnostics sink (to
// audit calls); both are optional and omitted here to keep the sample minimal.
var server = new McpToolServer { ServerName = "BlazorDX MCP sample" }
    .Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),
        () => new MeetingRequest(),
        (meeting, cancellationToken) =>
            Task.FromResult($"Scheduled \"{meeting.Title}\" for {meeting.Attendees} attendee(s).")));

// Stop cleanly on Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await McpStdioHost.RunAsync(server, Console.In, Console.Out, cts.Token);

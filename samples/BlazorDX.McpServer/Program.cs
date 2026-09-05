using BlazorDX.McpServer;
using BlazorDX.Primitives.Forms;

// Two tools, because the pair is the point: the grid the app already renders becomes the
// assistant's way to look something up, and the form the app already validates becomes its way
// to act on what it found. Neither is a bespoke AI surface — both are declarations the UI uses.
//
// A real server would also pass an IAiToolAuthorizer (to gate tools per caller) and an
// IDxDiagnostics sink (to audit calls); both are optional and omitted here to keep the sample
// minimal.
var server = new McpToolServer { ServerName = "BlazorDX MCP sample" }
    .Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),
        () => new MeetingRequest(),
        (meeting, cancellationToken) =>
            Task.FromResult($"Scheduled \"{meeting.Title}\" for {meeting.Attendees} attendee(s).")))
    .Add(new GridAiTool<StockRow>(
        "query_stock",
        "Warehouse stock levels. Filter by SKU, product name or warehouse; sort by any column. "
            + "Consult this before answering questions about what is in stock or running low.",
        new StockRowGridAccessor(),
        new StockDataSource(),
        maxRows: 25));

// Stop cleanly on Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await McpStdioHost.RunAsync(server, Console.In, Console.Out, cts.Token);

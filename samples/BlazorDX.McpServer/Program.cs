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
        maxRows: 25))

    // A prompt is the third surface on the same declaration, and the only one the *user* drives:
    // it shows up as a slash-command, states the task, lists the real field rules, and names the
    // tool that submits. Without it someone has to know schedule_meeting exists and describe its
    // fields themselves.
    .Add(new FormAiPrompt<MeetingRequest>(new MeetingRequestFormModel()))

    // A resource is read, not called — attached by the user rather than chosen by the model.
    // Built here from the same data the grid tool queries, so the two cannot disagree.
    .Add(new TextAiResource(
        "stock://low",
        "Low stock report",
        "Every SKU at or below its reorder point, newest count first.",
        StockDataSource.LowStockReportAsync,
        "text/markdown"));

// Stop cleanly on Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await McpStdioHost.RunAsync(server, Console.In, Console.Out, cts.Token);

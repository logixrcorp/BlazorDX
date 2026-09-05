# BlazorDX MCP server (stdio sample)

A runnable [Model Context Protocol](https://modelcontextprotocol.io) server that exposes two
BlazorDX declarations as AI tools over **stdio**: a `[DxFormModel]` and a `[GridRow]` bound to an
`IGridDataSource`. It's the proof that *your UI is already the tool surface* — the annotated model
`DxForm` renders and the columns `DxDataGrid` shows are what the assistant can respectively write
and read.

```
                                        ┌─► query_stock       (read)  ← [GridRow] + IGridDataSource
assistant ──spawns──► dotnet run ──MCP──┤
                                        └─► schedule_meeting  (write) ← [DxFormModel]
```

The pair is deliberate. A tool that only writes makes an assistant guess at its arguments; a tool
that only reads leaves it narrating instead of acting. Together they are the whole loop: look
something up in the same data source the grid binds to, then act on it through the same validation
the form enforces.

## Run it

```bash
dotnet run --project samples/BlazorDX.McpServer
```

It then reads newline-delimited JSON-RPC on stdin and writes responses on stdout. Try it by
hand:

```bash
printf '%s\n%s\n%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"query_stock","arguments":{"filters":[{"column":"Warehouse","contains":"leeds"}],"sort":[{"column":"OnHand"}],"take":3}}}' \
  | dotnet run --project samples/BlazorDX.McpServer
```

You'll get the `initialize` result, a `tools/list` containing both tools with their JSON-Schemas,
and a page of stock rows carrying the full `totalCount` alongside the three returned.

## What the model can and cannot see

`query_stock` derives everything — its schema, its filters, its sort keys, its output — from the
generated `IGridRowAccessor<StockRow>`. So it reaches exactly the properties marked
`[GridColumn]`, and `StockRow.Cost` (deliberately unmarked) is unreachable: it appears in neither
the grid nor the tool. Narrowing what an assistant may read is the same edit as narrowing what the
grid shows, not a second permission model kept in sync by hand.

Because the column set is known, the schema names the columns as a JSON-Schema `enum` rather than
asking for a free-text field name — a model cannot invent a column, and a wrong guess is a schema
violation the host rejects rather than a query that quietly matches nothing.

The tool is read-only and says so (`annotations.readOnlyHint`), because `IGridDataSource` has no
write path. Rows per call are capped, and the reply always carries the unclamped `totalCount`, so
a model that asked for everything is told plainly how much it did not see.

## Wire it into Claude Desktop

Add this to your `claude_desktop_config.json` (Settings → Developer → Edit Config), using an
absolute path to the project, then restart Claude Desktop:

```json
{
  "mcpServers": {
    "blazordx": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/BlazorDX/samples/BlazorDX.McpServer"]
    }
  }
}
```

Then ask something that needs both tools — "which Leeds items are nearly out of stock? book me 30
minutes to reorder them" — and watch it read through the grid's data source before writing through
the form's validation.

## How it's wired (`Program.cs`)

```csharp
var server = new McpToolServer { ServerName = "BlazorDX MCP sample" }
    .Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),          // source-generated descriptor
        () => new MeetingRequest(),
        (meeting, ct) => Task.FromResult($"Scheduled \"{meeting.Title}\".")))
    .Add(new GridAiTool<StockRow>(
        "query_stock",
        "Warehouse stock levels. Filter by SKU, product name or warehouse…",
        new StockRowGridAccessor(),             // source-generated accessor
        new StockDataSource(),                  // the same IGridDataSource a grid would bind to
        maxRows: 25));

await McpStdioHost.RunAsync(server, Console.In, Console.Out, cts.Token);
```

The description is the one part worth writing carefully: it is all the model has to decide *when*
this tool is the right one to reach for.

For production, also pass an `IAiToolAuthorizer` (to gate tools per caller) and an
`IDxDiagnostics` sink (to audit every call) to the `McpToolServer` — both are optional and
omitted here to keep the sample minimal. Authorization matters more once a read tool is present:
the data source is reached with the server's own permissions, so a per-caller gate is what stops
an assistant reading rows its user could not have seen in the grid.

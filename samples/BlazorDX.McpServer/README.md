# BlazorDX MCP server (stdio sample)

A runnable [Model Context Protocol](https://modelcontextprotocol.io) server that exposes a
BlazorDX `[DxFormModel]` as an AI tool over **stdio**. It's the proof that *forms are AI
tools*: the same annotated model `DxForm` renders for a human is the tool an assistant calls.

```
assistant ──spawns──► dotnet run (this) ──MCP/stdio──► McpToolServer ──► schedule_meeting tool
```

## Run it

```bash
dotnet run --project samples/BlazorDX.McpServer
```

It then reads newline-delimited JSON-RPC on stdin and writes responses on stdout. Try it by
hand:

```bash
printf '%s\n%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  | dotnet run --project samples/BlazorDX.McpServer
```

You'll get the `initialize` result and a `tools/list` containing `schedule_meeting` with its
JSON-Schema (generated from the model's `[DxField]` attributes).

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

Ask the assistant to "schedule a meeting titled Q3 planning for 8 people" and it will call the
tool — its arguments are validated by the *same* rules the rendered form uses.

## How it's wired (`Program.cs`)

```csharp
var server = new McpToolServer { ServerName = "BlazorDX MCP sample" }
    .Add(new FormAiTool<MeetingRequest>(
        new MeetingRequestFormModel(),          // source-generated descriptor
        () => new MeetingRequest(),
        (meeting, ct) => Task.FromResult($"Scheduled \"{meeting.Title}\".")));

await McpStdioHost.RunAsync(server, Console.In, Console.Out, cts.Token);
```

For production, also pass an `IAiToolAuthorizer` (to gate tools per caller) and an
`IDxDiagnostics` sink (to audit every call) to the `McpToolServer` — both are optional and
omitted here to keep the sample minimal.

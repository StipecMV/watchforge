using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using WatchForge.Mcp;

// WatchForge MCP server (S7-1) — streamable HTTP transport, nástroje mapujú
// REST API v1 (WatchForge.Api). Pre agenta/LLM: search_motion, get_clip,
// trigger_processing, get_request_status, search_recordings.
// Konfigurácia:
//   WATCHFORGE_API_URL  — base URL API (default http://localhost:5000)
//   WATCHFORGE_API_TOKEN — X-Api-Token (default prázdny; API musí byť dostupné)

var apiUrl = Environment.GetEnvironmentVariable("WATCHFORGE_API_URL") ?? "http://localhost:5000";
var apiToken = Environment.GetEnvironmentVariable("WATCHFORGE_API_TOKEN") ?? "";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<WatchForgeMcpTools>(client =>
{
    client.BaseAddress = new Uri(apiUrl.EndsWith('/') ? apiUrl : apiUrl + "/");
    if (!string.IsNullOrEmpty(apiToken))
        client.DefaultRequestHeaders.Add("X-Api-Token", apiToken);
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<WatchForgeMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.Run();

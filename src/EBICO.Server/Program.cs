using EBICO.Server;
using EBICO.Server.Http;
using EBICO.Server.Http.Admin;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// EBICS server (emulator): request pipeline, extension points, return-code mapping and the
// pluggable in-memory state store. See docs/server/host.md.
builder.Services.AddEbicoServer();

// Liveness probe for container orchestration (docker-compose/Kubernetes) and external checks.
builder.Services.AddHealthChecks();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

// EBICS HTTP endpoint (POST, text/xml) at the configured path (default "/ebics").
app.MapEbicsEndpoint(options.EndpointPath);

// Master-data admin API (banks/partners/subscribers CRUD) at the configured prefix (default
// "/admin"). NOTE: this API is unauthenticated by design — it is meant for local/emulator use
// only (like Azurite); do not expose it on an untrusted network.
app.MapEbicoAdminApi(options.AdminApiPath);

// Liveness endpoint at "/health" (returns 200 "Healthy"). Used by the docker-compose example and any
// orchestrator readiness/liveness probe.
app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// Program entry point. Declared as a partial class so integration tests can reference it as the
/// <c>TEntryPoint</c> of <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public partial class Program;

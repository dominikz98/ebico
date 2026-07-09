using EBICO.Server;
using EBICO.Server.Http;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// EBICS server (emulator): request pipeline, extension points, return-code mapping and the
// pluggable in-memory state store. See docs/server/host.md.
builder.Services.AddEbicoServer();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

// EBICS HTTP endpoint (POST, text/xml) at the configured path (default "/ebics").
app.MapEbicsEndpoint(options.EndpointPath);

app.Run();

/// <summary>
/// Program entry point. Declared as a partial class so integration tests can reference it as the
/// <c>TEntryPoint</c> of <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public partial class Program;

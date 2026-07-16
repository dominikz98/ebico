using EBICO.Server;
using EBICO.Server.State;
using EBICO.Suite.Components;
using EBICO.Suite.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Server-side emulator state in-process (ADR-0009): the read/write store plus the master-data
// manager (banks/partners/subscribers, referential integrity, cascades, permissions, lifecycle)
// from EBICO.Server, used directly rather than via an HTTP API. The management UI (#53) drives the
// manager; the read-only views bind the IEmulatorStateProvider bridge over the same store.
builder.Services.AddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
builder.Services.AddSingleton<IMasterDataManager, MasterDataManager>();
builder.Services.AddSingleton<SampleEmulatorStateProvider>();
builder.Services.AddScoped<IEmulatorStateProvider, EmulatorStateProvider>();

// Transaction inspector (#54): the event log, transaction stores and raw-message capture store from
// EBICO.Server, read in-process (ADR-0009). The Suite runs no live EBICS pipeline, so these are seeded
// with sample transactions below; cross-process live inspection is a follow-up (persistence, ADR-0015).
// InMemoryEventLog/InMemoryMessageCaptureStore need a TimeProvider and the server options.
builder.Services.AddOptions<EbicoServerOptions>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEventLog, InMemoryEventLog>();
builder.Services.AddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
builder.Services.AddSingleton<IDownloadTransactionStore, InMemoryDownloadTransactionStore>();
builder.Services.AddSingleton<IMessageCaptureStore, InMemoryMessageCaptureStore>();
builder.Services.AddScoped<ITransactionInspectorProvider, TransactionInspectorProvider>();

var app = builder.Build();

// The in-memory store starts empty; seed the sample master data so the UI has content to show.
await EmulatorStateSeeder.SeedAsync(app.Services);

// Seed sample transactions/events/captures so the inspector has content against the otherwise-empty store.
await TransactionInspectorSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
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
    .AddInteractiveServerRenderMode();

app.Run();

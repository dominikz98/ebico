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

var app = builder.Build();

// The in-memory store starts empty; seed the sample master data so the UI has content to show.
await EmulatorStateSeeder.SeedAsync(app.Services);

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

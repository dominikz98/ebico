# Suite: UI Shell & Navigation

> Implementation of **Issue #52** (Milestone M7 — Suite). This page describes the
> shell of `EBICO.Suite`: the fixed render mode, layout/navigation/
> theming and the binding to the emulator state. The render mode is justified in
> [ADR-0009](../adr/0009-blazor-render-mode.md). The concrete
> data views follow in #53 (master data), #54 (transaction inspector) and
> #55 (keys/certificates).

## Purpose

`EBICO.Suite` is the admin/inspector UI of the emulator (a .NET 10 Blazor Web
App). #52 delivers the *skeleton*: the navigation over the M7 areas, an
EBICO-specific theming and the mechanism by which the UI is bound to the server-side
state. The functional CRUD/inspector features come in the
follow-up issues; here it is the load-bearing structure that matters.

## Render mode

The Suite runs in **Interactive Server** mode ([ADR-0009](../adr/0009-blazor-render-mode.md)).
Interactivity is activated per component via `@rendermode InteractiveServer`;
the dashboard stays Static SSR, the master-data page is Static SSR with
interactive islands ([#53](master-data.md)). This keeps access to the
server-side state an in-process call via DI — no separate
WebAssembly client or contracts project needed.

## Navigation & layout

The navigation (`Components/Layout/NavMenu.razor`) maps the four M7 areas;
the Blazor template demo pages (Counter/Weather) were removed.

| Entry | Route | Content |
| --- | --- | --- |
| Dashboard | `/` | Metrics of the emulator state (count of banks/partners/subscribers) |
| Master Data | `/master-data` | Management of banks/partners/subscribers ([#53](master-data.md)) |
| Transactions | `/transactions` | Transaction inspector ([#54](transaction-inspector.md)) |
| Keys | `/keys` | Fingerprints, INI-letter comparison, test-CA/key tools ([#55](key-view.md)) |

The `MainLayout` keeps the template's sidebar structure (sidebar + content),
but shows the EBICO title in the top row instead of the template "About" link.

### Sample-data banner (#124)

Above the page content, **every** view carries a `DemoDataBanner`
(`Components/Layout/DemoDataBanner.razor`, `role="note"`): the Suite works on its **own**
in-memory state with seeded master data and transactions and is **not** connected to a separately
started `EBICO.Server` process.

This separation has been intended since [ADR-0009](../adr/0009-blazor-render-mode.md) and is documented in
[ADR-0015](../adr/0015-event-log-store.md) as well as in `docker-compose.yml` — it
was only invisible **in the surface itself**. Whoever runs `docker compose up` sees two services
side by side and a UI that shows plausible transactions of a server that never saw them.
The banner closes exactly this gap between documentation and screen.

### Unknown route (#126)

A non-existent address returns **HTTP 404** and renders `Components/Pages/NotFound.razor` in the
`MainLayout` — wired up twice: via `NotFoundPage` on the `Router` (`Components/Routes.razor`) for
client-side navigation and via `UseStatusCodePagesWithReExecute("/not-found")` in `Program.cs` for
direct calls.

Until #126 the page was the unchanged Blazor template remnant: English text in a consistently
German surface, **without `<PageTitle>`** (empty browser tab, while every other page sets a title)
and with `<h3>` instead of `<h1>`, whereby the `<FocusOnNavigate Selector="h1" />` of the same router
grasped at nothing — the page had no `h1` at all. Now German, with a title, an `h1` and a link
back to the dashboard.

## Theming

A restrained, EBICO-specific theme instead of the template defaults; no
in-house design system. The brand/accent colours live as CSS custom properties
(design tokens) in `wwwroot/app.css` under `:root` (`--ebico-primary`,
`--ebico-primary-dark`, `--ebico-accent`, `--ebico-sidebar-*`) and are reused by the
scoped-CSS files (`MainLayout.razor.css`, `NavMenu.razor.css`) as well as the
dashboard cards. Bootstrap remains as the base.

## Binding to the server state

The server-side emulator store (keys, transactions, onboarding state)
only comes into being in the server layer (M3/M4). So that the shell already shows the binding
end-to-end now, the UI accesses a read model of the existing
`EBICO.Core.Domain` aggregates via an **abstraction**:

| Type | Role |
| --- | --- |
| `IEmulatorStateProvider` | Read-model contract of the Suite: `GetBanksAsync` / `GetPartnersAsync` / `GetSubscribersAsync` |
| `SampleEmulatorStateProvider` | In-memory placeholder with deterministic sample data (`Bank`/`Partner`/`Subscriber`) |

```csharp
// Program.cs
builder.Services.AddScoped<IEmulatorStateProvider, SampleEmulatorStateProvider>();
```

The methods are kept **async** so that a later backend (in-process store
or HTTP API) can be plugged in without changes at the call sites.

> **Update (#53):** The real server store (M3, [#30](../server/master-data.md)) is
> now bound. The registered implementation is now the live bridge
> `EmulatorStateProvider` over the in-process `IEbicsStateStore`/`IMasterDataManager`
> (Suite → Server → Core); `SampleEmulatorStateProvider` now serves only as a seed and
> key source. The dashboard and key view remained unchanged. Details:
> [master-data management](master-data.md).

## Tests

`tests/EBICO.Tests/Suite/` covers:

- `SampleEmulatorStateProviderTests` — pure xUnit: the stub returns the expected
  banks/partners/subscribers, subscribers reference only known partners, cover
  technical users and several lifecycle states (happy path + consistency).
- `NavMenuTests` — bUnit: the navigation renders exactly the four M7 links and
  **no** Counter/Weather demo links anymore.
- `DashboardTests` — bUnit with a fake `IEmulatorStateProvider`: the dashboard
  shows the metrics from the state provider.
- `NotFoundPageTests` — bUnit: German `h1` heading, no English template remnants,
  no `h3`, way back to the dashboard (#126).

For Blazor component tests, **bUnit** (`Directory.Packages.props`)
was added; it is used framework-agnostically with xUnit v3 (`BunitContext`).

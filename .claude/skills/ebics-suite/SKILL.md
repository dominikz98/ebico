---
name: ebics-suite
description: >-
  Guide to working on EBICO.Suite — the Blazor web app (Interactive Server) that serves as the
  admin/inspection UI for the emulator. Use when adding/changing pages or components: master data
  management (banks/partners/subscribers), transaction inspector, key/certificate view. Covers the
  render mode, in-process access to the server state, the projections (message capture/event log)
  and the bUnit test convention.
---

# EBICO.Suite (Blazor admin UI)

Blazor Web App, render mode **Interactive Server** (ADR-0009). The Suite references `EBICO.Server`
and uses its services **in-process** — no HTTP against the emulator. Before making changes, read the
matching page under `docs/suite/`.

## Structure

- Components: `src/EBICO.Suite/Components/` (`Pages/`, `MasterData/`, `Transactions/`, `Keys/`, layout).
- Services/adapters: `src/EBICO.Suite/Services/` (`IEmulatorStateProvider` + `EmulatorStateProvider` /
  `SampleEmulatorStateProvider`, `ITransactionInspectorProvider` + `TransactionInspectorProvider`,
  seeders for sample data).
- Static assets: `src/EBICO.Suite/wwwroot/`.

## Connection to the emulator state (in-process)

- **Master data** (`docs/suite/master-data.md`): CRUD through `IMasterDataManager`
  (`src/EBICO.Server/State/IMasterDataManager.cs`) — banks/partners/subscribers including state &
  permissions, referential integrity on the server side. Sample data via seeders.
- **Transaction inspector** (`docs/suite/transaction-inspector.md`): two projections —
  raw XML per phase from `IMessageCaptureStore` (ADR-0021) and the global protocol view from
  `IEventLog` (all customers, live filters customer/period/type/severity). In-process (ADR-0015:
  cross-process live inspection remains a follow-up topic).
- **Key/certificate view** (`docs/suite/key-view.md`): display fingerprints,
  INI letter comparison (`PublicKeyFingerprint.Verify`), test CA/key tools; PDF via
  QuestPDF (ADR-0010).

> **The Suite shows its own state, not that of a running server.** It hosts the server stores
> in-process and seeds them; a separately started `EBICO.Server` process stays invisible
> (ADR-0009/ADR-0015). The `DemoDataBanner` in the `MainLayout` says so in the UI since #124 — so for
> new views, do **not** choose wording that suggests live data from a foreign server
> ("the emulator's transactions" or similar).

## Interactive islands do not stay consistent on their own (ADR-0031)

Several interactive islands on **one** page (as on the master data page: `BankManager` +
`PartnerManager` + `SubscriberManager`) are self-contained components with **their own copy of the
state** each, yet they write through the same `IMasterDataManager` — and the relationships cascade.
Without a notification, every mutation silently invalidates the siblings (#126). Therefore the following
applies to **every** component that displays master data:

1. `@implements IDisposable` + `@inject IMasterDataChangeNotifier Changes`; in `OnInitializedAsync`
   `Changes.Subscribe(handler)`, in `Dispose` return the subscription.
2. After **every** successful mutation, `await Changes.NotifyChangedAsync()`.
3. Use `InvokeAsync(...)` in the handler — the notification arrives on the thread of the
   triggering circuit (the notifier is a **singleton**, so that sessions converge too).
4. After the reload, **check transient UI state** (an open form with a deleted bank, a delete
   confirmation for a cascaded record, a detail area without a record). Reloading alone only
   repairs the tables.

> No guard enforces the subscription — whoever forgets it goes stale silently again.
> `MasterDataIslandSyncTests` is the safety net and renders several components in **one**
> `BunitContext` (shared DI container ⇒ shared store and notifier).

**`Save*` is an upsert, not "create".** Create forms have to check the identity beforehand
(`GetBankAsync`/`GetPartnerAsync`/`GetSubscriberAsync`), otherwise they silently overwrite
existing records — for a subscriber including state and permissions (#126).

**Build filter/selection lists data-driven**, not from `Enum.GetValues`: options without any data
only lead to "no matches" (see `GetTypeOptionsAsync`/`GetSeverityOptionsAsync`).

## Creating a new page/component

1. Razor component under `Components/` (for a page with `@page`, register it in the navigation).
2. Access state through the existing service abstractions (`IEmulatorStateProvider` /
   `ITransactionInspectorProvider` / `IMasterDataManager`), do not couple directly to the stores.
3. Extend the sample data seeding if the view would otherwise stay empty.
4. When displaying master data: subscribe to the notifier (see above).
5. Pages set a `<PageTitle>` and carry exactly one `<h1>` — `Routes.razor` focuses on it via
   `<FocusOnNavigate Selector="h1" />`.

## Tests (bUnit)

- `tests/EBICO.Tests/Suite`: `BunitContext` + `Render(...)`.
- **The xUnit1051 trap under `TreatWarningsAsErrors`:** for calls that accept a `CancellationToken`,
  pass `TestContext.Current.CancellationToken` — otherwise it is a build error.
- Register services in the test DI before rendering.

## Definition of Done

Docs under `docs/suite/` + a link in `docs/index.md`, tests, ADR if applicable. Process: `ebics-feature-workflow`.

## Sources

- Code: `src/EBICO.Suite/{Components,Services,wwwroot}`, `src/EBICO.Server/State`
  (`IMasterDataManager`, `IMessageCaptureStore`, `IEventLog`).
- Docs: `docs/suite/ui-shell.md`, `docs/suite/master-data.md`, `docs/suite/transaction-inspector.md`,
  `docs/suite/key-view.md`. ADR: 0009 (render mode/in-process), 0010 (QuestPDF),
  0021 (message capture), 0031 (change notification between the islands).

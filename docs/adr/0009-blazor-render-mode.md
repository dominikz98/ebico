# 0009 — Blazor render mode (Interactive Server)

- Status: accepted
- Date: 2026-07-09

## Context

`EBICO.Suite` is the admin/inspector UI for the emulator (Milestone M7). As a
.NET 10 Blazor Web App a render mode must be chosen. Blazor offers: Static SSR
(server-rendered only, no interactivity), Interactive Server (interactivity over a
SignalR circuit, logic runs on the server), Interactive WebAssembly (logic in the
browser) and Interactive Auto (first call via server, then WebAssembly).

The Suite runs in the same process as its data source: the EBICS emulator state
(banks/partners/subscribers, later transactions and keys; server store from M3
onwards, see [ADR backlog](README.md)) lives server-side. It is an internal
operations/diagnostics UI with no requirement for offline operation or mass scale.

## Decision

**Interactive Server** as the Suite's global interactivity mode; interactivity is
enabled per component via `@rendermode InteractiveServer`, pure display pages stay
Static SSR.

This keeps the existing setup (`AddInteractiveServerComponents()` /
`AddInteractiveServerRenderMode()` in `Program.cs`) in place and records it as a
deliberate architecture decision.

## Consequences

- **One host, no project split:** no separate WebAssembly client project and no
  shared DTO/contracts project needed.
- **Direct access to server-side state** via DI (e.g. `IEmulatorStateProvider`) —
  the wiring to the later emulator store (M3) stays an in-process call rather than a
  dedicated HTTP API.
- Trade-off: one open SignalR circuit per client; uncritical for an internal admin
  tool. Latency on every interaction (server round-trip) is acceptable.
- No offline/client-compute scenario possible — not relevant for this UI.

## Alternatives

- **Interactive Auto (WASM + Server):** requires an additional client project and a
  shared contracts project; state access only via an HTTP API. Significantly more
  effort with no benefit for an internal admin tool — rejected.
- **Interactive WebAssembly:** client-side like Auto, plus first-start latency from
  loading the runtime; the same API constraint — rejected.
- **Static SSR (no interactivity):** too little for inspector interactions
  (filtering, detail views, later actions) — rejected.

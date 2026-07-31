# 0031 — Change notification between the Suite's master-data islands

- **Status:** accepted
- **Date:** 2026-07-27
- **Context issue:** [#126](https://github.com/dominikz98/ebico/issues/126)

## Context

The `/master-data` page renders `BankManager`, `PartnerManager` and `SubscriberManager` as
**three separate interactive islands** (ADR-0009, "interactivity per component"). Each
component loads its state once in `OnInitializedAsync` and, after a mutation, updates only
itself.

But all three write through the **same** `IMasterDataManager`, and the relationships are
cascading (bank → partner → subscriber, see [#30](../server/master-data.md)). Thereby every
mutation in one island invalidated the state of the other two without their knowing it. An
exploratory test of the running application (#126) showed the consequences:

- A newly created bank was missing from the select fields of the partner and subscriber
  forms. Since the form pre-selects the **first** bank of the stale list, a partner silently
  ended up under a foreign bank — with a green success message.
- A deleted bank stayed selectable; saving against it produced the contradictory message pair
  "Bank X deleted." + "Bank X does not exist.".
- Cascade-deleted partners and subscribers stayed as dead entries in the tables — with active
  "Edit"/"Delete" buttons — until the next full page reload.

## Decision

A dedicated **`IMasterDataChangeNotifier`** (`src/EBICO.Suite/Services/`) as a **singleton**.
Each island subscribes to it in `OnInitializedAsync`, returns the subscription in `Dispose`
and calls `NotifyChangedAsync()` after **every** successful mutation.

A subscriber does two things:

1. reload its state, and
2. **check transient UI states against the fresh data** — an open form must no longer offer
   a deleted bank, a delete confirmation for a cascaded record is moot, a detail area without
   a record closes itself.

Point 2 is the part that is easily overlooked: reloading alone fixes the tables, not the
already-opened forms.

Because the notifier is a singleton, notifications arrive on the thread of the **triggering**
circuit. A subscriber must therefore switch back to its own renderer via
`ComponentBase.InvokeAsync` before touching component state.

## Consequences

- The islands stay consistent with each other, without a page reload.
- **Across sessions too:** the stores are process-wide singletons (ADR-0009), the notifier is
  one as well — a change in one browser tab reaches the others.
- The broadcast is **best-effort**: a failing subscriber does not stop the rest; the errors
  are collected and reported as an `AggregateException` instead of being silently swallowed.
- Every new component that displays master data must subscribe to the notifier — otherwise it
  goes stale silently again. That is the flip side of the island architecture and is recorded
  in [master-data.md](../suite/master-data.md) as well as in the `ebics-suite` skill.
- The notifier carries **no** payload ("what changed"). With three small in-memory lists a
  full reload is cheaper than a differentiated event model; a Blazor `StateHasChanged`
  re-renders the island completely anyway.

## Alternatives

- **Merge the three islands into one component** that holds the state and passes it to the
  managers as parameters. Also solves the problem and needs no new service — but discards the
  deliberately chosen granularity from ADR-0009 and turns three manageable components into one
  large one. Rejected.
- **Scoped instead of singleton.** Isolated per circuit, hence without marshalling obligation
  and without thread-safety need. Solves the cases within *one* session, but not across
  sessions — although the state behind it is shared. Rejected, because the inconsistency
  between two tabs springs from the same cause.
- **Polling** (a timer per island). No new contract, but latency, constant load and a form
  that jumps under one's hands without anything having happened. Rejected.
- **Do nothing and offer a reload button.** Shifts a consistency error onto the user, and the
  most harmful case (a partner silently ends up under the wrong bank) persists. Rejected.

## Related

- [ADR-0009 — Blazor render mode (in-process state)](0009-blazor-render-mode.md)
- [Suite: master-data management](../suite/master-data.md)
- [Server: master-data management (#30)](../server/master-data.md) — cascades and upsert
  semantics

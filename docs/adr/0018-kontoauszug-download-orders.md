# 0018 — Account-statement/report download orders (synthetic generation, camt.05x.001.08, ZIP container)

- Status: accepted
- Date: 2026-07-15

## Context

Issue #40 (Milestone M5) requires server-side **download orders for account statements
& reports**: STA (MT940), VMK (MT942), C53 (camt.053), C52 (camt.052), C54 (camt.054) —
with **server-generatable test data** and a **date-range filter**. The generic
[download transaction](../server/download-transaction.md) (#33) already exists, but
evaluates neither the business order type nor the order parameters; it only delivers
pre-fed raw payloads. There is **no** account/booking domain model and **no**
camt/MT940 XSD bindings (the ISO/SWIFT schemas are not part of the repo, ADR-0003/0006).

To be decided: (1) where the statement data comes from, (2) which camt message version
is generated, (3) whether the BTF `Container=Zip` is implemented as a real ZIP, and (4)
how the generation hooks into the download engine.

## Decision

1. **Synthetic, deterministic generator** (`SyntheticStatementGenerator`): account
   (valid DE IBAN with ISO 7064 check digits), balances and bookings are generated
   reproducibly from the subscriber triple (host/partner/user) + time range (stable
   FNV-1a seed, no `DateTime.Now`, no `string.GetHashCode()`). No new account
   master-data model. Admin-seedable raw payloads remain possible in parallel and take
   **precedence** over generation.

2. **Fixed camt version `camt.05x.001.08`** (modern ISO/CGI-MP variant, structured
   `<Sts><Cd>BOOK</Cd></Sts>`), analogous to the fixed `pain.002.001.03` in ADR-0017.
   The version is a single constant per builder.

3. **Real ZIP container** (`StatementZipContainer` via
   `System.IO.Compression.ZipArchive`), since the BTF entries declare `Container=Zip`.
   For byte-stable output the entry timestamp is fixed and the compression level pinned.
   The download engine compresses (zlib) and encrypts (E002) on top of it — matching the
   real layering `base64(E002(zlib(zip(document))))`.

4. **Generate-on-demand via `IDownloadOrderProcessor`** (default
   `StatementDownloadProcessor`), the download counterpart to `IUploadOrderProcessor`
   (#39). The engine first dequeues by the **resolved** order type, then (backward
   compatible) by the raw `FDL`/`BTD`, then generates. The resolution happens centrally
   via `BtfOrderTypeCatalog.ResolveDownloadOrderType` (BTF → FileFormat → direct); the
   missing **VMK/mt942** catalogue entry was added. Format generation lives entirely in
   `EBICO.Core` (`StatementContentFactory`), the server calls only this one seam.

## Consequences

- The emulator delivers plausible, date-range-filtered statements for all five order
  types without preparation; tests are possible without proprietary fixtures thanks to
  the determinism guarantee.
- Switching the dequeue key from the raw `FDL`/`BTD` to the resolved code is **strictly
  additive** thanks to the compat probe — existing #33 tests stay green unchanged.
- The synthetic formats are minimal and **unverified** against the official
  annexes/XSDs (documented spec caveats). A real account master-data model, the DK
  profile `.02` and the PSR/pain.002 download mapping (#39) remain follow-up steps.

## Alternatives

- **Account/booking master-data model** (new aggregate, admin API, store, Suite UI) —
  significantly larger scope, not needed for "generatable test data"; rejected in favour
  of the synthetic generator.
- **No ZIP** (leave the document to the engine's zlib compression directly) — simpler,
  but deviates from the declared `Container=Zip`; rejected in favour of the real ZIP.
- **Provider decorator** instead of a dedicated processor abstraction — would have
  burdened the `IDownloadDataProvider` (also used by admin seeding/re-enqueue) with a
  date-range parameter; rejected in favour of the separate, pluggable
  `IDownloadOrderProcessor` abstraction.

# Server: Download orders — account statements & reports (STA/VMK/C53/C52/C54)

> Implementation of **Issue #40** (Milestone M5 — Server: Orders & BTF). This page describes the
> **order-type-specific content generation** for account statements/reports on top of the
> [download transaction](download-transaction.md): on a download the emulator generates **on demand**
> synthetic statements in five formats and delivers them over the existing download machine.
>
> Deliberately **included**: the five order types **STA** (SWIFT MT940), **VMK** (SWIFT MT942), **C53**
> (camt.053), **C52** (camt.052), **C54** (camt.054); a **deterministic synthetic generator**
> (`SyntheticStatementGenerator`, "server-side generatable test data") with a valid DE IBAN, running
> balance and **date-range filter**; the five format builders (`Mt940Builder`/`Mt942Builder`/`Camt05xBuilder`);
> the **ZIP packing** (`StatementZipContainer`, BTF `Container=Zip`); the pluggable
> `IDownloadOrderProcessor` (default `StatementDownloadProcessor`); the resolution of the effective
> order code across all three conventions (`BtfOrderTypeCatalog.ResolveDownloadOrderType`) including the new
> **VMK/mt942** catalog entry; the **precedence** of pre-seeded data over generation.
> Deliberately **not yet**: a real **account/booking master data model** (the data is synthetic);
> the **camt version choice** is fixed at `camt.05x.001.08`; the **PSR/pain.002 download mapping** from
> [#39](payment-orders.md) (no BTF/order type points to the `PSR` queue); the Swiss/ISO-CH statement **Z53**;
> the **X002 signature** of the response (still M4).

## Purpose

An account statement/report download is a **generic, segmented download** (no dedicated handler):
it runs over the [download transaction](download-transaction.md) (FDL/BTD) or — for the classic
order types — directly over the order code. Previously the engine only delivered raw payloads seeded
in advance via the admin API and did **not** distinguish between the business order types when
dequeuing. #40 hooks in the **content generation** at this point: if no seeded payload exists for the
resolved order type, the server generates a synthetic statement in the matching format,
filtered to the requested date range, packs it into a ZIP and hands it to the send machine.

## Submission conventions & routing

The emulator accepts all three usual EBICS conventions; the **effective** order code is resolved centrally
via `BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, btf, fileFormat)` (order:
BTF → FileFormat → raw code):

| Version | Convention | Example | Resolution |
| --- | --- | --- | --- |
| H005 | `AdminOrderType=BTD` + `BTDOrderParams/Service` (BTF) | Service `EOP`/`camt.053` | → `C53` |
| H003/H004 | generic `OrderType=FDL` + `FDLOrderParams/FileFormat` | `FileFormat=camt.053` | → `C53` |
| H003/H004 | classic `OrderType` **directly** | `OrderType=STA` | → `STA` |

The routing detection (`DownloadTransactionEngine.IsDownloadOrderType`) now knows, besides `FDL`/`BTD`, also
the direct download codes (`BtfOrderTypeCatalog.IsDownloadOrderType`: STA/VMK/C53/C52/C54). The
resolved code is used **before** the authorisation check ([#38](btf-framework.md)) and passed on as the
queue/generation key.

The **BTF catalog** was extended with **VMK** (`STM`/`mt942`); STA/C53/C52/C54 were already
seeded with #38. Because `TryGetOrderType` matches on service **and** MsgName family, `mt942`↔VMK and
`camt.052`↔C52 do not collide despite sharing the service `STM`.

## Flow

Resolution, authorisation and provisioning happen in the **initialisation**; transfer/receipt work
unchanged on the generated payload (see [download transaction](download-transaction.md)):

| Step | Action |
| --- | --- |
| 1. Resolve | determine the effective order code (BTF/FileFormat/direct); extract `DateRange` from `FDLOrderParams`/`StandardOrderParams` (H003/H004) or `BTDOrderParams` (H005) |
| 2. Authorise | `Subscriber.HasPermissionFor(effectiveCode)` — otherwise `090003` |
| 3a. Dequeue | try the queue by the **resolved** code (e.g. `C53`) |
| 3b. Compat probe | if empty and the code ≠ raw OrderType: try the queue by `FDL`/`BTD` (backward compatibility) |
| 3c. Generate | if still empty and `StatementDownloadProcessor.CanProcess`: generate a synthetic statement (date-range filtered), ZIP-pack it |
| 4. Send | compress (`EbicsCompression`) → E002 encrypt → segment → segment 1 + `NumSegments` |

The **precedence** is thus: seeded data (admin API/re-enqueue) beats generation. The
key actually hit is remembered on the transaction, so that a negative receipt or an
eviction re-seeds the data under the same key.

The layering matches real EBICS practice: the delivered ciphertext is
`base64(E002(zlib(zip(document))))` — the business document sits in a ZIP (BTF `Container=Zip`), which
the transport compression of the engine additionally zlib-compresses.

### Example — MT940 (STA)

```text
:20:EBICO260731
:25:DE02120300000000202051
:28C:195/1
:60F:C260701EUR1000,00
:61:2607020702C200,00NTRFREF000001
:86:Rechnung 1 Kunde AG
:62F:C260731EUR1150,50
```

MT942 (VMK) carries, instead of `:60F:`/`:62F:`, a floor limit (`:34F:`), a time stamp (`:13D:`) and the
sums `:90D:`/`:90C:`.

### Example — camt.053 (C53), abridged

```xml
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
  <BkToCstmrStmt>
    <GrpHdr><MsgId>EBICO260731</MsgId><CreDtTm>2026-07-31T10:00:00Z</CreDtTm></GrpHdr>
    <Stmt>
      <Id>EBICO260731</Id><ElctrncSeqNb>195</ElctrncSeqNb>
      <FrToDt><FrDtTm>2026-07-01T00:00:00Z</FrDtTm><ToDtTm>2026-07-31T23:59:59Z</ToDtTm></FrToDt>
      <Acct><Id><IBAN>DE02120300000000202051</IBAN></Id><Ccy>EUR</Ccy> … </Acct>
      <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">1000.00</Amt>
           <CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-07-01</Dt></Dt></Bal>
      <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">1150.50</Amt>
           <CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-07-31</Dt></Dt></Bal>
      <Ntry><Amt Ccy="EUR">200.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts><Cd>BOOK</Cd></Sts> … </Ntry>
    </Stmt>
  </BkToCstmrStmt>
</Document>
```

camt.052 (C52) uses the root `BkToCstmrAcctRpt`/`Rpt` with an interim balance (`ITBD`); camt.054
(C54) the root `BkToCstmrDbtCdtNtfctn`/`Ntfctn` **without** balances.

## Return codes & error cases

| Situation | Return code | Storing |
| --- | --- | --- |
| success (generated or seeded, segment 1 delivered) | `000000` EBICS_OK | header + body |
| no authorisation for the (resolved) order type | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | body |
| nothing available **and** not generatable | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | body |
| payload exceeds the segment limit | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | body |

The remaining transaction/segment codes come unchanged from the
[download transaction](download-transaction.md).

### ⚠️ Spec caveats

- **Synthetic data.** Account, balances and bookings are generated deterministically from the subscriber
  triple + date range (no real account/booking master data model) — test data, not a business system.
- **MT940/MT942 tag syntax.** Minimal, plausible rendering; **no** XSD for MT. The `:61:` grammar,
  comma decimals and `:60F:`/`:62F:` framing are unchecked against the official SWIFT field specifications
  (pinned down by tests on exact strings).
- **camt version fixed at `camt.05x.001.08`.** Modern ISO/CGI-MP variant (structured
  `<Sts><Cd>BOOK</Cd></Sts>`); the classic DK profile `.02` (`<Sts>BOOK</Sts>`) is not implemented.
- **ZIP container vs. transport compression.** The document is packed into a ZIP and **additionally**
  zlib-compressed by the engine; the exact container framing (multiple daily files, base64 nesting) is
  unchecked against the proprietary annex.
- **PSR/pain.002 download open.** The [#39](payment-orders.md) status report under `PSR` remains
  unreachable via download (no BTF/order type maps to `PSR`) — a separate follow-up step.
- **Unsigned response.** X002 still deferred (M4), as with the download transaction.

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order identity | `OrderType` directly (STA/VMK/C5x) **or** `FDL` + `FDLOrderParams/FileFormat` | `AdminOrderType=BTD` + `BTDOrderParams/Service` (BTF) |
| Resolution → code | direct / FileFormat family → STA/VMK/C53/C52/C54 | BTF → classic code (catalog) |
| Date range | `FDLOrderParams/DateRange` or `StandardOrderParams/DateRange` | `BTDOrderParams/DateRange` |
| Generated formats | identical (MT940/MT942/camt.05x, version-agnostic) | ditto |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; MT/camt generated in code, no proprietary fixtures):

- `Core/Statements/SyntheticStatementGeneratorTests` — determinism, booking dates within the range,
  balance invariant (`Closing = Opening ± Σ`), valid DE IBAN (mod 97), different subscribers →
  different accounts, `rangeEnd < rangeStart` → `ArgumentException`.
- `Core/Statements/Mt940BuilderTests` / `Mt942BuilderTests` — tag presence, comma decimals, empty range
  (`:60F:`==`:62F:`, no `:61:`), MT942 sums and the absence of the booked balances.
- `Core/Statements/Camt053/052/054BuilderTests` — namespace + root, balance codes (`OPBD`/`CLBD`; `ITBD`;
  none for C54), `Ntry` count, `CdtDbtInd`, `Amt/@Ccy`.
- `Core/Statements/StatementZipContainerTests` / `StatementContentFactoryTests` — readable/deterministic
  ZIP, the expected format per order type.
- `Core/Btf/BtfDownloadOrderTypeTests` — VMK mapping, `IsDownloadOrderType`, `ResolveDownloadOrderType`
  (BTF/FileFormat/direct), `TryGetOrderTypeByFileFormat(Download)`.
- `Server/StatementDownloadTests` — **end-to-end** through the pipeline: H005 BTD+camt.053 (Init→Receipt),
  H004 FDL+FileFormat, H004 direct STA code; date-range filter, seeded data beats generation,
  missing authorisation → `090003`.

## Related documentation

- [Download transaction (initialisation + transfer + receipt)](download-transaction.md) — the send machine that #40 hooks into
- [BTF framework (H005)](btf-framework.md) — BTF↔order type catalog (VMK added), authorisation check
- [Upload orders: payments](payment-orders.md) — the mirror image on the upload side (`IUploadOrderProcessor`)
- [Master data management](master-data.md) — authorisations, admin API (seed the download queue)
- [EBICS return code catalog](../protocol/return-codes.md) — `090003`/`090005`/`091114`
- [ADR-0018 (account statement/report download orders)](../adr/0018-account-statement-download-orders.md) — synthetic generation, camt `.08`, ZIP container

# Server: Segmentation, compression & Base64 pipeline

> Implementation of **Issue #34** (Milestone M4 — Server: Transaction Engine). This page
> describes the **byte pipeline** for order data: splitting the compressed (and possibly
> encrypted) byte stream into **segments** and the deterministic **reassembly** on
> receipt, plus the **configurable segment size**.
>
> Deliberately **included**: the pure, reusable segmentation primitive
> `EbicsSegmentation` (`Split`/`Reassemble`) in `EBICO.Core`, the configurable segment size
> (`EbicoServerOptions.SegmentSizeBytes`), edge cases (1 segment, empty order data), determinism.
> Compression is **reused** via the existing `EbicsCompression`, Base64 is handled by the
> `base64Binary` binding per segment.
> Deliberately **not yet**: the transaction state machine — transaction ID, `DataTransfer`
> envelope, phases (initialisation/transfer/receipt), the header mapping of
> `NumSegments`/`SegmentNumber`/`lastSegment` and the triggering of the segment return codes. That
> is **composed and wired** by the upload (#32) and download transaction (#33), which use this
> primitive as a building block. No convenience orchestrator.

## Purpose

On the wire, every message carries its order data as `base64(compress(orderDataXml))` or — for
secured transactions — as `base64(encrypt(compress(orderDataXml)))`. As soon as this byte
stream grows larger than one segment size, it is delivered over **multiple messages**, one
`DataTransfer/OrderData` element per message. #34 provides the **how** of this preparation
(compress → base64 → split, or reassemble → base64-decode → decompress) as a cross-cutting
layer; the **when/who** (phases, transaction ID, envelope) lives in the transaction state machine
(#32/#33).

The primitive is deliberately **pure and policy-free**: `Split` is a deterministic byte splitter
and enforces **no** maximum number of segments / maximum size — that is the job of the transaction
engine (analogous to the policy-free stance of [`EncryptionE002`](../protocol/encryption-e002.md), which
likewise does not check any protocol permission). Compression already exists (Issue #47,
`EbicsCompression`); #34 builds on it instead of replacing it.

## Flow

Both directions compose the same primitives; `EbicsSegmentation` is the new seam.

**Send direction (server → client, download):**

| Step | Action |
| --- | --- |
| 1. Serialize | order data XML → `byte[]` (`EbicsXmlSerializer`) |
| 2. Compress | `EbicsCompression.Compress` (zlib) |
| 3. (optional) Encrypt | `EncryptionE002.Encrypt` → `EncryptedOrderDataBytes` (secured orders only) |
| 4. Segment | `EbicsSegmentation.Split(payload, options.SegmentSizeBytes)` → `SegmentedOrderData` (`Segments`, `NumSegments`) |
| 5. Base64 + envelope | one `DataTransfer/OrderData` per segment (`byte[]` → base64 via the `base64Binary` binding); `NumSegments`/`SegmentNumber`/`lastSegment` are set by the transaction layer (#33) |

**Receive direction (client → server, upload):**

| Step | Action |
| --- | --- |
| 1. Base64 decode | one `byte[]` per `OrderData` element (via the `base64Binary` binding) |
| 2. Reassemble | the **ordered** segments → `EbicsSegmentation.Reassemble(segments)` |
| 3. (optional) Decrypt | `EncryptionE002.Decrypt` (secured orders only) |
| 4. Decompress | `EbicsCompression.Decompress` |
| 5. Deserialize | `byte[]` → order data XML |

`Reassemble` concatenates in **list order** — it does **not** sort by `SegmentNumber` and
detects **no** gaps/duplicates. Sequence integrity (all `NumSegments` present, correct
order, `lastSegment` seen) is ensured by the transaction engine, which builds the ordered list
**before** the call. This keeps the primitive pure and deterministic.

## Segment size

`EbicoServerOptions.SegmentSizeBytes` (default **512 KiB**) limits the **raw bytes before Base64** per
segment. `EbicsSegmentation.Split` takes this value as an `int` parameter (`EBICO.Core` does not read the
server options — the server passes the value through).

Base64 inflates by a factor of **4/3** (`base64(N) = 4·⌈N/3⌉`):

| Raw segment size | Base64 wire size | Ratio to `MaxRequestBodyBytes` (1 MiB) |
| --- | --- | --- |
| 512 KiB (default) | ≈ 683 KiB | ~341 KiB headroom for the envelope |
| 768 KiB | = exactly 1 MiB | no headroom (theoretical maximum) |

The 512 KiB default deliberately leaves headroom for headers, `AuthSignature` and the `<OrderData>` tags,
which all count towards the same HTTP body. Determinism: same input + same size → **byte-identical**
segments (`NumSegments = ⌈payload.Length / SegmentSizeBytes⌉`, fixed sequential slices).

### The default is shared (#124)

The number lives, since **#124**, exactly once, in `EBICO.Core`:
**`EbicsSegmentation.DefaultSegmentSizeBytes`**. Both `EbicoServerOptions.SegmentSizeBytes` and the
connector's upload pipeline (`UploadExecutor`) reference it — both sides are thereby compatible **by
construction**.

> **Why this was necessary:** previously both sides chose independently. The connector took 768 KiB — the
> theoretical maximum from the table above, whose Base64 form fills *exactly* the 1 MiB body limit and
> leaves **zero** headroom for the envelope. Every upload whose compressed and encrypted
> order data filled a full segment was rejected with **HTTP 413** before the server could
> even respond (i.e. without an EBICS return code, as a transport exception in the middle of the
> transaction). Both defaults were tested individually, but never together over the real wire:
> `UploadE2ETests` explicitly checked `NumSegments == 1`.

Whoever deviates from a 1 MiB body limit derives the matching segment size with
**`EbicsSegmentation.MaxSegmentSizeForRequestBody(maxRequestBodyBytes, envelopeReserveBytes)`** instead of
guessing it. `EbicsSegmentation.Base64Length(n)` gives the wire size without a trial encoding. The
relationship itself — *Base64 segment + envelope reserve ≤ body limit* — is pinned down as a guard test
(`SegmentSizeCompatibilityTests`), including the historical 768 KiB value as a negative example. The
primitive `Split` remains **policy-free** and unaffected by this: the default is a constant next to it, not a
mandate in the splitter.

## Return codes & edge cases

The primitive throws only **argument-form errors** (BCL, no dedicated exception class):

| Situation | Behavior |
| --- | --- |
| empty order data (0 bytes) | **1 empty segment** (`NumSegments = 1`), no error; `Reassemble` returns `[]` |
| exactly 1 segment (`0 < len ≤ size`) | 1 segment == payload, `NumSegments = 1` |
| `maxSegmentSizeBytes ≤ 0` | `ArgumentOutOfRangeException` |
| `segments` `null` / element `null` | `ArgumentNullException` |
| `segments` empty (`Count == 0`) | `ArgumentException` (a valid transaction has ≥ 1 segment) |

The **business** segment return codes are already defined in the central catalog, but are **not**
triggered by #34 — that is done by the transaction engine (#32/#33), which knows the policy:
`EBICS_SEGMENT_SIZE_EXCEEDED` (091009), `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` (091104),
`EBICS_MAX_ORDER_DATA_SIZE_EXCEEDED` (091113), `EBICS_MAX_SEGMENTS_EXCEEDED` (091114). See
[return code catalog](../protocol/return-codes.md).

### ⚠️ Spec caveats

- **Raw vs. Base64 basis of the size:** `SegmentSizeBytes` counts raw bytes before Base64; the wire size
  is ≈ 4/3 of that. Whether EBICS applies its ~1 MB segment limit to the raw or the base64-encoded
  size is to be verified against the official EBICS annex. The choice (raw bytes) is confined to the
  size parameter / `SegmentSizeBytes`.
- **Base64 framing:** #34 models each segment as a **self-contained base64-encoded `byte[]`** (as
  the `base64Binary` binding does), not as a slice of a **shared** base64 stream. Which of the
  two readings the annex means is likewise to be verified; `Split`/`Reassemble` round trips
  hold regardless.
- **Compression framing** (inherited from `EbicsCompression`, Issue #47): zlib (RFC 1950) vs. raw DEFLATE
  vs. gzip is not verified against the annex.

## EBICS version mapping

The segment header fields exist as bindings per version (`EBICO.Core.Schema.<H00x>`), the
mapping happens in the transaction layer — the byte primitive is version-agnostic:

| Field | Upload/request | Download/response |
| --- | --- | --- |
| Number of segments | `StaticHeaderType.NumSegments` (`ulong?`) | `ResponseStaticHeaderType.NumSegments` (initialisation phase only) |
| Segment number | `MutableHeaderTypeSegmentNumber` (`Value` + `lastSegment`) | `ResponseMutableHeaderTypeSegmentNumber` (`Value` + `lastSegment`) |
| Phase | `TransactionPhaseType` (`Initialisation`/`Transfer`/`Receipt`) | ditto |

The wire fields are `ulong`; in memory `int` is natural (a `byte[][]` has ≤ `int.MaxValue`
elements). The cast `int → ulong` happens only at header mapping in #32/#33.

## Tests

`tests/EBICO.Tests/Serialization/EbicsSegmentationTests.cs` (xUnit v3 + AwesomeAssertions):

- **Happy path:** exact multiple → equally sized segments; with remainder → last segment shorter;
  smaller than size → 1 segment.
- **Edge cases:** empty input → 1 empty segment; `Split` with size `0`/`-1` → `ArgumentOutOfRangeException`;
  `Reassemble` `null`/empty list/null element → throws; `Reassemble([[]])` → `[]`.
- **Determinism:** split twice → byte-equal. Order test `[a][b][c]` → `abc` (catches
  accidental sorting).
- **Round trip / known answer:** `[Theory]` over lengths `{0, 1, size-1, size, size+1, 3·size, 100000}`
  × sizes `{1, 16, 1024, 512 KiB}` → `Reassemble(Split(x, s).Segments) == x`; fixed segment boundaries as a
  known-answer vector.
- **End-to-end with `EbicsCompression`:** `Decompress(Reassemble(Split(Compress(data), 64).Segments)) == data`
  (a small segment size forces multiple segments) — proves deterministic reassembly for real, not
  just self-consistency of one layer.
- **Default compatibility** (`SegmentSizeCompatibilityTests`, #124): `Base64Length(SegmentSizeBytes)` +
  envelope reserve ≤ `MaxRequestBodyBytes`; server and core default are the same constant; the
  historical 768 KiB value fills the limit exactly (kept as a negative example);
  `MaxSegmentSizeForRequestBody` returns the largest value that still fits.
- **Multi-segment E2E upload** (`UploadE2ETests`, #124): an upload with the **shipped** defaults
  over multiple segments, per H003/H004/H005. The payload is deliberately incompressible (base64 noise) —
  a normal pain.001 deflates to a single segment even at ten megabytes.

## Related documentation

- [Hostable server scaffolding](host.md) — pipeline, return codes, `MaxRequestBodyBytes`
- [Encryption E002](../protocol/encryption-e002.md) — the optional encryption step of the pipeline
- [EBICS return code catalog](../protocol/return-codes.md) — the (still unused) segment return codes
- [Upload transaction](upload-transaction.md) — receive direction: `Reassemble` wired in (#32)
- [Download transaction](download-transaction.md) — send direction: `Split` wired in (#33)
- [Connector architecture](../connector/architecture.md) — send pipeline & transaction skeleton (upload/download)

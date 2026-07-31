# 0030 — Aligned transport defaults, consistent return-code texts and client-side VEU wiring

- Status: accepted
- Date: 2026-07-27

## Context

An exploratory end-to-end test of the overall state (**#124**) checked the running
emulator not against the test suite but against the **docs**: started server and Suite via
`dotnet run`, ran the quickstart and pointed a custom connector client at the *separately
running* server — i.e. the path from [getting started](../getting-started.md), steps 1 + 2.
The core held up (onboarding, upload, download across all three versions), but three
findings could not be seen with the test suite by design:

1. **Uploads from 768 KiB onward were impossible with the shipped defaults.** The connector
   segmented at `768 KiB` raw — the theoretical upper bound whose base64 form yields
   *exactly* 1 MiB — while the server accepted `MaxRequestBodyBytes = 1 MiB`. The segment
   alone filled the limit; the envelope came on top. Every upload whose compressed and
   encrypted order data filled a full segment died with **HTTP 413** before the server could
   respond. Both defaults were tested individually, never together: `UploadE2ETests`
   explicitly checked `NumSegments == 1`.
2. **Business errors reported `EBICS_OK` as the report text.** The return code was read
   correctly from the non-OK slot (for business errors thus the body), the text however
   always from the header — which in exactly this case carries `000000`/`EBICS_OK`. Callers
   saw `090005: EBICS_OK`.
3. **The VEU workflow was not drivable from the connector**, although the
   [coverage matrix](../server/order-coverage-matrix.md) lists HVU–HVS as ✅ for all
   versions. Three gaps interlocked: H005 uploads demanded a BTF (which administrative orders
   do not have), `UploadRequest` could not transport an `OrderID`, and the `OrderAttribute`
   was hard-wired to `DZHNN` — so one could not even park an order.

Finding 3 is the real lesson: the matrix describes the **server**. From the user's point of
view an order type is only available once the shipped client can also send it.

## Decision

**1. One shared segment default in `EBICO.Core`.** `EbicsSegmentation.DefaultSegmentSizeBytes`
(512 KiB) is the one number that `EbicoServerOptions.SegmentSizeBytes` **and** the
connector's upload pipeline refer to. The value leaves a 1-MiB request ~341 KiB of room for
the envelope. Both sides are thereby compatible **by construction** rather than by chance.

**2. The relationship is fixed as a test, not the numbers.**
`SegmentSizeCompatibilityTests` checks
`Base64Length(SegmentSizeBytes) + envelope reserve ≤ MaxRequestBodyBytes` and keeps the
historic 768-KiB default as a negative example. Plus
`EbicsSegmentation.MaxSegmentSizeForRequestBody(…)`, with which a safe segment size can be
*derived* for a differing body limit rather than guessed. `EbicsSegmentation.Split` stays
policy-free — the default is a constant next to it, not a directive in the splitter.

**3. An E2E upload across multiple segments belongs to the standard matrix.**
`CctUpload_LargerThanOneSegment_RoundTripsWithTheShippedDefaults` runs for each H003/H004/H005
with the shipped defaults. The payload is deliberately **incompressible** (base64 noise in
the creditor names): a normal pain.001 deflates to a single segment even at ten megabytes —
which is exactly why the gap stayed undetected.

**4. Code and report text are resolved together.**
`EbicsReturnCodes.CombineOutcome(headerCode, headerText, bodyCode)` returns an
`EbicsResponseOutcome` (code + text). If the body wins, the text comes from the registry
(`SymbolicName`), **never** from the header. The function lives in Core instead of twice in
the two connector base classes; the view records take it via an additional constructor, so
the 15 parse sites stay readable unchanged.

**5. The H005 upload path treats administrative order types like the download path.** If an
order type does not resolve to a BTF, it is sent as an `AdminOrderType` instead of being
rejected client-side. This is not a softening but the removal of an **asymmetry**: the
download path has always done this (otherwise HTD/HKD/HAA/HPD/HAC/PTK on H005 would never have
been reachable).

**6. VEU is modelled as its own order family in the connector.** New: `VeuOrderReference`
(OrderID plus identity of the referenced order), `UploadRequest.DistributedSignature` (parking
trigger: H005 `SignatureFlag`, H003/H004 `OrderAttribute=OZHNN`),
`UploadRequest.Veu`/`DownloadRequest.Veu` as well as convenience requests
`Hvu`/`Hvz`/`Hvd`/`Hvt`/`Hve`/`Hvs` following the pattern of the other families. If the
reference is missing for HVE/HVS/HVD/HVT, the call fails **client-side** — with a message that
says what is missing, instead of the bank's generic `091121`.

**7. The bank fingerprints get an admin endpoint.** `GET /admin/banks/{hostId}/keys` returns
the fingerprint (hex + letter format), version, key length and the public PEM — the emulator
equivalent of the bank letter. Without it, a client against a separately hosted emulator has
no channel outside HPB against which to check the fingerprints; `HpbResult.FingerprintsVerified`
could never become `true` there. Only public components are exposed.

**8. The Suite marks its data set.** A `DemoDataBanner` in the layout says that the UI works
on its own in-memory state and is **not** connected to a separately running server. The
separation has been intended and documented since
[ADR-0009](0009-blazor-render-mode.md)/[ADR-0015](0015-event-log-store.md) — it was
just invisible in the UI itself, where seeded transactions looked like live data.

**9. The SDK pin names the lowest usable version.** `global.json` pins `10.0.100` instead of
`10.0.300` (each `rollForward: latestFeature`). `latestFeature` rolls only **upward**: the high
pin made the repo unbuildable on any machine with an SDK 10.0.2xx, while CI stayed
inconspicuously green because `actions/setup-dotnet` downloads the pinned version.

## Consequences

- **Large uploads work with the defaults.** Whoever deliberately raises the segment size must
  raise `MaxRequestBodyBytes` too; both XML-doc comments now say this explicitly and refer to
  `MaxSegmentSizeForRequestBody`.
- **Behaviour change in the H005 upload:** an order type without a BTF mapping is no longer a
  client-side error but goes as an `AdminOrderType` to the bank (which rejects it with `091006`
  if it does not know it). The test `H005_upload_with_an_unmapped_order_type_and_no_btf_throws`
  was flipped accordingly. Defensible, because the BTF catalogue is an explicitly **best-effort**
  seed ([ADR-0016](0016-btf-framework-and-authorisation.md)) and thus not a reliable "does this
  exist?" oracle. Without an order type *and* without a BTF it still throws.
- **`EbicsResult.ReturnText` can now be `null`**, where previously it wrongly stood at
  `"EBICS_OK"` — namely for a business code the catalogue does not know. A consistent `null` is
  preferable to a misleading success message.
- **The coverage matrix now separates server and client availability.** VEU stands at ✅ on both
  sides; the matrix got its own column for this so the same gap does not become invisible again.
- **Spec caveats remain.** The VEU order params carry, besides the OrderID, the identity of the
  referenced order (PartnerID + OrderType or service); the emulator keys its VEU store solely by
  the OrderID and ignores the rest. Against a real bank this is unverified. Also unverified: that
  `OZHNN` or `SignatureFlag` are the only parking triggers, and the HVE signature itself stays
  unverified server-side ([ADR-0020](0020-veu-orders.md)).
- **The admin endpoint enlarges the unauthenticated attack surface** — but only by public keys,
  which HPB hands out to every onboarded subscriber anyway. The admin API stays, as before,
  intended exclusively for local emulator operation.

## Alternatives

- **Only lower the connector default, without a shared constant:** rejected — would have made the
  same error possible again as soon as one side touches its value. The coupling is real and belongs
  made visible.
- **Raise `MaxRequestBodyBytes` to 2 MiB instead of lowering the segment size:** rejected — only
  shifts the boundary and makes the emulator more tolerant of payloads a real bank would reject.
  The EBICS-usual 1-MiB limit per segment stays the reference point.
- **Set the report text to `null` on body errors instead of fetching it from the registry:**
  rejected — the symbolic name is the information the caller expects, and it is already in the
  catalogue anyway. For unknown codes, `null` remains the result.
- **Make VEU reachable only via the generic `UploadRequest`/`DownloadRequest`:** rejected — would
  have solved points 5 and 6 (BTF requirement, `OrderID`), but every order family in the connector
  has convenience requests; VEU as the sole exception would be inconsistent and would leave the
  parking trigger undetected.
- **`GET /admin/banks/{hostId}/keys` also as `PUT` (seed a keypair):** deferred — the finding was
  the *reading* of the fingerprints. An import would need PEM parsing and a decision about private
  components; in-process `IServerBankKeyStore.SetAsync` stays the way.
- **Remove the SDK pin entirely:** rejected —
  [ADR-0001](0001-solution-layout-and-package-management.md) explicitly rests reproducibility without
  lock files on the pin.

## Related decisions

- [ADR-0029 — Interop fixes for real clients](0029-interop-fixes-real-clients.md) — the same
  mechanism: a test against something real finds what self-consistency tests hide by design.
- [ADR-0020 — VEU orders](0020-veu-orders.md) — the server-side implementation that is opened up
  client-side here.
- [ADR-0016 — BTF framework & authorisation](0016-btf-framework-and-authorisation.md) — why the BTF
  catalogue is not a completeness oracle.
- [ADR-0012 — Return-code catalogue](0012-return-code-catalogue.md) — the header/body placement whose
  text side is followed up here.
- [ADR-0015 — Event/audit log store](0015-event-log-store.md) — the documented separation
  of Suite and server state that the banner makes visible.

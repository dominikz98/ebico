# EBICO — context for Claude Code

You are working on the **EBICO** project: an EBICS implementation in C# (.NET 10),
conceptually like *Azurite*, but for EBICS instead of Azure Storage. The goal is a
server emulator plus a client package.

## Project structure (5 projects)

- `src/EBICO.Core` — shared primitives (schemas/serialisation, crypto, BTF/order models)
- `src/EBICO.Connector` — NuGet client for accessing an EBICS server (mediator pattern)
- `src/EBICO.Server` — the emulator (hostable, ASP.NET Core)
- `src/EBICO.Suite` — Blazor UI (Blazor Web App, Interactive Server) for the server
- `tests/EBICO.Tests` — unit/integration/conformance tests (xUnit v3)

Project references: Connector→Core, Server→Core, Suite→{Core, Server}, Tests→{Core, Connector, Server}.
(Suite→Server since #53: the Blazor UI uses the `IMasterDataManager`/state store in-process, ADR-0009.)

Supported EBICS versions: **H003, H004, H005**. Order coverage: the most
complete BTF/order palette possible.

## Build & tooling

- **.NET 10**, SDK pinned via `global.json`.
- `Directory.Build.props`: `Nullable enable`, `ImplicitUsings`, `TreatWarningsAsErrors`,
  `RestorePackagesWithLockFile`. Mandatory XML doc (`GenerateDocumentationFile`) only
  for `EBICO.Core` + `EBICO.Connector`.
- **Central package management** (`Directory.Packages.props`): versions ONLY there,
  `PackageReference` in the `.csproj` files without a version attribute.
- Tests: **xUnit v3** + **AwesomeAssertions** (MIT fork of FluentAssertions v7;
  FluentAssertions v8 is commercially licensed → deliberately NOT used).

## Project-wide, binding rules (Definition of Done per feature)

1. **DOCS:** Every feature is documented in Markdown under `docs/` and linked in
   the doc index (`docs/index.md`). Docs-as-Code: documentation belongs in the same
   PR as the code.
2. **TESTS:** Every feature is covered by unit tests (happy path +
   negative/edge cases). Protocol/crypto logic against test vectors and
   sample XML, not just self-consistency. No feature counts as done without tests.
3. **CI green:** `dotnet build` + `dotnet test`, no new warnings.
4. **XML-doc comments** on public APIs.
5. **Code review** carried out.

## Way of working

- **Issue-driven:** one branch (`feat/<no>-<slug>`) + one PR per issue, docs
  and tests included. The PR body uses `.github/PULL_REQUEST_TEMPLATE.md` and contains
  `Closes #<no>`.
- **Project language is English** (#133, epic #128): every contributor-facing text is written in
  English with **British spelling** (`authorisation`, `serialisation`) — `docs/` and ADRs, code
  comments and XML doc, Suite UI strings, `CLAUDE.md` and `.claude/skills/`, commit messages, PR
  descriptions and new issues. Reuse the terminology of the merged translations instead of coining
  new terms (e.g. *master data*, *subscriber*, *order type*, *bank-technical signature*, *return
  code*, *spec caveat*). Known exceptions: German file names, doc slugs and Suite routes stay until
  #134 renames them; the generated XSD bindings keep the German `<xs:documentation>` text of the
  proprietary schemas (ADR-0006, do not hand-edit); the INI/HIA letter
  (`InitializationLetterTextBuilder`) stays German because the subscriber prints and posts it to a
  German-speaking bank.
- Issues/milestones live on GitHub (`gh issue list`). Overview:
  `docs/ticket-overview.md` (10 milestones M0–M9, 63 issues, 12 epics).
- Roadmap: M0 (foundation) → M1 (core/protocol) → M2 (crypto) → M3–M5 (server)
  → M6 (connector) → M7 (Suite) → M8 (conformance) → M9 (packaging).

## Important constraints

- **LICENSE:** The EBICS schemas/specs are proprietary (EBICS SC). Modification /
  derivative uses without permission are not allowed. XSDs and official sample XML
  are **not** committed to the repo (`.gitignore`); obtain them locally via
  `scripts/fetch-schemas.sh`. The **generated C# bindings** under
  `src/EBICO.Core/Schema/` **are committed, however** (ADR-0006, option B;
  reproducible via `scripts/generate-bindings.sh`) — that way CI builds/tests without
  the schemas. Permission from the EBICS SC is being pursued in parallel.
- The architecture in `docs/connector/architecture.md` is a reasoned
  proposal, **not** a design verified against the spec. Once the real
  schemas are available, verify the details (e.g. order of E002/A00x/X002, segment loop per
  version) against the official XSDs/annexes and update the docs.

## Connector architecture in brief (details: `docs/connector/architecture.md`)

Mediator pattern: the caller only knows `IEbicsClient.Send(request)` and receives
a typed `EbicsResult<T>`. Pipeline per `Send`: validation → serialisation
→ compress/E002/A00x → X002 → transport (HttpClient behind `ITransport`) →
verify/decrypt → return code → segments if needed → deserialise. Own
dispatch instead of MediatR. Key store as an abstraction (`IKeyStore`).

## Documentation map (entry points)

- `docs/index.md` — annotated overall index; **always look here first**.
- `docs/server/order-coverage-matrix.md` — **source of truth** for order type/BTF ×
  version × status. Kept in sync with the code catalogues via a guard test
  (`OrderCoverageMatrixTests`); contains its own gaps section. Since #124 it separates
  **server** and **connector** availability: implemented server-side does not mean
  the bundled client can send that order type.
- `docs/adr/README.md` — 31 ADRs (0001–0031, MADR-lite, all `accepted`) + a backlog
  of open/superseded decisions. Every larger design question is reasoned out here.
- `docs/ticket-overview.md` — milestones (M0–M9), issues, epics.
- Feature docs live thematically under `docs/<area>/<name>.md`
  (`protocol/`, `server/`, `connector/`, `suite/`, `development/`, `deployment/`, `legal/`).

## Cross-cutting code conventions

- **Multi-version dispatch (H003/H004/H005):** the pervasive leitmotif. Per feature one
  version-agnostic base class (`<Xxx>OrderHandlerBase`) + one subclass per version
  (`H003<Xxx>OrderHandler` …). Tests span the version×case matrix via `TheoryData`.
- **DI registration (`AddEbicoServer` in `EbicoServerServiceCollectionExtensions.cs`):**
  infrastructure services (stores, verifiers, resolvers) with `TryAddSingleton` (overridable).
  Multi-registration extension points, by contrast, with `AddSingleton` (NOT `TryAdd`), so several
  can coexist: order handlers (`IEbicsOrderHandler`, resolved via
  `IEbicsOrderHandlerResolver` keyed by `(Version, OrderType)`) as well as upload/download
  processors (`IUploadOrderProcessor`/`IDownloadOrderProcessor`, the engine consumes the
  whole `IEnumerable<…>`, the first `CanProcess` match wins).
- **BTF/order-type resolution:** `BtfOrderTypeCatalog.Resolve{Upload,Download}OrderType`
  covers all three conventions (H005 BTU/BTD+BTF · H003/H004 direct code ·
  H003/H004 FUL/FDL+FileFormat). Authorisation: `Subscriber.HasPermissionFor` → `090003`.
  **Administrative order types have no BTF** and stay on the H005 `AdminOrderType` — that applies
  client-side to upload **and** download symmetrically (since #124; before that only the
  upload path demanded a BTF and thereby locked out the VEU uploads). The catalogue is a
  best-effort seed, hence **not** an oracle for whether an order type exists.
- **Guard tests keep docs↔code in sync:** a new order type must be added to the catalogue **and**
  the coverage matrix, otherwise `OrderCoverageMatrixTests` fails.
- **Shared transport defaults (#124/ADR-0030):** segment size and body limit are coupled — a
  base64-encoded segment travels *together with its envelope* in one HTTP body. The default therefore lives
  exactly once (`EbicsSegmentation.DefaultSegmentSizeBytes`, 512 KiB) and is consumed by `EbicoServerOptions` **and**
  the connector's `UploadExecutor`; `MaxSegmentSizeForRequestBody(…)` derives deviating values
  from it, `SegmentSizeCompatibilityTests` guards the relationship. Take care when changing this: the previous
  constellation (768 KiB client / 1 MiB server) made every multi-segment upload impossible (HTTP 413).
- **Response evaluation in the connector:** always resolve return code **and** report text together via
  `EbicsReturnCodes.CombineOutcome(headerCode, headerText, bodyCode)`. The `ReportText` only lives
  in the header — mixing it into a body code produced "`090005: EBICS_OK`" (#124).
- **Generated bindings + documented fixups:** `scripts/generate-bindings.sh` is not a pure
  generator — `apply_binding_fixups()` corrects, after every run, what xscgen cannot express
  (currently: strip `abstract` from `OrderDetailsType`, otherwise the `XmlSerializer` demands an
  `xsi:type` that real clients do not send — ADR-0029). Fixups belong **in the script** (not only
  in the committed `.cs`), need a guard test and an entry in
  `docs/protocol/xsd-bindings.md`; if the pattern is missing, the script aborts.
- **Test setup:** xUnit v3 + AwesomeAssertions; `TestContext.Current.CancellationToken`
  (the xUnit1051 trap under `TreatWarningsAsErrors`); server integration tests via
  `extern alias EbicoServer` + `WebApplicationFactory<Program>`; E2E via `EbicsE2EHarness`
  + `E2EKeyPool` (RSA-2048 is a hard lower bound ⇒ key reuse);
  XML comparison with `CanonicalXmlComparer`; proprietary sample XML is "skip-if-missing".
- **Spec caveats (current state):** server-side **X002 verification is active**
  (`X002EbicsRequestVerifier`, ADR-0023/#58, only takes effect after HIA). **ES/A00x signature verification
  of the order data remains deferred**; no key validity window; server responses are
  unsigned. For the **VEU** (ADR-0020/#124) the emulator only evaluates the `OrderID` — the
  remaining fields of the order params are sent schema-conformant but not checked; the parking triggers
  (`OZHNN`/`SignatureFlag`) and the HVE signature are unchecked. Parts of the architecture are design intent, not yet verified against the official
  XSDs (the schemas are proprietary). Two decisions are evidenced against a **real client**,
  not against the annexes (#117/ADR-0029): `OrderDetails` without `xsi:type`, and `A006`/PSS
  from H004 onwards (H003 excluded).

## Available skills (`.claude/skills/`)

Retrievable step-by-step recipes for the recurring workflows:

- `ebics-order-handler` — create a new server-side order handler or upload/download processor.
- `ebics-conformance-test` — write E2E/conformance tests (round-trip, wire shapes, vendor captures, tampering).
- `ebics-feature-workflow` — the complete feature/bugfix workflow incl. Definition of Done (branch → docs → ADR → tests → PR).
- `ebics-crypto` — EBICS crypto (A005/A006, X002, E002, fingerprints, X.509).
- `ebics-suite` — work on the Blazor Suite (pages/components, master data, inspector, key view).
- `ebics-connector` — work on the connector NuGet package (send pipeline, DI, send-side validation, packaging).

## Maintaining context, docs & skills

These context files do **not** maintain themselves. Keeping them up to date is part of the Definition
of Done and belongs in the **same PR** as the change that triggered it:

- **Docs (`docs/`):** document new/changed features and link them in `docs/index.md`;
  for order types, update `docs/server/order-coverage-matrix.md` (a guard test enforces this).
- **`CLAUDE.md`:** adjust it as soon as a cross-cutting convention, the project structure
  or a spec caveat changes.
- **Skills (`.claude/skills/`):** update them when a described workflow or a
  referenced symbol/path changes (e.g. renaming a handler, an interface or a
  doc page). The skills deliberately point at concrete files/types and otherwise go stale
  **silently** — there is no automatic guard for that.

Rule of thumb: if a PR touches a pattern described in `CLAUDE.md` or in a skill,
updating that text belongs in the same PR. The PR checklist
(`.github/PULL_REQUEST_TEMPLATE.md`) explicitly asks "Docs/skills updated?" and requires
an issue link (`Closes #<no>`) — **every** PR references exactly one issue, including pure
tooling/meta changes (e.g. to `.claude/` or `CLAUDE.md` itself).

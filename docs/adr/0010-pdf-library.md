# 0010 — PDF library for the INI/HIA letter: QuestPDF (Community)

- Status: accepted
- Date: 2026-07-09

## Context

With connector onboarding (issue **#47**, Milestone M6), `EBICO.Connector` generates
the **INI/HIA letter** — the document with the public-key fingerprints that the
subscriber signs and sends to the bank so it can reconcile the keys transferred via
INI/HIA against the letter. The letter is required as **text and PDF**.

So far the repo contains **no** PDF library (`Directory.Packages.props` lists only
test, DI and HTTP packages). PDF generation therefore requires a new dependency. The
project is deliberately restrained about dependencies (BCL-only for crypto —
[ADR-0008](0008-crypto-library.md); avoiding the commercially licensed
FluentAssertions v8 in favour of AwesomeAssertions — [ADR-0002](0002-test-stack.md)).
The license situation of a PDF package must therefore be checked explicitly.

## Decision

The INI/HIA letter is generated with **QuestPDF** under the **Community license**.
The version is pinned centrally in `Directory.Packages.props` (`PackageVersion`) and
referenced without a version in `EBICO.Connector.csproj`
([ADR-0001](0001-solution-layout-and-package-management.md)).

The letter is encapsulated behind the `IInitializationLetterRenderer` abstraction:

- `TextInitializationLetterRenderer` produces the letter **without** any dependency
  (plain text).
- `PdfInitializationLetterRenderer` additionally produces the PDF (QuestPDF) — it is
  the default implementation registered via `AddEbicoOnboarding()` and delivers text
  **and** PDF.

The shared body (`InitializationLetterTextBuilder`) ensures that the text and PDF
variants are identical in content. The Community license is set once in the static
constructor of the PDF renderer (`QuestPDF.Settings.License = LicenseType.Community`).

## Consequences

- **License:** QuestPDF Community is free for organisations below the revenue
  threshold defined by QuestPDF (currently USD 1M annual revenue). Above it, a
  commercial QuestPDF license is required. This must be checked before production
  use; the text renderer remains available at all times as a license-free fallback.
- **Dependency weight:** QuestPDF becomes a transitive dependency of the connector
  NuGet (including the bundled SkiaSharp renderer). This is in tension with the "lean
  dependency list" goal of the [connector architecture](../connector/architecture.md);
  the text renderer is therefore kept dependency-free, and the clean decoupling (PDF
  renderer in a separate package `EBICO.Connector.Pdf`) is documented as an option
  for later.
- **Headless/CI:** QuestPDF renders via a bundled SkiaSharp; the PDF tests check only
  validity (PDF magic `%PDF-`, non-empty), not layout, so they run
  platform-independently.
- **Risk/revision:** if QuestPDF changes its license terms or CI problems arise with
  SkiaSharp, this ADR is re-evaluated — preferably by moving the PDF renderer into an
  optional package or switching the PDF library; the text letter stays untouched.

## Alternatives

- **Text only (no PDF):** no new dependency, but the required PDF output is missing —
  rejected (requirement of the issue).
- **PdfSharp/MigraDoc:** MIT-licensed, no revenue threshold; API less fluent, layout
  more laborious. Remains a fallback option should the QuestPDF license become an
  obstacle.
- **iText:** AGPL or commercial — unsuitable for a potentially public NuGet, rejected.
- **PDF renderer in its own package `EBICO.Connector.Pdf`:** cleanest decoupling,
  keeps the connector core dependency-free; deferred for this issue (solution/packaging
  effort), noted as the preferred migration in the risk section.

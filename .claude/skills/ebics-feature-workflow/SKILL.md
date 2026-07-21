---
name: ebics-feature-workflow
description: >-
  Der verbindliche Ablauf für jedes Feature bzw. jeden Bugfix in EBICO, inklusive Definition of Done.
  Verwenden, sobald eine Änderung umgesetzt wird, die als Feature/Bugfix in einen PR mündet — von der
  Branch-Erstellung über Code, Doku und ADR bis zu Tests, grüner CI und PR. Kapselt die projektweiten
  DoD-Regeln (Docs-as-Code, Tests, keine neuen Warnungen, XML-Doc, Review) und die Repo-Konventionen.
---

# Feature-/Bugfix-Workflow (Definition of Done)

Issue-getrieben, pro Issue ein Branch + ein PR. Reihenfolge:

## 1. Branch

- Von `main` abzweigen. Namensschema: **`feat/<nr>-<slug>`** (z. B. `feat/59-conformance-real-clients`).
- Commit/Push nur, wenn der Nutzer es verlangt. Auf `main` nie direkt committen.

## 2. Code

- Bestehende Muster wiederverwenden (siehe Skills `ebics-order-handler`, `ebics-connector`, `ebics-suite`,
  `ebics-crypto`). Multi-Version-Dispatch (H003/H004/H005) beachten, wo relevant.
- `TreatWarningsAsErrors` ist aktiv, zentrale Paketverwaltung (`Directory.Packages.props`), `Nullable enable`.

## 3. Doku (Docs-as-Code, im **selben** PR)

- Neue Seite `docs/<bereich>/<name>.md` (`protocol/`, `server/`, `connector/`, `suite/`, `development/`,
  `deployment/`, `legal/`).
- **Verlinken in `docs/index.md`** unter der passenden Rubrik (sonst nutzloser Doku-Waise).
- Betrifft die Änderung Auftragsarten: `docs/server/order-coverage-matrix.md` aktualisieren.
- „Spec-Vorbehalte" explizit machen, wo Design-Intent statt XSD-verifiziert.

## 4. ADR (bei Designentscheidungen)

- Neue Datei `docs/adr/NNNN-<kebab-titel-deutsch>.md` mit der **nächsten freien Nummer** (aktuell endet
  der Bestand bei 0027). MADR-lite: Kontext / Entscheidung / Konsequenzen / Alternativen, Status `accepted`.
- Im ADR-Index `docs/adr/README.md` eintragen.

## 5. Tests

- Jedes Feature: Unit-Tests Happy Path **und** Negativ-/Grenzfälle. Protokoll-/Krypto-Logik gegen
  Testvektoren und Sample-XML, nicht nur Selbstkonsistenz.
- Testordner spiegeln die Produktordner (`tests/EBICO.Tests/{Core,Server,Connector,Crypto,Suite,E2E,…}`).
- E2E/Conformance: siehe Skill `ebics-conformance-test`.

## 6. CI grün

- `dotnet build` + `dotnet test` (Release), **keine neuen Warnungen**.
- `docs-link-check` (lychee offline über `**/*.md`) — tote Links vermeiden.
- Weitere CI-Jobs: `container-build` (Server-Image), `pack` (NuGet Core+Connector, CalVer, build-only).
- Tag-getriggerter Release (`release.yml`, #62/ADR-0027): Publish nach nuget.org + GHCR, auto-Release-Notes
  (nur auf `v*.*.*`-Tags; Runbook `docs/development/release.md`).

## 7. PR

- Body nach `.github/PULL_REQUEST_TEMPLATE.md` (wird von GitHub automatisch vorbefüllt).
- **Issue-Verlinkung ist Pflicht:** jeder PR enthält **`Closes #<nr>`** und referenziert genau
  ein Issue — auch reine Tooling-/Doku-Änderungen.
- Checkliste vollständig abhaken, insbesondere **„Docs/Skills aktualisiert?"** (siehe unten).
- Code-Review durchführen.

## Kontext-/Doku-Wartung (Teil der Definition of Done)

Berührt der PR ein Muster, das in `docs/`, `CLAUDE.md` oder einem Skill beschrieben ist, gehört
dessen Aktualisierung in **denselben** PR (siehe CLAUDE.md → „Wartung von Kontext, Doku & Skills").
Skills verweisen auf konkrete Symbole/Pfade und veralten sonst stillschweigend.

## Meta-/Tooling-Änderungen

Auch Änderungen an `.claude/`, `CLAUDE.md` oder der CI bekommen **ein eigenes Issue** + einen
eigenen kleinen Branch — nicht in einen fachlichen Feature-Branch mischen.

## Quellen

`CLAUDE.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/workflows/ci.yml`, `docs/adr/README.md`,
`docs/development/ci.md`, `docs/development/testing.md`, `docs/index.md`, `docs/ticket-overview.md`.

# EBICS-Schemas/Specs — Lizenz & Repo-Policy

Diese Seite ordnet die Lizenzlage der EBICS-Schemas/Spezifikationen ein und legt
die daraus abgeleitete **Repo-Policy** fest. Sie gehört zu Issue **#5 —
Lizenz-/Terms-of-Use-Klärung** (Milestone M0).

> ⚠️ **Kein Rechtsrat.** Dies ist eine technische/organisatorische Einordnung auf
> Basis der öffentlich einsehbaren EBICS-Nutzungsbedingungen, keine
> Rechtsberatung. Die verbindliche Entscheidung — insbesondere zur offenen Frage
> der generierten Bindings (siehe unten) — und ggf. die Kontaktaufnahme mit der
> EBICS SC liegen beim Betreiber des Projekts.

## Ausgangslage

Die EBICS-Schemas (XSDs) und -Spezifikationen sind **proprietäres Eigentum der
EBICS SC** (EBICS Société par Actions Simplifiée). Auf Basis der Quellen
(siehe [../protocol/schema-sources.md](../protocol/schema-sources.md)) gilt:

| | |
| --- | --- |
| ✅ **Erlaubt** | Download und **Reproduktion** der Schemas/Specs **mit vollständigem Copyright-Vermerk** (nicht-exklusiv, nicht-unterlizenzierbar). |
| ❌ **Nicht erlaubt** (ohne schriftliche Genehmigung der EBICS SC) | **Modifikation** und **derivative uses** der Schemas/Specs. |
| ⚠️ **Marken/Bezeichnung** | Produkte, die **nicht** auf den veröffentlichten Specs basieren, dürfen nicht „EBICS" genannt werden / das Logo nicht führen. |

## Repo-Policy (Entscheidung)

1. **Keine XSD-Dateien im Repository.** Die EBICS-XSDs werden **nicht** eingecheckt.
   `.gitignore` schließt sie aus:
   - `schemas/**/*.xsd`, `schemas/**/MANIFEST.sha256`, `schemas/manifest.json`
2. **Keine offiziellen Beispiel-XML im Repository.** Die EBICS-Beispiele
   (ebics.org) sind ebenso proprietär und werden nicht eingecheckt:
   - `tests/**/Fixtures/Xml/**/*.xml`
3. **Lokaler, reproduzierbarer Bezug** über
   [`scripts/fetch-schemas.sh`](../../scripts/fetch-schemas.sh): manueller Download
   (ablaufende securedl-URLs, „I accept") → Skript entpackt/sortiert/prüft per
   SHA-256-Manifest nach `schemas/<VERSION>/`.
4. **Copyright-Vermerke bleiben erhalten.** Beim lokalen Bezug werden die
   Original-Header der Dateien nicht entfernt; abgeleitete Artefakte verweisen auf
   die Herkunft.

Diese Policy ist bereits umgesetzt (M0): `.gitignore`, `fetch-schemas.sh`,
`schema-sources.md` und die Test-Fixture-READMEs spiegeln sie wider.

## Generierte Bindings: „derivative works"? (M1-Gate — entschieden)

M1 erzeugt aus den XSDs **C#-Bindings** (Klassen). Ob diese als **derivative use**
der proprietären Schemas gelten, war das M1-Gate. **Entschieden (Option B,
[../adr/0006-generierte-xsd-bindings-committen.md](../adr/0006-generierte-xsd-bindings-committen.md)):
die Bindings werden committet, die XSDs selbst bleiben ungetrackt.** So baut/testet
die CI den Protokoll-Kern ohne proprietäre Schemas; die schriftliche Genehmigung
der EBICS SC wird parallel verfolgt.

Optionen (zur Einordnung, Details in [ADR-0006](../adr/0006-generierte-xsd-bindings-committen.md)):

- **(A) Bindings nicht committen — beim Build aus lokal bezogenen XSDs generieren.**
  Konservativ, keine generierten Ableitungen im Repo. Nachteil: Contributor/CI
  brauchen die lokal bezogenen XSDs zum Bauen → der schema-abhängige Teil von
  `EBICO.Core` ist ohne Schemas nicht baubar/testbar. **Verworfen.**
- **(B) Bindings committen (XSDs bleiben ungetrackt).** Beste Developer-Experience;
  CI baut/testet den Protokoll-Kern ohne proprietäre Schemas. **Gewählt.**
- **(C) Handgeschriebene Modelle** statt generierter Bindings — kein direkter
  derivative use des XSD-Texts, dafür deutlich höherer Aufwand und Fehlerrisiko.

**Entscheidung:** **(B)** — die generierten Bindings werden committet (XSDs
bleiben ungetrackt), Genehmigung der EBICS SC parallel verfolgt. Begründung und
Konsequenzen: [ADR-0006](../adr/0006-generierte-xsd-bindings-committen.md). Es
werden nur generierte Artefakte committet, nicht der XSD-Originaltext; lässt die
EBICS SC dies nicht zu, sind die Bindings entfernbar/neu generierbar.

## Bezug zu EBICO

„EBICO" ist eine eigenständige Emulator-/Client-Implementierung und führt kein
EBICS-Branding. Konformitätsaussagen sind nur zulässig, soweit die Implementierung
den veröffentlichten Specs entspricht (vgl. M8 — Validation & Conformance).

## Verweise

- [Schema-Quellen & Bezug](../protocol/schema-sources.md)
- [`scripts/fetch-schemas.sh`](../../scripts/fetch-schemas.sh)
- EBICS Terms of Use: <https://www.ebics.org/en/informationen/disclaimer>

# EBICS Schema- und Spezifikationsquellen

Diese Seite dokumentiert, **woher** die EBICS-Schemas und -Spezifikationen
stammen, **wie** sie reproduzierbar ins Repo gelangen und **welche
rechtlichen Rahmenbedingungen** gelten. Sie ist die zentrale Referenz für
das Issue *„Schemas & Specs beschaffen"*.

> **Kurzfassung:** Es gibt keine stabilen Direktlinks. Downloads liegen hinter
> einem „I accept"-Button und werden über ablaufende `securedl`-URLs
> ausgeliefert. Daher: manuell laden, dann `scripts/fetch-schemas.sh` für den
> reproduzierbaren Rest.

---

## 1. Quellen (stabile Seiten-URLs)

| Inhalt | Version(en) | URL |
|---|---|---|
| EBICS Schema (aktuell) | H005 / EBICS 3.0 | https://www.ebics.org/en/technical-information/ebics-schema |
| Schema-Archiv | H004 / EBICS 2.5 und älter | https://www.ebics.org/en/technical-information/archive-ebics/schema |
| EBICS-Spezifikation (aktuell) | V 3.0.2 (gültig ab 30.12.2022) | https://www.ebics.org/en/technical-information/ebics-specification |
| Spezifikations-Archiv | ältere Versionen | https://www.ebics.org/en/technical-information/archive-ebics/specification |
| BTF-Mapping / External Code List | versionsunabhängig (zuletzt 23.10.2024) | https://www.ebics.org/en/technical-information/btf-mapping |
| Implementation Guide | — | https://www.ebics.org/en/technical-information/implementation-guide |
| Security Concept (Annex „TLS and KMS") | versionsunabhängig | https://www.ebics.org/en/technical-information/security-concept |
| Beispiele (Sample-XML) | — | https://www.ebics.org/en/technical-information/examples |
| Additional Standards | — | https://www.ebics.org/en/technical-information/additional-standards |
| Passed Change Requests | — | https://www.ebics.org/en/technical-information/maintain-advance/passed-crs |
| Terms of Use | — | https://www.ebics.org/en/informationen/disclaimer |

---

## 2. Enthaltene Schemadateien

### H005 (EBICS 3.0) — aus dem aktuellen Schema-ZIP

| Datei | Zweck |
|---|---|
| `ebics_H005.xsd` | Master-Schema, inkludiert alle anderen (Konsistenz) |
| `ebics_request_H005.xsd` | Protokoll-Schema für Standard-Requests |
| `ebics_response_H005.xsd` | Protokoll-Schema für Standard-Responses |
| `ebics_orders_H005.xsd` | Order-bezogene Referenzelemente und Typdefinitionen |
| `ebics_types_H005.xsd` | einfache Typdefinitionen |
| `ebics_keymgmt_request_H005.xsd` | Protokoll-Schema für Key-Management-Requests |
| `ebics_keymgmt_response_H005.xsd` | Protokoll-Schema für Key-Management-Responses |
| `ebics_hev.xsd` | H000 — OrderType HEV |
| `ebics_signature.xsd` | S002 — elektronische Signatur (Minor-Update von S001) |
| `xmldsig-core-schema.xsd` | W3C — Standard-Schema für XML-Signatur |

**Instant Payments:** Für das Clearing von Instant Payments gibt es eine
separate Request-XSD. Diese muss in `ebics_H005.xsd` die Standard-Request-XSD
als `include` **ersetzen**. Details siehe „EBICS Delta concept" auf der
Spezifikationsseite.

> Hinweis aus der Quelle: Am 07.08.2017 wurde `ebics_orders_H005.xsd`
> aktualisiert (Wieder-Einführung der Elementgruppe `standardOrderParams`,
> u. a. für HAC-Downloads benötigt).

### H004 (EBICS 2.5) — aus dem Schema-Archiv

Analoge Dateistruktur mit `H004`-Suffix (`ebics_H004.xsd`,
`ebics_request_H004.xsd`, …). Die genaue Dateiliste bitte beim Bezug gegen das
Archiv-ZIP verifizieren.

### H003 (EBICS 2.4)

Aus dem Schema-Archiv. Älter, teils abweichende Struktur — beim Bezug prüfen.

---

## 3. Beschaffung — Schritt für Schritt

1. **Schema-ZIP manuell laden:**
   - H005: Schema-Seite öffnen, Terms „I accept" bestätigen, ZIP speichern.
   - H004/H003: dasselbe auf der Archiv-Seite.
2. **Aufbereiten lassen:**
   ```bash
   ./scripts/fetch-schemas.sh --zip ~/Downloads/<ebics_3.0_schema>.zip --version H005
   ./scripts/fetch-schemas.sh --zip ~/Downloads/<ebics_2.5_schema>.zip --version H004
   ```
   Optional `--strict`, damit fehlende erwartete Dateien zum Fehler führen.
3. **Ergebnis prüfen:**
   - Dateien unter `schemas/<VERSION>/`
   - `schemas/<VERSION>/MANIFEST.sha256` (Checksums je Datei)
   - `schemas/manifest.json` (aggregiert über alle Versionen, inkl.
     Quell-ZIP-Hash und Bezugszeitpunkt)
4. **Vor dem Commit:** Lizenzfrage klären (siehe unten).

Das Skript ist idempotent: Es befüllt das Versionsverzeichnis sauber neu und
erhält die Metadaten der jeweils anderen Versionen im aggregierten Manifest.

---

## 4. Lizenz / Terms of Use — bitte beachten

Die Schemas und Spezifikationen sind **proprietäres Eigentum der EBICS SC**.
Aus den Terms of Use (Stand der Erfassung):

- **Erlaubt:** Herunterladen und Reproduzieren, sofern alle Copyright-Vermerke
  vollständig erhalten bleiben (nicht-exklusive, nicht unterlizenzierbare
  Lizenz).
- **Nicht erlaubt** (ohne vorherige schriftliche Genehmigung der EBICS SC):
  Modifikation oder sonstige *derivative uses* der Spezifikationen.
- Produkte/Dienste, die **nicht** auf den veröffentlichten EBICS-Specs
  basieren, dürfen nicht „EBICS" heißen und nicht mit dem EBICS-Logo
  gekennzeichnet werden.

### Konsequenzen fürs Projekt (zu klären, kein Rechtsrat)

- **XSDs ins Repo?** Solange ungeklärt: **nicht committen.** Stattdessen jeder
  Entwickler/CI-Job zieht sie lokal per `fetch-schemas.sh`. Ein
  `.gitignore`-Eintrag für `schemas/**/*.xsd` verhindert versehentliche Commits.
- **Generierte XSD-Bindings** (z. B. via `XmlSerializer`-Codegen) könnten als
  *derivative use* gewertet werden. Risiko bewerten; im Zweifel bei
  `info@ebics.de` anfragen.
- **Copyright-Vermerke** reproduzierter Inhalte vollständig übernehmen.

Entscheidung und Begründung gehören in eine ADR sowie nach
`docs/legal/ebics-licensing.md`.

---

## 5. Versionsstände (zum Mitführen)

| Artefakt | Stand bei Erfassung |
|---|---|
| EBICS-Spezifikation | V 3.0.2, gültig ab 30.12.2022 (Revision von V 3.0.1) |
| BTF External Code List | zuletzt aktualisiert 23.10.2024 |
| Annex „TLS and KMS" | umbenannt/erweitert am 20.03.2026 (vormals „Transport Layer Security") |
| `ebics_orders_H005.xsd` | aktualisiert 07.08.2017 |

> Diese Stände sind ein Schnappschuss. Beim tatsächlichen Bezug die auf
> ebics.org angegebenen Daten/Subversionen erneut prüfen und im
> `schemas/manifest.json` festhalten.

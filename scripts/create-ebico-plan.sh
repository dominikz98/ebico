#!/usr/bin/env bash
###############################################################################
# create-ebico-plan.sh
#
# Legt Milestones, Labels und Issues fuer das EBICO-Projekt via gh-CLI an.
#
# Voraussetzungen:
#   - gh (GitHub CLI) installiert und authentifiziert:  gh auth login
#   - Du befindest dich in einem geklonten Repo ODER setzt REPO unten.
#
# Nutzung:
#   REPO="DeinUser/ebico" ./create-ebico-plan.sh
#   (oder REPO leer lassen, dann nimmt gh das aktuelle Repo)
#
# Hinweis: Das Skript ist idempotent-freundlich gebaut: Milestones/Labels
# werden uebersprungen, wenn sie schon existieren. Issues werden jedoch
# IMMER neu erzeugt - nicht zweimal laufen lassen, sonst Duplikate.
###############################################################################
set -euo pipefail

REPO="${REPO:-}"                 # z.B. "dominik/ebico" - leer = aktuelles Repo
GH_REPO_FLAG=()
[[ -n "$REPO" ]] && GH_REPO_FLAG=(--repo "$REPO")

echo ">> Verwende Repo: ${REPO:-<aktuelles>}"

# ---------------------------------------------------------------------------
# Zentrale Quellen-Referenzen (ebics.org)
#
# WICHTIG: Die eigentlichen ZIP/PDF-Dateien liegen hinter einem "I accept"-
# Button und werden ueber signierte, ABLAUFENDE securedl-URLs ausgeliefert.
# Solche Links sind nicht stabil und werden daher NICHT eingebettet.
# Stattdessen werden die stabilen Seiten-URLs referenziert; dort muss einmalig
# den Terms of Use zugestimmt und das ZIP/PDF gezogen werden.
#
# LIZENZ: Schemas/Specs sind proprietaer (EBICS SC). Download + Reproduktion
# mit Copyright-Vermerk erlaubt; Modifikation / derivative uses NICHT ohne
# schriftliche Genehmigung. -> XSDs nicht ungefragt ins oeffentliche Repo
# committen; per Beschaffungsskript ziehen (siehe Issue "Schemas & Specs
# beschaffen").
# ---------------------------------------------------------------------------
SRC_SCHEMA="https://www.ebics.org/en/technical-information/ebics-schema"
SRC_SCHEMA_ARCHIVE="https://www.ebics.org/en/technical-information/archive-ebics/schema"
SRC_SPEC="https://www.ebics.org/en/technical-information/ebics-specification"
SRC_SPEC_ARCHIVE="https://www.ebics.org/en/technical-information/archive-ebics/specification"
SRC_BTF="https://www.ebics.org/en/technical-information/btf-mapping"
SRC_IMPLGUIDE="https://www.ebics.org/en/technical-information/implementation-guide"
SRC_SECURITY="https://www.ebics.org/en/technical-information/security-concept"
SRC_EXAMPLES="https://www.ebics.org/en/technical-information/examples"
SRC_ADDSTD="https://www.ebics.org/en/technical-information/additional-standards"
SRC_CRS="https://www.ebics.org/en/technical-information/maintain-advance/passed-crs"
SRC_TERMS="https://www.ebics.org/en/informationen/disclaimer"

# Hinweisblock zur Schema-Beschaffung, der an Schema-/Protokoll-Issues angehaengt wird
SCHEMA_HINT="

---
**Quellen / Schemas (ebics.org)**
- Aktuelle Schemas (H005 / EBICS 3.0): ${SRC_SCHEMA}
- Schema-Archiv (H004 / EBICS 2.5 und aelter): ${SRC_SCHEMA_ARCHIVE}
- Spezifikation (aktuell V 3.0.2): ${SRC_SPEC}

> Download hinter \"I accept\"-Button (ablaufende securedl-Links). Terms of Use beachten: ${SRC_TERMS}
> Schemas NICHT modifiziert ins Repo committen - per Beschaffungsskript ziehen."


# ---------------------------------------------------------------------------
# Labels
# ---------------------------------------------------------------------------
create_label () {
  local name="$1" color="$2" desc="$3"
  if gh label list "${GH_REPO_FLAG[@]}" --limit 200 | grep -qiE "^${name}\s"; then
    echo "   label '${name}' existiert bereits - skip"
  else
    gh label create "$name" "${GH_REPO_FLAG[@]}" --color "$color" --description "$desc" || true
    echo "   label '${name}' angelegt"
  fi
}

echo ">> Labels anlegen..."
create_label "epic"            "5319e7" "Uebergeordnetes Arbeitspaket"
create_label "connector"       "1d76db" "EBICO.Connector (NuGet)"
create_label "server"          "0e8a16" "EBICO.Server (Emulator)"
create_label "suite"           "fbca04" "EBICO.Suite (Blazor UI)"
create_label "core"            "c5def5" "EBICO.Core (shared)"
create_label "crypto"          "b60205" "Signatur / Verschluesselung / Zertifikate"
create_label "protocol"        "d93f0b" "Protokoll / XSD / Serialisierung"
create_label "orders"          "0052cc" "Order-Typen / BTF"
create_label "infra"           "bfdadc" "Build / CI / Tooling"
create_label "docs"            "006b75" "Issue ist primaer eine Doku-Aufgabe"
create_label "H003"            "ededed" "EBICS H003 (2.4)"
create_label "H004"            "ededed" "EBICS H004 (2.5)"
create_label "H005"            "ededed" "EBICS H005 (3.0)"
create_label "tests"           "2da44e" "Unit-/Integrationstests verpflichtend"
create_label "documentation"   "0075ca" "Markdown-Doku verpflichtend"
create_label "legal"           "e99695" "Lizenz / Terms of Use / Rechtliches"

# ---------------------------------------------------------------------------
# Milestones (via REST, da gh keine native milestone-create-Subcommand hat)
# ---------------------------------------------------------------------------
declare -A MS_NUMBER          # title -> number

resolve_repo () {
  if [[ -n "$REPO" ]]; then echo "$REPO"; else
    gh repo view --json nameWithOwner -q .nameWithOwner; fi
}
FULL_REPO="$(resolve_repo)"
echo ">> Voller Repo-Slug: $FULL_REPO"

create_milestone () {
  local title="$1" desc="$2"
  # existiert schon?
  local existing
  existing="$(gh api "repos/$FULL_REPO/milestones?state=all&per_page=100" \
      --jq ".[] | select(.title==\"$title\") | .number" 2>/dev/null || true)"
  if [[ -n "$existing" ]]; then
    echo "   milestone '$title' existiert (#$existing) - skip"
    MS_NUMBER["$title"]="$existing"
    return
  fi
  local num
  num="$(gh api "repos/$FULL_REPO/milestones" -f title="$title" -f description="$desc" --jq .number)"
  MS_NUMBER["$title"]="$num"
  echo "   milestone '$title' angelegt (#$num)"
}

echo ">> Milestones anlegen..."
create_milestone "M0 - Foundation & Tooling"        "Repo-Setup, Solution-Struktur, CI, Test-Harness."
create_milestone "M1 - Core & Protocol Primitives"  "EBICO.Core: XSD-Bindings, Serialisierung, Datenmodelle fuer H003/H004/H005."
create_milestone "M2 - Cryptography & Certificates"  "Signatur (A005/A006/E002/X002), Hashing, Zertifikatsverifizierung, Key-Mgmt-Krypto."
create_milestone "M3 - Server: Key Management"       "INI/HIA/HPB/HSA/SPR Ablauf im Emulator, Benutzer-/Partner-/Bank-Verwaltung."
create_milestone "M4 - Server: Transaction Engine"   "Upload/Download-Transaktionsmaschine, Segmentierung, Recovery, Quittungen."
create_milestone "M5 - Server: Orders & BTF"         "Order-Type/BTF-Abdeckung (CCT, CDD, STA, C5x, HAC, HTD, ...)."
create_milestone "M6 - Connector (NuGet)"            "EBICO.Connector Client-Bibliothek, fluent API, Order-Submission."
create_milestone "M7 - Suite (Blazor UI)"            "Admin-/Inspektor-UI fuer den Emulator-Zustand und Transaktionen."
create_milestone "M8 - Validation & Conformance"     "End-to-End-Tests, Konformitaet gegen echte Clients, Negativfaelle."
create_milestone "M9 - Packaging & Docs"             "NuGet-Publish, Container-Image, Quickstart-Doku, Beispiele."

# ---------------------------------------------------------------------------
# Issue-Helper
# ---------------------------------------------------------------------------
# Querschnitts-"Definition of Done", die an JEDES Feature-Issue angehaengt wird.
# Gilt projektweit: nichts gilt als fertig ohne Doku + Tests.
DOD_BLOCK="

---
**Definition of Done (projektweit verpflichtend)**
- [ ] **Doku:** Feature in Markdown unter \`docs/\` beschrieben (Zweck, Ablauf, Beispiel-XML/Code, EBICS-Versionsbezug) und im Doku-Index verlinkt
- [ ] **Tests:** Unit-Tests fuer die Kernlogik (Happy Path + relevante Negativ-/Grenzfaelle); bei Protokoll-/Krypto-Themen mit Testvektoren/Sample-XML
- [ ] **CI gruen:** \`dotnet build\` + \`dotnet test\` erfolgreich, keine neuen Warnungen
- [ ] **XML-Doc-Kommentare** an oeffentlichen APIs
- [ ] Code-Review durchgefuehrt"

mk () {
  # mk "<milestone-title>" "<labels,csv>" "<title>" "<body>" [no-dod]
  # Optionaler 5. Parameter "no-dod" haengt den DoD-Block NICHT an
  # (fuer Epics/Meta-Issues). Standardmaessig werden tests+documentation-Labels
  # und der DoD-Block ergaenzt.
  local ms="$1" labels="$2" title="$3" body="$4" mode="${5:-dod}"
  if [[ "$mode" != "no-dod" ]]; then
    body="${body}${DOD_BLOCK}"
    # tests + documentation Labels sicherstellen (ohne Duplikate)
    [[ ",$labels," == *",tests,"* ]] || labels="${labels},tests"
    [[ ",$labels," == *",documentation,"* ]] || labels="${labels},documentation"
  fi
  local label_flags=()
  IFS=',' read -ra L <<< "$labels"
  for l in "${L[@]}"; do label_flags+=(--label "$l"); done
  gh issue create "${GH_REPO_FLAG[@]}" \
      --title "$title" \
      --body "$body" \
      --milestone "$ms" \
      "${label_flags[@]}" >/dev/null
  echo "   issue: $title"
}

echo ">> Issues anlegen..."

# ===========================================================================
# M0 - Foundation & Tooling
# ===========================================================================
mk "M0 - Foundation & Tooling" "epic,infra" \
"EPIC: Foundation & Tooling" \
"Repo-Grundgeruest, Solution-Layout, CI/CD, Test-Harness und Coding-Standards.

**Projektstruktur (5 Projekte):**
- \`EBICO.Core\`    - geteilte Primitives (Schemas, Serialisierung, Krypto-Abstraktionen, Modelle)
- \`EBICO.Connector\` - NuGet-Client fuer Zugriff auf einen EBICS-Server
- \`EBICO.Server\`   - der Emulator (Hostable)
- \`EBICO.Suite\`    - Blazor-UI fuer den Server
- \`EBICO.Tests\`    - Unit/Integration/Conformance

**Akzeptanz:** \`dotnet build\` + \`dotnet test\` laufen lokal und in CI gruen." "no-dod"

mk "M0 - Foundation & Tooling" "epic,documentation" \
"EPIC: Dokumentationsstrategie (Markdown / docs/)" \
"Querschnittsanforderung: **jedes Feature wird umfassend in Markdown dokumentiert.** Diese Doku lebt im Repo unter \`docs/\` und wird gemeinsam mit dem Code reviewt (Docs-as-Code).

**Struktur (Vorschlag):**
- \`docs/README.md\` - Doku-Index / Einstieg
- \`docs/architecture/\` - ADRs, Komponentenuebersicht, Sequenzdiagramme
- \`docs/protocol/\` - EBICS-Versionen, Envelope-Aufbau, Order/BTF-Referenz
- \`docs/crypto/\` - Signatur-/Verschluesselungsverfahren, Testvektoren
- \`docs/server/\`, \`docs/connector/\`, \`docs/suite/\` - komponentenspezifisch
- \`docs/guides/\` - Quickstarts, How-Tos

**Akzeptanz:**
- [ ] Doku-Index + Verzeichnisstruktur stehen
- [ ] Doku-Template fuer Feature-Seiten (Zweck, Ablauf, Beispiel, Versionsbezug, Tests-Verweis)
- [ ] CI-Check, der tote interne Doku-Links bricht (Link-Checker)
- [ ] Beitragsrichtlinie: kein Feature-PR ohne zugehoerige Doku" "no-dod"

mk "M0 - Foundation & Tooling" "epic,tests" \
"EPIC: Teststrategie (Unit-Tests pro Feature)" \
"Querschnittsanforderung: **jedes Feature wird mit Unit-Tests abgesichert.** Kein Feature gilt als fertig ohne Tests; CI erzwingt das.

**Leitlinien:**
- Test-First/begleitend, Happy Path + Negativ-/Grenzfaelle
- Protokoll-/Krypto-Logik gegen **Testvektoren** und **Sample-XML** (nicht nur Selbstkonsistenz)
- deterministische Tests (feste Test-Keys/Test-CA, kein Netz)
- Coverage als Sichtbarkeit, nicht als Selbstzweck (sinnvolle Schwelle definieren)

**Akzeptanz:**
- [ ] xUnit + FluentAssertions eingerichtet (\`EBICO.Tests\`)
- [ ] Coverage-Report in CI als Artefakt
- [ ] Mindest-Coverage-Gate fuer \`EBICO.Core\` (Wert in ADR festlegen)
- [ ] PR-Vorlage mit Test-Checkliste
- [ ] Branch-Protection: Tests muessen gruen sein" "no-dod"

mk "M0 - Foundation & Tooling" "infra,protocol,legal" \
"Schemas & Specs beschaffen (Beschaffungsskript)" \
"Alle benoetigten EBICS-Schemas und Spezifikationen von ebics.org beziehen und reproduzierbar bereitstellen. **Manueller Schritt noetig:** Downloads liegen hinter einem \"I accept\"-Button und werden ueber ablaufende \`securedl\`-URLs ausgeliefert - es gibt keine stabilen Direktlinks.

**Quellen (Seiten-URLs):**
- H005 / EBICS 3.0 Schemas: ${SRC_SCHEMA}
- H004 / EBICS 2.5 (und aelter) Schema-Archiv: ${SRC_SCHEMA_ARCHIVE}
- Spezifikation (aktuell V 3.0.2, gueltig ab 30.12.2022): ${SRC_SPEC}
- Spezifikations-Archiv (aeltere Versionen): ${SRC_SPEC_ARCHIVE}
- BTF-Mapping / External Code List: ${SRC_BTF}
- Implementation Guide: ${SRC_IMPLGUIDE}
- Security Concept (Annex 'TLS and KMS'): ${SRC_SECURITY}
- Beispiele (Sample-XML): ${SRC_EXAMPLES}
- Additional Standards: ${SRC_ADDSTD}
- Passed Change Requests: ${SRC_CRS}

**Enthaltene H005-Schemadateien (laut Schema-Seite):**
- \`ebics_H005.xsd\` (Master-Include)
- \`ebics_request_H005.xsd\`, \`ebics_response_H005.xsd\`
- \`ebics_orders_H005.xsd\`, \`ebics_types_H005.xsd\`
- \`ebics_keymgmt_request_H005.xsd\`, \`ebics_keymgmt_response_H005.xsd\`
- \`ebics_hev.xsd\` (H000, OrderType HEV)
- \`ebics_signature.xsd\` (S002)
- \`xmldsig-core-schema.xsd\` (W3C XML-Signatur)
- separate Instant-Payment-Request-XSD (ersetzt \`ebics_request_H005.xsd\` als Include in \`ebics_H005.xsd\`)

**Aufgaben:**
- [ ] \`scripts/fetch-schemas.sh\` (lokaler Pfad zu manuell heruntergeladenem ZIP -> entpacken nach \`schemas/{H005,H004,H003}/\`)
- [ ] Checksums (SHA-256) der bezogenen Dateien festhalten (Versionierung/Reproduzierbarkeit)
- [ ] Bezugsdatum + Spec-(Sub-)Version dokumentieren (z.B. V 3.0.2)
- [ ] \`schemas/\` ggf. via \`.gitignore\` ausschliessen (siehe Lizenz-Issue) ODER bewusst mit Copyright-Vermerk ablegen
- [ ] Doku-Seite \`docs/protocol/schema-sources.md\` mit allen Quell-URLs + Vorgehen" "no-dod"

mk "M0 - Foundation & Tooling" "legal,docs" \
"Lizenz-/Terms-of-Use-Klaerung (EBICS-Schemas/Specs)" \
"Die EBICS-Schemas und -Spezifikationen sind **proprietaeres Eigentum der EBICS SC**. Vor Verteilung/Generierung klaeren.

**Kernpunkte der Terms of Use (${SRC_TERMS}):**
- Erlaubt: Download + Reproduktion, sofern Copyright-Vermerke vollstaendig erhalten bleiben.
- **Nicht** erlaubt ohne schriftliche Genehmigung: Modifikation oder sonstige derivative uses der Specs.
- Produkte, die nicht auf den veroeffentlichten Specs basieren, duerfen nicht 'EBICS' heissen / das Logo tragen.

**Zu klaeren / umzusetzen:**
- [ ] Duerfen die XSDs (unveraendert) ins Repo? Falls unklar -> nicht committen, nur per Beschaffungsskript ziehen
- [ ] Sind generierte XSD-Bindings ein 'derivative use'? Risiko bewerten / ggf. bei info@ebics.de anfragen
- [ ] Copyright-Vermerk-Handling fuer reproduzierte Inhalte definieren
- [ ] Entscheidung in ADR + \`docs/legal/ebics-licensing.md\` festhalten
- [ ] NOTICE/THIRD-PARTY-Datei im Repo vorbereiten" "no-dod"

mk "M0 - Foundation & Tooling" "infra" \
"Solution & Projektgeruest anlegen" \
"- [ ] \`EBICO.sln\` mit den 5 Projekten (net10.0)
- [ ] \`Directory.Build.props\` (LangVersion, Nullable enable, TreatWarningsAsErrors)
- [ ] zentrale Paketversionsverwaltung (\`Directory.Packages.props\`)
- [ ] \`.editorconfig\`, README-Stub
- [ ] \`docs/\`-Grundstruktur + Doku-Index anlegen
- [ ] \`.github/PULL_REQUEST_TEMPLATE.md\` mit Doku- und Test-Checkliste
- [ ] Solution-Folder-Struktur (src/, tests/, docs/)"

mk "M0 - Foundation & Tooling" "infra" \
"CI-Pipeline (GitHub Actions)" \
"- [ ] build + test Workflow auf push/PR
- [ ] dotnet-Version-Pinning
- [ ] Cache fuer NuGet
- [ ] Test-Report / Coverage-Artefakt
- [ ] Doku-Link-Checker als CI-Step
- [ ] (spaeter) Pack + Publish-Job fuer Connector"

mk "M0 - Foundation & Tooling" "infra" \
"Test-Harness & Fixtures" \
"- [ ] Test-Framework waehlen (xUnit) + FluentAssertions
- [ ] Verzeichnis fuer Sample-XML (request/response je Version)
- [ ] Schluessel-/Zertifikat-Fixtures fuer Tests (Test-CA, Test-Keys)
- [ ] Hilfsmittel zum XML-Vergleich (kanonisiert)

---
**Quelle fuer Sample-XML:** offizielle EBICS-Beispiele: ${SRC_EXAMPLES}"

mk "M0 - Foundation & Tooling" "infra,docs" \
"Architektur-Entscheidungen dokumentieren (ADRs)" \
"- [ ] ADR-Format festlegen
- [ ] ADR: Mehr-Versionen-Strategie (H003/H004/H005 nebeneinander)
- [ ] ADR: Persistenz im Server (In-Memory + pluggable Store?)
- [ ] ADR: Krypto-Bibliothek (BouncyCastle vs. System.Security.Cryptography)"

# ===========================================================================
# M1 - Core & Protocol Primitives
# ===========================================================================
mk "M1 - Core & Protocol Primitives" "epic,core,protocol" \
"EPIC: Core & Protocol Primitives" \
"Geteilte Protokoll-Grundlagen in \`EBICO.Core\`: XSD-Bindings, Serialisierung und Datenmodelle fuer alle drei Versionen.

> Hinweis: Offizielle XSDs/Schemas von ebics.org beziehen und unter \`schemas/{H003,H004,H005}/\` ablegen. Lizenz/Weitergabe pruefen (siehe Issues 'Schemas & Specs beschaffen' und 'Lizenz-/Terms-of-Use-Klaerung').
>
> Schemas (H005): ${SRC_SCHEMA} | Archiv (H004/H003): ${SRC_SCHEMA_ARCHIVE} | Spezifikation: ${SRC_SPEC}" "no-dod"

mk "M1 - Core & Protocol Primitives" "core,protocol,H005" \
"XSD-Bindings generieren - H005 (EBICS 3.0)" \
"- [ ] offizielle H005-Schemas einbinden (ebics_request/response/keymgmt, HVE, BTF ...)
- [ ] Code-Gen-Strategie (XmlSerializer-Klassen vs. handgeschrieben)
- [ ] generierte Typen unter \`EBICO.Core/Schema/H005\`
- [ ] Round-Trip-Test gegen Sample-XML${SCHEMA_HINT}"

mk "M1 - Core & Protocol Primitives" "core,protocol,H004" \
"XSD-Bindings generieren - H004 (EBICS 2.5)" \
"- [ ] H004-Schemas einbinden
- [ ] Typen generieren
- [ ] Round-Trip-Test${SCHEMA_HINT}"

mk "M1 - Core & Protocol Primitives" "core,protocol,H003" \
"XSD-Bindings generieren - H003 (EBICS 2.4)" \
"- [ ] H003-Schemas einbinden
- [ ] Typen generieren
- [ ] Round-Trip-Test
- [ ] Abweichungen zu H004 dokumentieren${SCHEMA_HINT}"

mk "M1 - Core & Protocol Primitives" "core,protocol" \
"Versionsabstraktion / Protokoll-Dispatch" \
"- [ ] gemeinsame Schnittstellen ueber Versionen (IEbicsRequest/Response-Abstraktion)
- [ ] Mapping Version -> konkrete Typen
- [ ] Erkennung der Version aus eingehendem Envelope
- [ ] Strategie fuer versionsspezifische Order-/BTF-Behandlung"

mk "M1 - Core & Protocol Primitives" "core,protocol" \
"XML-Serialisierung & Canonicalization (C14N)" \
"- [ ] Exklusive XML-Canonicalization fuer Signaturen
- [ ] deterministische Serialisierung (Namespaces, Praefixe)
- [ ] Tests gegen bekannte C14N-Vektoren"

mk "M1 - Core & Protocol Primitives" "core" \
"Domaenenmodell: Bank / Partner / User / Subscriber" \
"- [ ] Modelle fuer HostID, PartnerID, UserID, SystemID
- [ ] Berechtigungen / Transport- vs. Bankunterschrift
- [ ] Zustaende eines Subscribers (New, Initialized, Ready, Suspended)"

# ===========================================================================
# M2 - Cryptography & Certificates
# ===========================================================================
mk "M2 - Cryptography & Certificates" "epic,crypto" \
"EPIC: Cryptography & Certificates" \
"Komplette Krypto-Schicht: Signaturverfahren, Verschluesselung, Hashing und Zertifikatsverifizierung gemaess EBICS-Annex.

Verfahren u.a.: **A005/A006** (Banktechnische Unterschrift, RSASSA-PKCS1/PSS), **E002** (Verschluesselung, RSA + AES-128 CBC), **X002** (Authentifikationssignatur).

---
**Quellen:**
- Security Concept (Annex 'TLS and KMS'): ${SRC_SECURITY}
- Signatur-Schema \`ebics_signature.xsd\` (S002) + \`xmldsig-core-schema.xsd\` (W3C): ${SRC_SCHEMA}
- Spezifikation (Krypto-Annexe, V 3.0.2): ${SRC_SPEC}" "no-dod"

mk "M2 - Cryptography & Certificates" "crypto" \
"Schluesselpaare & -repraesentation (A/E/X)" \
"- [ ] Modelle fuer Auth-, Enc-, Signatur-Keys (A00x/E002/X002)
- [ ] Import/Export (PKCS#8, X.509)
- [ ] Key-Versionen pro EBICS-Version mappen"

mk "M2 - Cryptography & Certificates" "crypto" \
"Banktechnische Signatur A005/A006 (sign + verify)" \
"- [ ] A005 (RSASSA-PKCS1-v1_5) signieren/verifizieren
- [ ] A006 (RSASSA-PSS) signieren/verifizieren
- [ ] Order-Hash-Bildung gemaess Spec
- [ ] Testvektoren"

mk "M2 - Cryptography & Certificates" "crypto" \
"Authentifikationssignatur X002" \
"- [ ] Signatur ueber den Request gemaess EBICS
- [ ] Verifikation im Server
- [ ] Tests"

mk "M2 - Cryptography & Certificates" "crypto" \
"Verschluesselung E002 (RSA + AES)" \
"- [ ] AES-128-CBC Transportverschluesselung des OrderData
- [ ] RSA-Verschluesselung des Transaktionsschluessels
- [ ] Padding/IV-Handling gemaess Spec
- [ ] Ent-/Verschluesselung Round-Trip-Test"

mk "M2 - Cryptography & Certificates" "crypto" \
"Hashing & Public-Key-Fingerprints (HPB/INI/HIA)" \
"- [ ] SHA-256 Hashwerte der oeffentlichen Schluessel
- [ ] Darstellung fuer INI-Brief / HPB-Antwort
- [ ] Verifikation der vom Client gesendeten Hashes"

mk "M2 - Cryptography & Certificates" "crypto" \
"Zertifikatsverifizierung (X.509)" \
"- [ ] Kette/Vertrauensanker pruefen (konfigurierbar, Test-CA)
- [ ] Gueltigkeit/Verwendungszweck
- [ ] optional: Verfahren ohne Zertifikate (reine Schluessel) unterstuetzen"

# ===========================================================================
# M3 - Server: Key Management
# ===========================================================================
mk "M3 - Server: Key Management" "epic,server" \
"EPIC: Server - Key Management & Onboarding" \
"Der Emulator bildet den Subscriber-Onboarding-Flow ab: INI, HIA, HPB sowie HSA/SPR/HCA/HCS je nach Version.

**Akzeptanz:** Ein echter EBICS-Client kann sich initialisieren (INI+HIA), HPB abrufen und in den Status 'Ready' gelangen." "no-dod"

mk "M3 - Server: Key Management" "server" \
"Hostable Server-Grundgeruest (ASP.NET Core)" \
"- [ ] HTTP-Endpoint(e) fuer EBICS (POST, text/xml)
- [ ] Request-Pipeline (Parse -> Version-Dispatch -> Verify -> Handle -> Respond)
- [ ] zentrale Fehlerabbildung auf EBICS-Returncodes (z.B. 011000, 091xxx)
- [ ] In-Memory-Zustandsspeicher (pluggable)"

mk "M3 - Server: Key Management" "server,orders" \
"INI - Senden der Signaturschluessel (A00x)" \
"- [ ] OrderType INI verarbeiten
- [ ] Signaturschluessel speichern, Status setzen
- [ ] Returncodes & Fehlerfaelle (bereits initialisiert ...)"

mk "M3 - Server: Key Management" "server,orders" \
"HIA - Senden Auth- & Enc-Schluessel (X002/E002)" \
"- [ ] OrderType HIA verarbeiten
- [ ] X002/E002 speichern
- [ ] Statusuebergang"

mk "M3 - Server: Key Management" "server,orders" \
"HPB - Abruf der Bankschluessel" \
"- [ ] Bank-Public-Keys (E002/X002) zurueckgeben
- [ ] Verschluesselte/HPB-konforme Antwort
- [ ] Hashes zum Abgleich"

mk "M3 - Server: Key Management" "server,orders" \
"HSA / SPR / HCA / HCS - Schluesselwechsel & Sperrung" \
"- [ ] HCA (Auth/Enc aendern), HCS (alle aendern)
- [ ] SPR (Suspendierung / Sperrung)
- [ ] HSA (falls Version relevant)
- [ ] Statusmaschine konsistent halten"

mk "M3 - Server: Key Management" "server" \
"Subscriber-/Partner-/Bank-Verwaltung (Stammdaten)" \
"- [ ] CRUD im Server-Zustand
- [ ] Berechtigungen pro OrderType/BTF
- [ ] Mehr-Banken-/Mehr-Mandanten-Faehigkeit"

# ===========================================================================
# M4 - Server: Transaction Engine
# ===========================================================================
mk "M4 - Server: Transaction Engine" "epic,server,protocol" \
"EPIC: Server - Transaction Engine" \
"Generische Transaktionsmaschine fuer Upload (Initialisation->Transfer) und Download (Initialisation->Transfer->Receipt), inkl. Segmentierung, Kompression, Recovery." "no-dod"

mk "M4 - Server: Transaction Engine" "server,protocol" \
"Upload-Transaktion (Initialisation + Transfer)" \
"- [ ] TransactionID-Vergabe
- [ ] OrderData: Kompression (zip) + Verschluesselung + Base64
- [ ] Segmentierung & Reassemblierung
- [ ] Signaturpruefung des OrderData"

mk "M4 - Server: Transaction Engine" "server,protocol" \
"Download-Transaktion (Initialisation + Transfer + Receipt)" \
"- [ ] Datenbereitstellung serverseitig
- [ ] Segmentierte Auslieferung
- [ ] Receipt-Verarbeitung (positiv/negativ)
- [ ] Quittungs-Returncodes"

mk "M4 - Server: Transaction Engine" "server,protocol" \
"Segmentierung, Kompression & Base64-Pipeline" \
"- [ ] konfigurierbare Segmentgroesse
- [ ] deterministische Reassemblierung
- [ ] Grenzfaelle (1 Segment, leeres OrderData)"

mk "M4 - Server: Transaction Engine" "server,protocol" \
"Transaktions-Recovery & Timeouts" \
"- [ ] Wiederaufnahme unterbrochener Transaktionen
- [ ] Ablauf/Timeout von TransactionIDs
- [ ] Idempotenz / doppelte Segmente"

mk "M4 - Server: Transaction Engine" "server,protocol" \
"EBICS-Returncode-Katalog" \
"- [ ] technische + fachliche Returncodes als Konstanten
- [ ] Mapping Exception -> Returncode
- [ ] Tests fuer haeufige Fehlerpfade"

# ===========================================================================
# M5 - Server: Orders & BTF
# ===========================================================================
mk "M5 - Server: Orders & BTF" "epic,server,orders" \
"EPIC: Orders & Business Transaction Formats" \
"Moeglichst vollstaendige Abdeckung der Order-Typen (H003/H004) bzw. BTF (H005).

> Die vollstaendige BTF-Tabelle aus EBICS 3.0 / Annex gegen offizielle Quelle verifizieren. Untenstehende Liste ist eine Arbeitsgrundlage.
>
> BTF-Mapping / External Code List: ${SRC_BTF} | Spezifikation: ${SRC_SPEC}" "no-dod"

mk "M5 - Server: Orders & BTF" "server,orders,H005" \
"BTF-Framework (H005)" \
"- [ ] BTF-Parameter (Service, Option, Scope, Container, MsgName, Version, Format)
- [ ] Mapping BTF <-> klassische OrderTypes (H004)
- [ ] Berechtigungspruefung pro BTF

---
**Quelle:** BTF-Mapping / External Code List (versionsunabhaengig, zuletzt 23.10.2024): ${SRC_BTF}"

mk "M5 - Server: Orders & BTF" "server,orders" \
"Upload-Orders: Zahlungsverkehr (CCT/CDD/CDB/CIP/...)" \
"- [ ] CCT (SEPA Credit Transfer / pain.001)
- [ ] CDD (SEPA Direct Debit / pain.008)
- [ ] CDB (B2B Lastschrift)
- [ ] Validierung der pain-Payloads (Schema)
- [ ] Ablage zur spaeteren Auslieferung (Statusreports)"

mk "M5 - Server: Orders & BTF" "server,orders" \
"Download-Orders: Kontoauszuege & Reports (STA/C53/C52/C54/Z53...)" \
"- [ ] STA (MT940), VMK (MT942)
- [ ] C53 (camt.053), C52 (camt.052), C54 (camt.054)
- [ ] generierbare Testdaten serverseitig
- [ ] Filter nach Zeitraum"

mk "M5 - Server: Orders & BTF" "server,orders" \
"Status- & Protokoll-Orders (HAC/HAA/HTD/HKD/HPD/PTK)" \
"- [ ] HAC (Customer Protocol, camt.086 / pain.002 je nach Version)
- [ ] HTD (Teilnehmerdaten), HKD (Kundendaten)
- [ ] HAA (verfuegbare OrderTypes/BTF)
- [ ] HPD (Bankparameter), PTK (Protokoll)"

mk "M5 - Server: Orders & BTF" "server,orders" \
"Verteilte elektronische Unterschrift (HVE/HVD/HVU/HVZ/HVS/HVT)" \
"- [ ] VEU-Datenstrukturen
- [ ] HVU/HVZ (Uebersicht offener VEU)
- [ ] HVE/HVS (Zeichnung), HVD/HVT (Detail)
- [ ] Mehr-Unterschriften-Workflow im Server-Zustand"

mk "M5 - Server: Orders & BTF" "server,orders,docs" \
"Order-/BTF-Abdeckungsmatrix pflegen" \
"- [ ] Tabelle: OrderType/BTF x Version x Status (geplant/erledigt)
- [ ] in README/Docs verlinken
- [ ] offene Luecken markieren

---
**Quellen:** BTF-Mapping ${SRC_BTF} | Spezifikation (Order-/BTF-Definitionen) ${SRC_SPEC}"

# ===========================================================================
# M6 - Connector (NuGet)
# ===========================================================================
mk "M6 - Connector (NuGet)" "epic,connector" \
"EPIC: EBICO.Connector (NuGet Client)" \
"Client-Bibliothek fuer den Zugriff auf einen EBICS-Server (Emulator oder echt). Fluent, testbar, DI-freundlich.

## Architektur (Mediator-Muster)

Der Aufrufer kennt nur \`IEbicsClient.Send(request)\`. Die gesamte EBICS-Komplexitaet (Transaktions-Skelett, Krypto, Serialisierung, Transport) liegt darunter und ist unsichtbar.

\`\`\`
var result = await client.Send(new CddUploadRequest { Pain008 = bytes });
\`\`\`

**Warum Mediator:** EBICS-Auftraege unterscheiden sich wenig - fast alles ist ein Upload (Initialisation -> Transfer) oder ein Download (Initialisation -> Transfer -> Receipt). Ein generischer Handler pro Richtung deckt den Grossteil ab; spezielle Auftraege (HPB, INI/HIA) bekommen eigene Handler.

**Schichten (aussen -> innen):**
1. Aufrufende App - bringt DI, eigenen HttpClient, Key-Store mit
2. \`IEbicsClient\` (Mediator) - \`Send<TResult>(IEbicsRequest<TResult>)\`, waehlt Handler
3. Upload-/Download-Handler - CCT/CDD bzw. STA/C53/HPB ...
4. Transaktionsmaschine - Init/Transfer/Receipt, Segmentierung
5. Krypto + Serialisierung (A00x, E002, X002, XSD) | Transport (\`ITransport\` ueber HttpClient)
6. EBICS-Server

**Send-Pipeline (Upload-Beispiel):**
1. Validierung (Berechtigung, BTF)
2. Payload -> XML serialisieren
3. Komprimieren + E002 verschluesseln + A00x signieren
4. X002 Authentifikationssignatur
5. \`HttpClient.Send\`
6. HTTP-Antwort
7. Verify + entschluesseln
8. Returncode pruefen
9. ggf. weitere Segmente (Download-Schleife)
10. Deserialisieren -> \`TResult\`

Jede Stufe ist eine eigene, unit-testbare Komponente.

## Kern-Abstraktionen

\`\`\`csharp
// Request 'weiss', was er zurueckgibt (Marker + Ergebnistyp-Bindung)
public interface IEbicsRequest<TResult> { }

// Der Mediator - das Einzige, was die App kennt
public interface IEbicsClient {
    Task<EbicsResult<TResult>> Send<TResult>(
        IEbicsRequest<TResult> request, CancellationToken ct = default);
}

// Request = nur Daten, keine Logik
public sealed class CddUploadRequest : IEbicsRequest<UploadReceipt> {
    public required ReadOnlyMemory<byte> Pain008 { get; init; }
}

// Ein Handler pro Request, vom Client nachgeschlagen
public interface IEbicsRequestHandler<TRequest, TResult>
    where TRequest : IEbicsRequest<TResult> {
    Task<EbicsResult<TResult>> Handle(TRequest request, EbicsContext ctx, CancellationToken ct);
}
\`\`\`

## Designentscheidungen (mit Trade-offs)

- **Eigener Dispatch statt MediatR-Library.** Pipeline-Reihenfolge (Krypto vor Transport, Segment-Schleife) und Versionsabhaengigkeit (H003/H004/H005) sind EBICS-spezifisch -> volle Kontrolle, keine Fremd-Dependency im NuGet-Paket (schlanke Abhaengigkeitsliste als Verkaufsargument). MediatR waere moeglich, spart Boilerplate, bringt aber Kopplung.
- **\`EbicsResult<T>\` statt Exceptions fuer fachliche Returncodes.** EBICS liefert viele fachliche Codes (z.B. 'noch keine Daten vorhanden'), die kein Programmfehler sind. Result-Typ ist sauberer; echte Transport-/Krypto-Fehler duerfen weiter werfen.
- **HttpClient hinter schmalem \`ITransport\`.** Nicht direkt durchreichen, sondern intern nutzen -> saubere \`IHttpClientFactory\`/\`AddHttpClient\`-Integration (Polly-Resilienz, Named Clients, Logging-Handler geschenkt), ohne dass EBICS-Logik vom konkreten HttpClient abhaengt.
- **Key-Store als Abstraktion (\`IKeyStore\`).** Nicht fest auf Dateien: In-Memory im Test, Datei/HSM/eigener Store in Produktion -> Krypto-Schicht bleibt testbar.

**DI-Registrierung (Zielbild):**
\`\`\`csharp
services.AddEbicoConnector(o => { o.HostId = \"...\"; o.PartnerId = \"...\"; })
        .AddHttpClient();   // eigener HttpClient, eigene Resilienz-Policy
\`\`\`

> Vollstaendige, gepflegte Fassung dieser Architektur: \`docs/connector/architecture.md\`

**Akzeptanz:** Mit dem Connector lassen sich INI/HIA/HPB sowie ein Upload (CCT) und ein Download (C53) gegen \`EBICO.Server\` durchfuehren." "no-dod"

mk "M6 - Connector (NuGet)" "connector,docs" \
"Architektur-Dokumentation EBICO.Connector" \
"Die Connector-Architektur (Mediator-Muster, Send-Pipeline, Kern-Abstraktionen, Designentscheidungen) ausfuehrlich und gepflegt dokumentieren.

- [ ] \`docs/connector/architecture.md\` mit Schichtenmodell, Send-Pipeline und Interface-Skizzen
- [ ] Diagramme (Schichten + Pipeline) als Mermaid/SVG einbetten
- [ ] Designentscheidungen inkl. Trade-offs (Dispatch vs. MediatR, Result vs. Exceptions, ITransport, IKeyStore)
- [ ] Code-Beispiele: \`Send(...)\`-Aufruf, Request-Definition, Handler-Signatur, DI-Registrierung
- [ ] Verweise aus Epic und README auf die Doku-Seite
- [ ] bei Architekturaenderungen: Doku + ggf. ADR aktualisieren

> Inhaltliche Grundlage steht bereits im Epic-Body und in \`docs/connector/architecture.md\`."

mk "M6 - Connector (NuGet)" "connector" \
"Client-Kern & Konfiguration" \
"- [ ] Verbindungsparameter (URL, HostID, PartnerID, UserID, Version)
- [ ] Schlusselspeicher-Abstraktion \`IKeyStore\` (Datei/Memory/eigener Store)
- [ ] Transport-Abstraktion \`ITransport\` (kapselt injizierten HttpClient)
- [ ] \`IEbicsClient.Send\`-Dispatch + Handler-Registry (eigener Dispatch, kein MediatR)
- [ ] \`EbicsResult<T>\` fuer fachliche Returncodes (Exceptions nur fuer Transport/Krypto)
- [ ] HttpClient-Integration (IHttpClientFactory, Resilienz, Timeouts)
- [ ] DI-Erweiterung (\`AddEbicoConnector(...)\`)

> Architektur-Referenz: Epic-Body bzw. \`docs/connector/architecture.md\`."

mk "M6 - Connector (NuGet)" "connector,crypto" \
"Onboarding-Flows: INI / HIA / HPB" \
"- [ ] Schlusselgenerierung clientseitig
- [ ] INI/HIA senden
- [ ] HPB abrufen + Bankschluessel verifizieren (Hash-Abgleich)
- [ ] INI-Brief generieren (PDF/Text)"

mk "M6 - Connector (NuGet)" "connector" \
"Upload-API (CCT/CDD ...)" \
"- [ ] generische Upload-Methode (BTF/OrderType + Payload)
- [ ] Komprimierung/Verschluesselung/Signatur clientseitig
- [ ] Convenience-Methoden fuer CCT/CDD"

mk "M6 - Connector (NuGet)" "connector" \
"Download-API (STA/C53 ...)" \
"- [ ] generische Download-Methode
- [ ] Receipt-Handling
- [ ] Convenience-Methoden + Parsing-Hooks"

mk "M6 - Connector (NuGet)" "connector,docs" \
"NuGet-Packaging & Beispiele" \
"- [ ] Paketmetadaten, Symbols, README
- [ ] minimaler Quickstart-Sample (Konsolenapp)
- [ ] SemVer-Strategie"

# ===========================================================================
# M7 - Suite (Blazor UI)
# ===========================================================================
mk "M7 - Suite (Blazor UI)" "epic,suite" \
"EPIC: EBICO.Suite (Blazor UI)" \
"Admin-/Inspektor-UI fuer den Emulator: Stammdaten verwalten, Transaktionen beobachten, Payloads inspizieren.

Stack: Blazor (passend zum .NET-Stack)." "no-dod"

mk "M7 - Suite (Blazor UI)" "suite" \
"UI-Grundgeruest & Navigation" \
"- [ ] Blazor-Projekt (Auto/Server - in ADR festlegen)
- [ ] Layout, Navigation, Theming
- [ ] Anbindung an Server-Zustand (geteilter Store oder API)"

mk "M7 - Suite (Blazor UI)" "suite" \
"Stammdaten-Verwaltung (Banks/Partner/User)" \
"- [ ] Listen + Detailansichten
- [ ] Anlage/Bearbeitung
- [ ] Subscriber-Status sichtbar/aenderbar"

mk "M7 - Suite (Blazor UI)" "suite" \
"Transaktions-Inspektor" \
"- [ ] Liste laufender/abgeschlossener Transaktionen
- [ ] Roh-XML + entschluesselter OrderData-View
- [ ] Returncodes/Fehler sichtbar"

mk "M7 - Suite (Blazor UI)" "suite,crypto" \
"Schluessel-/Zertifikats-Ansicht" \
"- [ ] Public-Key-Fingerprints anzeigen
- [ ] INI-Brief-Vergleich
- [ ] Test-CA/Schluessel-Tools"

# ===========================================================================
# M8 - Validation & Conformance
# ===========================================================================
mk "M8 - Validation & Conformance" "epic,infra" \
"EPIC: Validation & Conformance" \
"End-to-End-Verifikation, Konformitaet gegen reale Clients/Banken-Sandboxen und systematische Negativtests." "no-dod"

mk "M8 - Validation & Conformance" "infra" \
"E2E: Connector <-> Server Happy Paths" \
"- [ ] INI/HIA/HPB
- [ ] Upload CCT
- [ ] Download C53
- [ ] je H003/H004/H005"

mk "M8 - Validation & Conformance" "infra" \
"Negativ-/Sicherheitsfaelle" \
"- [ ] falsche Signatur, abgelaufene Keys, falscher Hash
- [ ] manipuliertes OrderData
- [ ] doppelte/inkonsistente Segmente
- [ ] erwartete Returncodes pruefen"

mk "M8 - Validation & Conformance" "infra,docs" \
"Konformitaet gegen reale Clients" \
"- [ ] Test mit mind. einem echten EBICS-Client (z.B. OSS-Client)
- [ ] Abweichungen dokumentieren
- [ ] Kompatibilitaetsmatrix"

# ===========================================================================
# M9 - Packaging & Docs
# ===========================================================================
mk "M9 - Packaging & Docs" "epic,docs,infra" \
"EPIC: Packaging & Documentation" \
"Veroeffentlichung und Doku: NuGet, Container-Image fuer den Server, Quickstart und Beispiele." "no-dod"

mk "M9 - Packaging & Docs" "infra" \
"Container-Image fuer EBICO.Server" \
"- [ ] Dockerfile
- [ ] Konfiguration via ENV
- [ ] Beispiel docker-compose (Server + Suite)"

mk "M9 - Packaging & Docs" "infra,connector" \
"NuGet-Publish-Pipeline" \
"- [ ] Pack + Push in CI (nuget.org / GitHub Packages)
- [ ] Versionierung/Tags
- [ ] Release-Notes-Automatisierung"

mk "M9 - Packaging & Docs" "docs" \
"Quickstart & Beispiele" \
"- [ ] 'In 5 Minuten zum laufenden Emulator'
- [ ] Connector-Sample-Repo/Ordner
- [ ] Mehr-Versionen-Hinweise
- [ ] Hinweis: offizielle Schemas/Lizenz von ebics.org beachten"

echo ">> Fertig. Milestones + Issues angelegt."

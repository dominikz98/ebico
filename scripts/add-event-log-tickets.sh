#!/usr/bin/env bash
###############################################################################
# add-event-log-tickets.sh
#
# ADDITIVES Skript - ergaenzt den gemeinsamen Ereignis-/Protokollspeicher
# (IEventLog) im bereits bestehenden EBICO-Projekt.
#
# Das urspruengliche create-ebico-plan.sh ist bereits gelaufen. Dieses Skript
# legt daher NICHTS doppelt an:
#   1. NEU:        Issue "Ereignis-/Protokollspeicher (IEventLog)" in M4
#                  (nur wenn nicht schon vorhanden)
#   2. EDIT:       bestehendes HAC-Issue -> Hinweis, dass HAC eine Projektion
#                  ueber IEventLog ist (gh issue edit, kein neues Issue)
#   3. EDIT:       bestehendes "Transaktions-Inspektor"-Issue -> zusaetzlich
#                  globale Protokollansicht ueber IEventLog
#   4. LABEL:      'observability' nur anlegen, wenn es fehlt
#
# IDEMPOTENZ: Das Skript ist mehrfach ausfuehrbar. Es sucht bestehende Issues
# per exaktem Titel und warnt bei 0 oder >1 Treffern, statt blind zu handeln.
#
# Voraussetzungen:
#   - gh installiert und authentifiziert (gh auth login)
#   - im geklonten ebico-Repo ausfuehren ODER REPO="User/ebico" setzen
###############################################################################
set -euo pipefail

REPO="${REPO:-}"
GH_REPO_FLAG=()
[[ -n "$REPO" ]] && GH_REPO_FLAG=(--repo "$REPO")

FULL_REPO="$(if [[ -n "$REPO" ]]; then echo "$REPO"; else gh repo view --json nameWithOwner -q .nameWithOwner; fi)"
echo ">> Repo: $FULL_REPO"

# Exakte Titel der bestehenden Issues (muessen mit create-ebico-plan.sh uebereinstimmen)
HAC_TITLE="Status- & Protokoll-Orders (HAC/HAA/HTD/HKD/HPD/PTK)"
INSPECTOR_TITLE="Transaktions-Inspektor"
NEW_TITLE="Ereignis-/Protokollspeicher (IEventLog)"
MILESTONE="M4 - Server: Transaction Engine"

# ---------------------------------------------------------------------------
# Hilfsfunktion: Issue-Nummer per exaktem Titel finden.
# Gibt die Nummer aus, oder leer. Warnt bei Mehrdeutigkeit.
# Sucht in offenen UND geschlossenen Issues.
# ---------------------------------------------------------------------------
find_issue_number () {
  local title="$1"
  local json matches
  # Issues als JSON holen, exakten Titelvergleich in Python (robuster als
  # gh-internes jq mit --arg, das nicht in allen gh-Versionen verfuegbar ist).
  json="$(gh issue list "${GH_REPO_FLAG[@]}" --state all --limit 300 \
      --json number,title 2>/dev/null || echo '[]')"
  matches="$(printf '%s' "$json" | python3 -c '
import sys, json
title = sys.argv[1]
try:
    data = json.load(sys.stdin)
except Exception:
    data = []
for it in data:
    if it.get("title") == title:
        print(it.get("number"))
' "$title")"
  local count
  count="$(printf '%s\n' "$matches" | grep -c . || true)"
  if [[ "$count" -gt 1 ]]; then
    echo "WARN: mehrere Issues mit Titel '$title' gefunden (#$(printf '%s' "$matches" | tr '\n' ' ')). Bitte manuell pruefen." >&2
    echo ""   # leer zurueck -> kein automatisches Edit
    return
  fi
  printf '%s' "$matches" | head -n1 | tr -d '[:space:]'
}

# ---------------------------------------------------------------------------
# 0) Label 'observability' anlegen, falls fehlt
# ---------------------------------------------------------------------------
echo ">> Pruefe Label 'observability' ..."
if gh label list "${GH_REPO_FLAG[@]}" --limit 200 | grep -qiE "^observability\s"; then
  echo "   existiert bereits - skip"
else
  gh label create "observability" "${GH_REPO_FLAG[@]}" --color "1d76db" \
      --description "Logging / Ereignisse / Audit / Protokoll" || true
  echo "   angelegt"
fi

# ---------------------------------------------------------------------------
# 1) NEUES Issue: IEventLog (nur wenn nicht vorhanden)
# ---------------------------------------------------------------------------
echo ">> Pruefe, ob '$NEW_TITLE' bereits existiert ..."
existing_new="$(find_issue_number "$NEW_TITLE")"
if [[ -n "$existing_new" ]]; then
  echo "   existiert bereits als #$existing_new - kein erneutes Anlegen."
else
  echo "   nicht vorhanden -> wird angelegt."
  NEW_BODY="Gemeinsamer, append-only **Ereignis-/Protokollspeicher**, in den alle Server-Komponenten relevante Ereignisse schreiben (Auftrag eingegangen, Signatur geprueft, Returncode vergeben, Transaktion abgeschlossen, Key-Mgmt-Aktion ...).

## Warum eigenes Ticket
HAC (Customer Protocol) und der Suite-Inspektor lesen beide aus DERSELBEN Quelle, erzeugen sie aber nicht. Ohne diese Komponente hat HAC nichts zurueckzugeben und die Suite nichts anzuzeigen. Diese Grundlage gehoert vor M5 (HAC) und M7 (Inspektor).

## Konzept
- **Ein** append-only Ereignismodell mit genug Struktur fuer beide Sichten:
  Kunde (Partner/User), Auftrag/Transaktion, Ereignistyp, Zeitstempel,
  Ergebnis/Returncode, **Sichtbarkeit** (z.B. kundensichtbar vs. nur intern).
- Zwei Projektionen darueber (siehe HAC- und Inspektor-Issue):
  - HAC = gefiltert je Kunde + spec-konformes Mapping
  - Suite = roh/global, auch interne Details

## Aufgaben
- [ ] \`IEventLog\`-Abstraktion (Append + Query), pluggable wie der uebrige State-Store
- [ ] Ereignismodell definieren (Felder s.o., inkl. Sichtbarkeits-/Severity-Kennzeichnung)
- [ ] Schreibpunkte in Key-Mgmt, Transaktionsmaschine, Signatur/VEU verdrahten
- [ ] Query-API: Filter nach Kunde, Zeitraum, Typ
- [ ] Persistenz an Server-Store anbinden (SQLite o.ae.; In-Memory fuer Tests)
- [ ] Abgrenzung dokumentieren: was ist kundensichtbar (HAC) vs. nur Betreiber (Suite)

## Abhaengigkeiten
- Nutzt denselben Persistenz-Ansatz wie der uebrige Server-Zustand (ADR Persistenz).
- Wird konsumiert von: HAC-Order (M5), Suite-Protokollansicht (M7).

---
**Definition of Done (projektweit verpflichtend)**
- [ ] **Doku:** \`docs/server/event-log.md\` (Modell, zwei Projektionen, Beispiel-Ereignisse) + im Doku-Index verlinkt
- [ ] **Tests:** Unit-Tests (Append/Query, Filter, Sichtbarkeitsregeln); Happy Path + Grenzfaelle
- [ ] **CI gruen:** dotnet build + dotnet test, keine neuen Warnungen
- [ ] **XML-Doc-Kommentare** an oeffentlichen APIs
- [ ] Code-Review durchgefuehrt"

  gh issue create "${GH_REPO_FLAG[@]}" \
    --title "$NEW_TITLE" \
    --body "$NEW_BODY" \
    --milestone "$MILESTONE" \
    --label "server" --label "protocol" --label "observability" \
    --label "tests" --label "documentation" >/dev/null
  echo "   Issue '$NEW_TITLE' angelegt (Milestone: $MILESTONE)."
fi

# ---------------------------------------------------------------------------
# 2) EDIT bestehendes HAC-Issue (append, nicht ersetzen)
# ---------------------------------------------------------------------------
echo ">> Suche HAC-Issue zum Ergaenzen ..."
hac_num="$(find_issue_number "$HAC_TITLE")"
if [[ -z "$hac_num" ]]; then
  echo "   HAC-Issue nicht eindeutig gefunden - uebersprungen (bitte manuell pruefen)." >&2
else
  # Bestehenden Body holen und nur ergaenzen, wenn der Hinweis noch nicht drin ist.
  cur_body="$(gh issue view "$hac_num" "${GH_REPO_FLAG[@]}" --json body --jq .body)"
  if printf '%s' "$cur_body" | grep -q "Projektion ueber"; then
    echo "   #$hac_num enthaelt den Hinweis bereits - skip."
  else
    add_block="

---
**Hinweis (ergaenzt): HAC ist eine Projektion ueber \`IEventLog\`**
HAC liefert das Customer Protocol und erzeugt KEIN eigenes Log. Es liest den gemeinsamen Ereignisspeicher (Issue '$NEW_TITLE'), **gefiltert je Kunde** und **spec-konform gemappt** (camt.086 / pain.002 je nach Version - gegen offizielle Spec verifizieren).
- [ ] HAC als Query/Projektion ueber \`IEventLog\` umsetzen (kein paralleles Logsystem)
- [ ] Kundenfilter + Sichtbarkeitsregeln anwenden (nur kundensichtbare Ereignisse)
- [ ] Mapping Ereignis -> HAC-Format gegen Spec verifizieren

Siehe \`docs/server/event-log.md\`."
    gh issue edit "$hac_num" "${GH_REPO_FLAG[@]}" --body "${cur_body}${add_block}" >/dev/null
    # Label observability ergaenzen (idempotent - doppelte werden ignoriert)
    gh issue edit "$hac_num" "${GH_REPO_FLAG[@]}" --add-label "observability" >/dev/null 2>&1 || true
    echo "   #$hac_num ergaenzt."
  fi
fi

# ---------------------------------------------------------------------------
# 3) EDIT bestehendes Inspektor-Issue (append, nicht ersetzen)
# ---------------------------------------------------------------------------
echo ">> Suche Inspektor-Issue zum Ergaenzen ..."
insp_num="$(find_issue_number "$INSPECTOR_TITLE")"
if [[ -z "$insp_num" ]]; then
  echo "   Inspektor-Issue nicht eindeutig gefunden - uebersprungen (bitte manuell pruefen)." >&2
else
  cur_body="$(gh issue view "$insp_num" "${GH_REPO_FLAG[@]}" --json body --jq .body)"
  if printf '%s' "$cur_body" | grep -q "Globale Protokollansicht"; then
    echo "   #$insp_num enthaelt den Zusatz bereits - skip."
  else
    add_block="

---
**Erweiterung (ergaenzt): Globale Protokollansicht ueber \`IEventLog\`**
Zusaetzlich zum Transaktions-Inspektor eine globale, durchsuchbare Protokollansicht ueber denselben Ereignisspeicher (Issue '$NEW_TITLE') - roh und ueber ALLE Kunden, fuer Betreiber/Entwickler (auch interne Details, die nicht ins HAC gehoeren).
- [ ] Globale Ereignisliste (alle Kunden), nicht spec-gefiltert
- [ ] Live-Filter: Kunde, Zeitraum, Ereignistyp, Severity
- [ ] Verweis/Sprung von einem Ereignis zur zugehoerigen Transaktion

Siehe \`docs/server/event-log.md\`."
    gh issue edit "$insp_num" "${GH_REPO_FLAG[@]}" --body "${cur_body}${add_block}" >/dev/null
    gh issue edit "$insp_num" "${GH_REPO_FLAG[@]}" --add-label "observability" >/dev/null 2>&1 || true
    echo "   #$insp_num ergaenzt."
  fi
fi

echo ">> Fertig."

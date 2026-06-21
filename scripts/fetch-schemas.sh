#!/usr/bin/env bash
###############################################################################
# fetch-schemas.sh
#
# Bereitet die EBICS-Schemas reproduzierbar auf.
#
# WARUM KEIN VOLLAUTOMATISCHER DOWNLOAD?
#   Die Schema-/Spec-Dateien auf ebics.org liegen hinter einem "I accept"-
#   Button und werden ueber signierte, ABLAUFENDE securedl-URLs ausgeliefert.
#   Es gibt keine stabilen Direktlinks. Der Download-Schritt ist daher manuell;
#   dieses Skript uebernimmt alles danach reproduzierbar:
#     entpacken -> nach schemas/<VERSION>/ einsortieren -> SHA-256 ->
#     -> Manifest schreiben -> optional gegen erwartete Dateiliste pruefen.
#
# LIZENZ: Schemas/Specs sind proprietaer (EBICS SC). Download + Reproduktion
#   mit Copyright-Vermerk erlaubt; Modifikation / derivative uses NICHT ohne
#   schriftliche Genehmigung. Siehe docs/legal/ebics-licensing.md (falls
#   vorhanden) bzw. docs/protocol/schema-sources.md.
#
# -----------------------------------------------------------------------------
# WORKFLOW
#   1) Schema-ZIP manuell laden:
#        H005 (EBICS 3.0): https://www.ebics.org/en/technical-information/ebics-schema
#        H004/H003 (Archiv): https://www.ebics.org/en/technical-information/archive-ebics/schema
#      (auf der Seite "I accept" bestaetigen, ZIP speichern)
#   2) Skript aufrufen, ZIP + Zielversion angeben:
#        ./scripts/fetch-schemas.sh --zip ~/Downloads/EBICS_3.0_schema.zip --version H005
#        ./scripts/fetch-schemas.sh --zip ~/Downloads/EBICS_2.5_schema.zip --version H004
#   3) Ergebnis landet unter schemas/<VERSION>/ ; Manifest unter
#      schemas/<VERSION>/MANIFEST.sha256 und schemas/manifest.json (aggregiert).
#
# Re-Lauf ist idempotent: Zielverzeichnis wird pro Version sauber neu befuellt.
###############################################################################
set -euo pipefail

# --- Konfiguration / Defaults ------------------------------------------------
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCHEMA_ROOT="${REPO_ROOT}/schemas"
ZIP=""
VERSION=""
STRICT="0"           # bei 1: fehlende erwartete Dateien fuehren zu Exit-Code 2
KEEP_TMP="0"

usage () {
  cat <<EOF
fetch-schemas.sh - EBICS-Schemas reproduzierbar aufbereiten

  --zip <pfad>        Pfad zum manuell heruntergeladenen Schema-ZIP (Pflicht)
  --version <id>      Zielversion: H005 | H004 | H003 (Pflicht)
  --strict            Fehlende erwartete Schemadateien => Fehler (Exit 2)
  --keep-tmp          Temporaeres Entpackverzeichnis nicht loeschen
  -h, --help          Diese Hilfe

Beispiele:
  ./scripts/fetch-schemas.sh --zip ~/Downloads/ebics_3.0.zip --version H005
  ./scripts/fetch-schemas.sh --zip ~/Downloads/ebics_2.5.zip --version H004 --strict
EOF
}

# --- Argumente ---------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --zip)      ZIP="${2:-}"; shift 2 ;;
    --version)  VERSION="${2:-}"; shift 2 ;;
    --strict)   STRICT="1"; shift ;;
    --keep-tmp) KEEP_TMP="1"; shift ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "Unbekanntes Argument: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -z "$ZIP" || -z "$VERSION" ]] && { echo "Fehler: --zip und --version sind Pflicht." >&2; usage; exit 1; }
[[ -f "$ZIP" ]] || { echo "Fehler: ZIP nicht gefunden: $ZIP" >&2; exit 1; }

case "$VERSION" in
  H005|H004|H003) ;;
  *) echo "Fehler: --version muss H005, H004 oder H003 sein (war: $VERSION)." >&2; exit 1 ;;
esac

# --- Tool-Checks -------------------------------------------------------------
need () { command -v "$1" >/dev/null 2>&1 || { echo "Fehlt: $1" >&2; exit 1; }; }
need unzip
SHACMD=""
if command -v sha256sum >/dev/null 2>&1; then SHACMD="sha256sum";
elif command -v shasum  >/dev/null 2>&1; then SHACMD="shasum -a 256";
else echo "Fehlt: sha256sum bzw. shasum" >&2; exit 1; fi

# --- Erwartete Dateien je Version (zur Plausibilitaetspruefung) --------------
# Quelle: ebics.org Schema-Seite. Liste ist Soll-Stand, nicht zwingend
# vollstaendig fuer alle Subversionen - dient als Warnhinweis.
expected_files () {
  case "$1" in
    H005)
      cat <<EOF
ebics_H005.xsd
ebics_request_H005.xsd
ebics_response_H005.xsd
ebics_orders_H005.xsd
ebics_types_H005.xsd
ebics_keymgmt_request_H005.xsd
ebics_keymgmt_response_H005.xsd
ebics_hev.xsd
ebics_signature.xsd
xmldsig-core-schema.xsd
EOF
      ;;
    H004)
      cat <<EOF
ebics_H004.xsd
ebics_request_H004.xsd
ebics_response_H004.xsd
ebics_orders_H004.xsd
ebics_types_H004.xsd
ebics_keymgmt_request_H004.xsd
ebics_keymgmt_response_H004.xsd
ebics_hev.xsd
ebics_signature.xsd
xmldsig-core-schema.xsd
EOF
      ;;
    H003)
      cat <<EOF
ebics_H003.xsd
ebics_request_H003.xsd
ebics_response_H003.xsd
ebics_orders_H003.xsd
ebics_types_H003.xsd
ebics_keymgmt_request_H003.xsd
ebics_keymgmt_response_H003.xsd
ebics_signature.xsd
xmldsig-core-schema.xsd
EOF
      ;;
  esac
}

# --- Entpacken ---------------------------------------------------------------
TMP="$(mktemp -d)"
cleanup () { [[ "$KEEP_TMP" == "1" ]] || rm -rf "$TMP"; }
trap cleanup EXIT

echo ">> Entpacke $ZIP ..."
unzip -o -q "$ZIP" -d "$TMP"

# Quell-ZIP-Hash festhalten (zur Nachvollziehbarkeit des Bezugs)
ZIP_HASH="$($SHACMD "$ZIP" | awk '{print $1}')"

# --- Zielverzeichnis vorbereiten --------------------------------------------
DEST="${SCHEMA_ROOT}/${VERSION}"
echo ">> Zielverzeichnis: $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"

# --- .xsd-Dateien flach einsortieren ----------------------------------------
# (EBICS-ZIPs enthalten je nach Version Unterordner - wir flachen auf Dateinamen ab.
#  Bei Namenskollision wird gewarnt und nicht ueberschrieben.)
echo ">> Sortiere .xsd-Dateien ein ..."
found_count=0
while IFS= read -r -d '' f; do
  base="$(basename "$f")"
  if [[ -e "$DEST/$base" ]]; then
    echo "   WARN: Namenskollision, uebersprungen: $base" >&2
    continue
  fi
  cp "$f" "$DEST/$base"
  found_count=$((found_count+1))
done < <(find "$TMP" -type f -iname '*.xsd' -print0)

echo "   $found_count .xsd-Datei(en) uebernommen."
[[ "$found_count" -eq 0 ]] && { echo "Fehler: keine .xsd im ZIP gefunden." >&2; exit 1; }

# --- Abgleich gegen erwartete Liste -----------------------------------------
echo ">> Pruefe gegen erwartete Dateiliste ($VERSION) ..."
missing=0
while IFS= read -r exp; do
  [[ -z "$exp" ]] && continue
  if [[ ! -e "$DEST/$exp" ]]; then
    echo "   fehlt (erwartet): $exp" >&2
    missing=$((missing+1))
  fi
done < <(expected_files "$VERSION")
if [[ "$missing" -gt 0 ]]; then
  echo "   $missing erwartete Datei(en) nicht gefunden."
  [[ "$STRICT" == "1" ]] && { echo "   --strict gesetzt -> Abbruch." >&2; exit 2; }
  echo "   (Hinweis: je nach Subversion/Instant-XSD kann das ok sein.)"
else
  echo "   alle erwarteten Dateien vorhanden."
fi

# --- Checksums je Version ----------------------------------------------------
echo ">> Schreibe SHA-256-Manifest ..."
( cd "$DEST" && $SHACMD *.xsd | sort -k2 > MANIFEST.sha256 )
echo "   $DEST/MANIFEST.sha256"

# --- Aggregiertes JSON-Manifest ueber alle Versionen -------------------------
# (manuell zusammengesetzt, ohne jq-Abhaengigkeit)
echo ">> Aktualisiere schemas/manifest.json ..."
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
MJSON="${SCHEMA_ROOT}/manifest.json"
TMPJSON="$(mktemp)"

# Bestehende sourceZipSha256 / ingestedAt pro Version aus altem Manifest lesen,
# damit ein Lauf fuer Version X die Metadaten von Version Y nicht verwirft.
# Ohne jq: kleines awk, das pro Versionsblock die beiden Felder herauszieht.
get_old_meta () {   # $1 = version, $2 = feldname  -> Wert oder leer
  [[ -f "$MJSON" ]] || return 0
  awk -v ver="$1" -v key="$2" '
    $0 ~ "\""ver"\"[[:space:]]*:[[:space:]]*\\{" { inblock=1 }
    inblock && $0 ~ "\""key"\"" {
      # Zeile der Form:  "key": "value",
      line=$0
      sub(/^[^:]*:[[:space:]]*"/, "", line)
      sub(/".*$/, "", line)
      print line
      inblock=0
    }
    inblock && /"files"[[:space:]]*:/ { inblock=0 }  # Feld nicht vorhanden
  ' "$MJSON"
}

{
  echo "{"
  echo "  \"generatedAt\": \"${NOW}\","
  echo "  \"note\": \"Reproduzierbar erzeugt von scripts/fetch-schemas.sh. Quelldateien sind proprietaer (EBICS SC) - siehe docs/protocol/schema-sources.md.\","
  echo "  \"versions\": {"
  first_v=1
  for vdir in "${SCHEMA_ROOT}"/H*/ ; do
    [[ -d "$vdir" ]] || continue
    vid="$(basename "$vdir")"
    man="${vdir}MANIFEST.sha256"
    [[ -f "$man" ]] || continue
    [[ $first_v -eq 0 ]] && echo ","
    first_v=0

    # Metadaten bestimmen: fuer die aktuell verarbeitete Version frisch,
    # fuer andere Versionen aus dem alten Manifest uebernehmen.
    if [[ "$vid" == "$VERSION" ]]; then
      v_ziphash="$ZIP_HASH"
      v_ingested="$NOW"
    else
      v_ziphash="$(get_old_meta "$vid" sourceZipSha256)"
      v_ingested="$(get_old_meta "$vid" ingestedAt)"
    fi

    printf '    "%s": {\n' "$vid"
    [[ -n "$v_ziphash"  ]] && printf '      "sourceZipSha256": "%s",\n' "$v_ziphash"
    [[ -n "$v_ingested" ]] && printf '      "ingestedAt": "%s",\n' "$v_ingested"
    printf '      "files": {\n'
    first_f=1
    while read -r h name; do
      [[ -z "$h" ]] && continue
      [[ $first_f -eq 0 ]] && printf ',\n'
      first_f=0
      printf '        "%s": "%s"' "$name" "$h"
    done < "$man"
    printf '\n      }\n'
    printf '    }'
  done
  echo ""
  echo "  }"
  echo "}"
} > "$TMPJSON"
mv "$TMPJSON" "$MJSON"
echo "   $MJSON"

# --- README-Stub im Versionsordner ------------------------------------------
cat > "$DEST/README.md" <<EOF
# EBICS Schemas - $VERSION

Reproduzierbar einsortiert von \`scripts/fetch-schemas.sh\` am ${NOW}.

- Quell-ZIP SHA-256: \`${ZIP_HASH}\`
- Datei-Checksums: siehe \`MANIFEST.sha256\`
- Quellen & Lizenz: siehe \`../../docs/protocol/schema-sources.md\`

> Diese Dateien sind proprietaeres Eigentum der EBICS SC. Nicht modifizieren.
> Pruefe vor dem Commit, ob diese Dateien ueberhaupt ins Repo duerfen
> (siehe Lizenz-Issue / docs/legal/ebics-licensing.md).
EOF

echo ">> Fertig. Schemas unter: $DEST"
echo "   Tipp: 'git status schemas/' pruefen und Lizenzfrage beachten, bevor du committest."

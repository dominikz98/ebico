#!/usr/bin/env bash
###############################################################################
# generate-bindings.sh
#
# Erzeugt die C#-XSD-Bindings (EBICO.Core/Schema) reproduzierbar aus den lokal
# bezogenen EBICS-Schemas.
#
# WARUM EIN SKRIPT?
#   Die Bindings werden committet (ADR-0006), aber die XSDs selbst bleiben
#   proprietaer und ungetrackt (ADR-0003). Dieses Skript ist der reproduzierbare
#   Weg, die committeten .cs nach einem Schema-Update neu zu generieren.
#   Es ist KEIN Build-Schritt: die CI kompiliert die committeten Bindings ohne
#   Schemas oder dieses Tool.
#
# VORAUSSETZUNGEN
#   1) Schemas lokal vorhanden:   ./scripts/fetch-schemas.sh ... (schemas/<V>/)
#   2) Tool wiederhergestellt:    dotnet tool restore  (.config/dotnet-tools.json
#                                 pinnt dotnet-xscgen / XmlSchemaClassGenerator)
#
# AUFRUF
#   ./scripts/generate-bindings.sh --all
#   ./scripts/generate-bindings.sh --version H005
#
# ERGEBNIS (idempotent; Zielordner werden pro Lauf sauber neu befuellt)
#   src/EBICO.Core/Schema/
#     H005/ H004/ H003/                 versionsspezifische Typen
#     Shared/XmlDsig/                   W3C xmldsig (einmal, geteilt)
#     Shared/Hev/                       HEV / H000 (einmal, geteilt)
#     Shared/Signature/S001/            EBICS-Signatur S001 (H003+H004)
#     Shared/Signature/S002/            EBICS-Signatur S002 (H005)
#
# LIZENZ: Schemas/Specs sind proprietaer (EBICS SC). Die generierten Bindings
#   sind abgeleitete Artefakte; siehe docs/legal/ebics-licensing.md und ADR-0006.
###############################################################################
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCHEMA_ROOT="${REPO_ROOT}/schemas"
OUT_ROOT="${REPO_ROOT}/src/EBICO.Core/Schema"
DO_ALL="0"
VERSION=""

usage () {
  cat <<EOF
generate-bindings.sh - EBICS-XSD-Bindings reproduzierbar generieren

  --all               Alle Versionen (H003, H004, H005) neu generieren
  --version <id>      Nur eine Version: H005 | H004 | H003
  -h, --help          Diese Hilfe

Voraussetzung: ./scripts/fetch-schemas.sh (Schemas lokal) + dotnet tool restore.
EOF
}

# --- Argumente ---------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --all)      DO_ALL="1"; shift ;;
    --version)  VERSION="${2:-}"; shift 2 ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "Unbekanntes Argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ "$DO_ALL" == "0" && -z "$VERSION" ]]; then
  echo "Fehler: --all oder --version <id> angeben." >&2
  usage; exit 1
fi

# --- gemeinsame xscgen-Optionen ---------------------------------------------
#   --nullable          : nullable Adapter-Properties fuer optionale Werttypen
#                         (statt *Specified; verhindert stillen Datenverlust)
#   --netCore --pcl     : portable, framework-unabhaengige Klassen
#   --separateFiles     : eine Datei pro Typ (kleine, reviewbare Diffs)
#   --namespaceHierarchy: ein Ordner je C#-Namespace -> trennt shared/versioniert
#   --commentLanguages en: <summary> aus den XSD-<annotation>s (englisch)
#   --commandArgs-      : keine Befehlszeile in den Header (sonst nicht-determin.)
XSCGEN_COMMON=(--nullable --netCore --pcl --separateFiles --namespaceHierarchy
               --commentLanguages en --commandArgs-)

DS_MAP="http://www.w3.org/2000/09/xmldsig#=EBICO.Core.Schema.XmlDsig"
HEV_MAP="http://www.ebics.org/H000=EBICO.Core.Schema.Hev"
S001_MAP="http://www.ebics.org/S001=EBICO.Core.Schema.Signature.S001"
S002_MAP="http://www.ebics.org/S002=EBICO.Core.Schema.Signature.S002"

# Generiert eine Version in ein Staging-Verzeichnis (Echo: Pfad zu .../Schema)
generate_to_staging () {
  local ver="$1" staging="$2"
  local -a maps
  case "$ver" in
    H005) maps=(-n "urn:org:ebics:H005=EBICO.Core.Schema.H005" -n "$S002_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    H004) maps=(-n "urn:org:ebics:H004=EBICO.Core.Schema.H004" -n "$S001_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    H003) maps=(-n "http://www.ebics.org/H003=EBICO.Core.Schema.H003" -n "$S001_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    *) echo "Unbekannte Version: $ver" >&2; return 1 ;;
  esac

  if [[ ! -d "${SCHEMA_ROOT}/${ver}" ]] || ! ls "${SCHEMA_ROOT}/${ver}"/*.xsd >/dev/null 2>&1; then
    echo "Fehler: keine XSDs unter ${SCHEMA_ROOT}/${ver}/ — zuerst fetch-schemas.sh ausfuehren." >&2
    return 2
  fi

  ( cd "$REPO_ROOT" && dotnet xscgen -o "$staging" "${maps[@]}" "${XSCGEN_COMMON[@]}" \
      "schemas/${ver}"/*.xsd >/dev/null )
}

# Kopiert einen Namespace-Ordner aus dem Staging an seinen Zielort
place () {
  local src="$1" dst="$2"
  rm -rf "$dst"
  mkdir -p "$dst"
  cp -r "$src/." "$dst/"
}

declare -A STAGES=()
cleanup () { for d in "${STAGES[@]:-}"; do [[ -n "$d" ]] && rm -rf "$d"; done; }
trap cleanup EXIT

TARGETS=()
if [[ "$DO_ALL" == "1" ]]; then TARGETS=(H003 H004 H005); else TARGETS=("$VERSION"); fi

# 1) Alle Zielversionen in eigene Staging-Verzeichnisse generieren
for ver in "${TARGETS[@]}"; do
  st="$(mktemp -d)"
  STAGES["$ver"]="$st"
  echo ">> generiere $ver ..."
  generate_to_staging "$ver" "$st"
done

# 2) Versionsspezifische Typen platzieren
for ver in "${TARGETS[@]}"; do
  place "${STAGES[$ver]}/EBICO/Core/Schema/${ver}" "${OUT_ROOT}/${ver}"
done

# 3) Geteilte Namespaces einmal platzieren (deterministische Quelle je Namespace)
#    XmlDsig + Hev aus H005 (bzw. der ersten verfuegbaren Version);
#    S002 aus H005, S001 aus H004/H003.
pick_stage () { # erste verfuegbare Version aus der Argumentliste
  for v in "$@"; do [[ -n "${STAGES[$v]:-}" ]] && { echo "${STAGES[$v]}"; return; }; done
}
DS_SRC="$(pick_stage H005 H004 H003)"
HEV_SRC="$(pick_stage H005 H004 H003)"
[[ -n "$DS_SRC"  ]] && place "${DS_SRC}/EBICO/Core/Schema/XmlDsig" "${OUT_ROOT}/Shared/XmlDsig"
[[ -n "$HEV_SRC" ]] && place "${HEV_SRC}/EBICO/Core/Schema/Hev"    "${OUT_ROOT}/Shared/Hev"

S002_SRC="$(pick_stage H005)"
[[ -n "$S002_SRC" ]] && place "${S002_SRC}/EBICO/Core/Schema/Signature/S002" "${OUT_ROOT}/Shared/Signature/S002"
S001_SRC="$(pick_stage H004 H003)"
[[ -n "$S001_SRC" ]] && place "${S001_SRC}/EBICO/Core/Schema/Signature/S001" "${OUT_ROOT}/Shared/Signature/S001"

echo ">> fertig. Bindings unter ${OUT_ROOT#${REPO_ROOT}/}"

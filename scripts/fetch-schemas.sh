#!/usr/bin/env bash
###############################################################################
# fetch-schemas.sh
#
# Prepares the EBICS schemas reproducibly.
#
# WHY IS THERE NO FULLY AUTOMATED DOWNLOAD?
#   The schema/spec files on ebics.org sit behind an "I accept"
#   button and are served via signed, EXPIRING securedl URLs.
#   There are no stable direct links. The download step is therefore manual;
#   this script handles everything after it reproducibly:
#     unzip -> sort into schemas/<VERSION>/ -> SHA-256 ->
#     -> write manifest -> optionally check against the expected file list.
#
# LICENSE: schemas/specs are proprietary (EBICS SC). Download + reproduction
#   with a copyright notice is allowed; modification / derivative uses are NOT
#   without written permission. See docs/legal/ebics-licensing.md (if
#   present) or docs/protocol/schema-sources.md.
#
# -----------------------------------------------------------------------------
# WORKFLOW
#   1) Download the schema ZIP manually:
#        H005 (EBICS 3.0): https://www.ebics.org/en/technical-information/ebics-schema
#        H004/H003 (archive): https://www.ebics.org/en/technical-information/archive-ebics/schema
#      (confirm "I accept" on the page, save the ZIP)
#   2) Call the script, passing the ZIP + target version:
#        ./scripts/fetch-schemas.sh --zip ~/Downloads/EBICS_3.0_schema.zip --version H005
#        ./scripts/fetch-schemas.sh --zip ~/Downloads/EBICS_2.5_schema.zip --version H004
#   3) The result lands under schemas/<VERSION>/ ; the manifest under
#      schemas/<VERSION>/MANIFEST.sha256 and schemas/manifest.json (aggregated).
#
# Re-running is idempotent: the target directory is cleanly refilled per version.
###############################################################################
set -euo pipefail

# --- Configuration / defaults ------------------------------------------------
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCHEMA_ROOT="${REPO_ROOT}/schemas"
ZIP=""
VERSION=""
STRICT="0"           # when 1: missing expected files lead to exit code 2
KEEP_TMP="0"

usage () {
  cat <<EOF
fetch-schemas.sh - prepare the EBICS schemas reproducibly

  --zip <path>        Path to the manually downloaded schema ZIP (required)
  --version <id>      Target version: H005 | H004 | H003 (required)
  --strict            Missing expected schema files => error (exit 2)
  --keep-tmp          Do not delete the temporary unpack directory
  -h, --help          This help

Examples:
  ./scripts/fetch-schemas.sh --zip ~/Downloads/ebics_3.0.zip --version H005
  ./scripts/fetch-schemas.sh --zip ~/Downloads/ebics_2.5.zip --version H004 --strict
EOF
}

# --- Arguments ---------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --zip)      ZIP="${2:-}"; shift 2 ;;
    --version)  VERSION="${2:-}"; shift 2 ;;
    --strict)   STRICT="1"; shift ;;
    --keep-tmp) KEEP_TMP="1"; shift ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

[[ -z "$ZIP" || -z "$VERSION" ]] && { echo "Error: --zip and --version are required." >&2; usage; exit 1; }
[[ -f "$ZIP" ]] || { echo "Error: ZIP not found: $ZIP" >&2; exit 1; }

case "$VERSION" in
  H005|H004|H003) ;;
  *) echo "Error: --version must be H005, H004 or H003 (was: $VERSION)." >&2; exit 1 ;;
esac

# --- Tool checks -------------------------------------------------------------
need () { command -v "$1" >/dev/null 2>&1 || { echo "Missing: $1" >&2; exit 1; }; }
need unzip
SHACMD=""
if command -v sha256sum >/dev/null 2>&1; then SHACMD="sha256sum";
elif command -v shasum  >/dev/null 2>&1; then SHACMD="shasum -a 256";
else echo "Missing: sha256sum or shasum" >&2; exit 1; fi

# --- Expected files per version (for a plausibility check) -------------------
# Source: the ebics.org schema page. The list is the intended state, not
# necessarily complete for every sub-version - it serves as a warning hint.
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
ebics_signature_S002.xsd
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
      # H003 uses UNSUFFIXED file names (unlike H004/H005);
      # its master schema is called ebics.xsd.
      cat <<EOF
ebics.xsd
ebics_request.xsd
ebics_response.xsd
ebics_orders.xsd
ebics_types.xsd
ebics_keymgmt_request.xsd
ebics_keymgmt_response.xsd
ebics_hev.xsd
ebics_signature.xsd
xmldsig-core-schema.xsd
EOF
      ;;
  esac
}

# --- Unpacking ---------------------------------------------------------------
TMP="$(mktemp -d)"
cleanup () { [[ "$KEEP_TMP" == "1" ]] || rm -rf "$TMP"; }
trap cleanup EXIT

echo ">> Unpacking $ZIP ..."
unzip -o -q "$ZIP" -d "$TMP"

# Record the source ZIP hash (so the provenance stays traceable)
ZIP_HASH="$($SHACMD "$ZIP" | awk '{print $1}')"

# --- Prepare the target directory -------------------------------------------
DEST="${SCHEMA_ROOT}/${VERSION}"
echo ">> Target directory: $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"

# --- Sort the .xsd files in flat --------------------------------------------
# (Depending on the version, EBICS ZIPs contain subfolders - we flatten to file names.
#  On a name collision a warning is emitted and nothing is overwritten.)
echo ">> Sorting the .xsd files in ..."
found_count=0
while IFS= read -r -d '' f; do
  base="$(basename "$f")"
  if [[ -e "$DEST/$base" ]]; then
    echo "   WARN: name collision, skipped: $base" >&2
    continue
  fi
  cp "$f" "$DEST/$base"
  found_count=$((found_count+1))
done < <(find "$TMP" -type f -iname '*.xsd' -print0)

echo "   $found_count .xsd file(s) taken over."
[[ "$found_count" -eq 0 ]] && { echo "Error: no .xsd found in the ZIP." >&2; exit 1; }

# --- Compare against the expected list --------------------------------------
echo ">> Checking against the expected file list ($VERSION) ..."
missing=0
while IFS= read -r exp; do
  [[ -z "$exp" ]] && continue
  if [[ ! -e "$DEST/$exp" ]]; then
    echo "   missing (expected): $exp" >&2
    missing=$((missing+1))
  fi
done < <(expected_files "$VERSION")
if [[ "$missing" -gt 0 ]]; then
  echo "   $missing expected file(s) not found."
  [[ "$STRICT" == "1" ]] && { echo "   --strict set -> aborting." >&2; exit 2; }
  echo "   (Note: depending on the sub-version/instant XSD this can be fine.)"
else
  echo "   all expected files present."
fi

# --- Checksums per version ---------------------------------------------------
echo ">> Writing the SHA-256 manifest ..."
( cd "$DEST" && $SHACMD *.xsd | sort -k2 > MANIFEST.sha256 )
echo "   $DEST/MANIFEST.sha256"

# --- Aggregated JSON manifest across all versions ----------------------------
# (assembled by hand, without a jq dependency)
echo ">> Updating schemas/manifest.json ..."
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
MJSON="${SCHEMA_ROOT}/manifest.json"
TMPJSON="$(mktemp)"

# Read the existing sourceZipSha256 / ingestedAt per version from the old manifest,
# so that a run for version X does not discard the metadata of version Y.
# Without jq: a small awk that extracts the two fields per version block.
get_old_meta () {   # $1 = version, $2 = field name  -> value or empty
  [[ -f "$MJSON" ]] || return 0
  awk -v ver="$1" -v key="$2" '
    $0 ~ "\""ver"\"[[:space:]]*:[[:space:]]*\\{" { inblock=1 }
    inblock && $0 ~ "\""key"\"" {
      # A line of the form:  "key": "value",
      line=$0
      sub(/^[^:]*:[[:space:]]*"/, "", line)
      sub(/".*$/, "", line)
      print line
      inblock=0
    }
    inblock && /"files"[[:space:]]*:/ { inblock=0 }  # field not present
  ' "$MJSON"
}

{
  echo "{"
  echo "  \"generatedAt\": \"${NOW}\","
  echo "  \"note\": \"Reproducibly produced by scripts/fetch-schemas.sh. The source files are proprietary (EBICS SC) - see docs/protocol/schema-sources.md.\","
  echo "  \"versions\": {"
  first_v=1
  for vdir in "${SCHEMA_ROOT}"/H*/ ; do
    [[ -d "$vdir" ]] || continue
    vid="$(basename "$vdir")"
    man="${vdir}MANIFEST.sha256"
    [[ -f "$man" ]] || continue
    [[ $first_v -eq 0 ]] && echo ","
    first_v=0

    # Determine the metadata: fresh for the version currently being processed,
    # taken from the old manifest for the other versions.
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

# --- README stub in the version folder --------------------------------------
cat > "$DEST/README.md" <<EOF
# EBICS Schemas - $VERSION

Reproducibly sorted in by \`scripts/fetch-schemas.sh\` on ${NOW}.

- Source ZIP SHA-256: \`${ZIP_HASH}\`
- File checksums: see \`MANIFEST.sha256\`
- Sources & license: see \`../../docs/protocol/schema-sources.md\`

> These files are the proprietary property of the EBICS SC. Do not modify them.
> Before committing, check whether these files may go into the repo at all
> (see the license issue / docs/legal/ebics-licensing.md).
EOF

echo ">> Done. Schemas under: $DEST"
echo "   Tip: check 'git status schemas/' and mind the license question before you commit."

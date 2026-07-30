#!/usr/bin/env bash
###############################################################################
# generate-bindings.sh
#
# Produces the C# XSD bindings (EBICO.Core/Schema) reproducibly from the locally
# obtained EBICS schemas.
#
# WHY A SCRIPT?
#   The bindings are committed (ADR-0006), but the XSDs themselves stay
#   proprietary and untracked (ADR-0003). This script is the reproducible
#   way to regenerate the committed .cs after a schema update.
#   It is NOT a build step: CI compiles the committed bindings without
#   the schemas or this tool.
#
# PREREQUISITES
#   1) Schemas available locally:  ./scripts/fetch-schemas.sh ... (schemas/<V>/)
#   2) Tool restored:             dotnet tool restore  (.config/dotnet-tools.json
#                                 pins dotnet-xscgen / XmlSchemaClassGenerator)
#
# INVOCATION
#   ./scripts/generate-bindings.sh --all
#   ./scripts/generate-bindings.sh --version H005
#
# RESULT (idempotent; the target folders are cleanly refilled per run)
#   src/EBICO.Core/Schema/
#     H005/ H004/ H003/                 version-specific types
#     Shared/XmlDsig/                   W3C xmldsig (once, shared)
#     Shared/Hev/                       HEV / H000 (once, shared)
#     Shared/Signature/S001/            EBICS signature S001 (H003+H004)
#     Shared/Signature/S002/            EBICS signature S002 (H005)
#
# POST-PROCESSING: apply_binding_fixups() is applied after every run (a
#   documented intervention in the generated types, issue #117 / ADR-0029 —
#   see the comment on the function and docs/protocol/xsd-bindings.md).
#
# LICENSE: schemas/specs are proprietary (EBICS SC). The generated bindings
#   are derived artefacts; see docs/legal/ebics-licensing.md and ADR-0006.
###############################################################################
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCHEMA_ROOT="${REPO_ROOT}/schemas"
OUT_ROOT="${REPO_ROOT}/src/EBICO.Core/Schema"
DO_ALL="0"
VERSION=""

usage () {
  cat <<EOF
generate-bindings.sh - generate the EBICS XSD bindings reproducibly

  --all               Regenerate all versions (H003, H004, H005)
  --version <id>      A single version only: H005 | H004 | H003
  -h, --help          This help

Prerequisite: ./scripts/fetch-schemas.sh (schemas locally) + dotnet tool restore.
EOF
}

# --- Arguments ---------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --all)      DO_ALL="1"; shift ;;
    --version)  VERSION="${2:-}"; shift 2 ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ "$DO_ALL" == "0" && -z "$VERSION" ]]; then
  echo "Error: pass --all or --version <id>." >&2
  usage; exit 1
fi

# --- shared xscgen options ---------------------------------------------------
#   --nullable          : nullable adapter properties for optional value types
#                         (instead of *Specified; prevents silent data loss)
#   --netCore --pcl     : portable, framework-independent classes
#   --separateFiles     : one file per type (small, reviewable diffs)
#   --namespaceHierarchy: one folder per C# namespace -> separates shared/versioned
#   --commentLanguages en: <summary> from the XSD <annotation>s (English)
#   --commandArgs-      : no command line in the header (non-deterministic otherwise)
XSCGEN_COMMON=(--nullable --netCore --pcl --separateFiles --namespaceHierarchy
               --commentLanguages en --commandArgs-)

DS_MAP="http://www.w3.org/2000/09/xmldsig#=EBICO.Core.Schema.XmlDsig"
HEV_MAP="http://www.ebics.org/H000=EBICO.Core.Schema.Hev"
S001_MAP="http://www.ebics.org/S001=EBICO.Core.Schema.Signature.S001"
S002_MAP="http://www.ebics.org/S002=EBICO.Core.Schema.Signature.S002"

# Generates one version into a staging directory (echo: path to .../Schema)
generate_to_staging () {
  local ver="$1" staging="$2"
  local -a maps
  case "$ver" in
    H005) maps=(-n "urn:org:ebics:H005=EBICO.Core.Schema.H005" -n "$S002_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    H004) maps=(-n "urn:org:ebics:H004=EBICO.Core.Schema.H004" -n "$S001_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    H003) maps=(-n "http://www.ebics.org/H003=EBICO.Core.Schema.H003" -n "$S001_MAP" -n "$HEV_MAP" -n "$DS_MAP") ;;
    *) echo "Unknown version: $ver" >&2; return 1 ;;
  esac

  if [[ ! -d "${SCHEMA_ROOT}/${ver}" ]] || ! ls "${SCHEMA_ROOT}/${ver}"/*.xsd >/dev/null 2>&1; then
    echo "Error: no XSDs under ${SCHEMA_ROOT}/${ver}/ — run fetch-schemas.sh first." >&2
    return 2
  fi

  ( cd "$REPO_ROOT" && dotnet xscgen -o "$staging" "${maps[@]}" "${XSCGEN_COMMON[@]}" \
      "schemas/${ver}"/*.xsd >/dev/null )
}

# Copies a namespace folder out of the staging area to its target location
place () {
  local src="$1" dst="$2"
  rm -rf "$dst"
  mkdir -p "$dst"
  cp -r "$src/." "$dst/"
}

###############################################################################
# POST-PROCESSING OF THE GENERATED BINDINGS (issue #117, ADR-0029)
#
# xscgen does NOT translate an XSD <restriction> that re-types an element more
# concretely: in the static header, `OrderDetails` stays on the abstract
# `OrderDetailsType`. The XmlSerializer then demands an
# xsi:type discriminator that real third-party clients do not send -> their
# INI/HIA/HPB get rejected. In all three versions the concrete sub-types carry
# no members of their own; stripping `abstract` therefore costs nothing and
# makes the binding xsi:type-free in both directions (receiving stays tolerant,
# because the [XmlInclude] attributes remain in place).
#
# The intervention MUST live here and not only in the committed .cs, otherwise it
# silently disappears with the next regeneration. If the expected
# pattern goes away (a new xscgen/schema revision), the script aborts hard.
###############################################################################
apply_binding_fixups () {
  local ver="$1"
  local file="${OUT_ROOT}/${ver}/OrderDetailsType.cs"
  local tmp

  [[ -f "$file" ]] || { echo "Fixup error: $file is missing." >&2; return 1; }

  # \r?$ and the carried-along eol keep CRLF checkouts (Windows) unchanged.
  tmp="$(mktemp)"
  awk '
    /^    public abstract partial class OrderDetailsType\r?$/ {
      eol = (/\r$/ ? "\r" : "")
      printf "%s%s\n", "    // EBICO fixup (issue #117, ADR-0029) - applied by scripts/generate-bindings.sh:", eol
      printf "%s%s\n", "    // the generated type is `abstract`, which makes the XmlSerializer demand an", eol
      printf "%s%s\n", "    // xsi:type discriminator on <OrderDetails>. Real third-party clients follow the", eol
      printf "%s%s\n", "    // concrete schema type and omit it. The [XmlInclude]s above stay, so a request", eol
      printf "%s%s\n", "    // that does carry the discriminator still deserializes.", eol
      printf "%s%s\n", "    public partial class OrderDetailsType", eol
      patched = 1
      next
    }
    { print }
    END { if (!patched) exit 3 }
  ' "$file" > "$tmp" || {
    rm -f "$tmp"
    echo "Fixup error ($ver): 'public abstract partial class OrderDetailsType' not found." >&2
    echo "  -> The generator/schema revision has changed. Review the fixup (ADR-0029), do not ignore this." >&2
    return 1
  }

  mv "$tmp" "$file"
  echo "   Fixup applied: ${file#${REPO_ROOT}/}"
}

declare -A STAGES=()
cleanup () { for d in "${STAGES[@]:-}"; do [[ -n "$d" ]] && rm -rf "$d"; done; }
trap cleanup EXIT

TARGETS=()
if [[ "$DO_ALL" == "1" ]]; then TARGETS=(H003 H004 H005); else TARGETS=("$VERSION"); fi

# 1) Generate all target versions into their own staging directories
for ver in "${TARGETS[@]}"; do
  st="$(mktemp -d)"
  STAGES["$ver"]="$st"
  echo ">> generating $ver ..."
  generate_to_staging "$ver" "$st"
done

# 2) Place the version-specific types + apply the fixups
for ver in "${TARGETS[@]}"; do
  place "${STAGES[$ver]}/EBICO/Core/Schema/${ver}" "${OUT_ROOT}/${ver}"
  apply_binding_fixups "$ver"
done

# 3) Place the shared namespaces once (a deterministic source per namespace)
#    XmlDsig + Hev from H005 (or the first available version);
#    S002 from H005, S001 from H004/H003.
pick_stage () { # the first available version from the argument list
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

echo ">> done. Bindings under ${OUT_ROOT#${REPO_ROOT}/}"

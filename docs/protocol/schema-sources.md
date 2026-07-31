# EBICS schema and specification sources

This page documents **where** the EBICS schemas and specifications
come from, **how** they get into the repo reproducibly and **which
legal conditions** apply. It is the central reference for
the issue *"acquire schemas & specs"*.

> **Short version:** There are no stable direct links. Downloads sit behind
> an "I accept" button and are delivered via expiring `securedl` URLs.
> Therefore: download manually, then `scripts/fetch-schemas.sh` for the
> reproducible rest.

---

## 1. Sources (stable page URLs)

| Content | Version(s) | URL |
|---|---|---|
| EBICS schema (current) | H005 / EBICS 3.0 | https://www.ebics.org/en/technical-information/ebics-schema |
| Schema archive | H004 / EBICS 2.5 and older | https://www.ebics.org/en/technical-information/archive-ebics/schema |
| EBICS specification (current) | V 3.0.2 (valid from 30.12.2022) | https://www.ebics.org/en/technical-information/ebics-specification |
| Specification archive | older versions | https://www.ebics.org/en/technical-information/archive-ebics/specification |
| BTF mapping / External Code List | version-independent (last 23.10.2024) | https://www.ebics.org/en/technical-information/btf-mapping |
| Implementation Guide | — | https://www.ebics.org/en/technical-information/implementation-guide |
| Security Concept (Annex "TLS and KMS") | version-independent | https://www.ebics.org/en/technical-information/security-concept |
| Examples (sample XML) | — | https://www.ebics.org/en/technical-information/examples |
| Additional Standards | — | https://www.ebics.org/en/technical-information/additional-standards |
| Passed Change Requests | — | https://www.ebics.org/en/technical-information/maintain-advance/passed-crs |
| Terms of Use | — | https://www.ebics.org/en/informationen/disclaimer |

---

## 2. Included schema files

### H005 (EBICS 3.0) — from the current schema ZIP

| File | Purpose |
|---|---|
| `ebics_H005.xsd` | master schema, includes all others (consistency) |
| `ebics_request_H005.xsd` | protocol schema for standard requests |
| `ebics_response_H005.xsd` | protocol schema for standard responses |
| `ebics_orders_H005.xsd` | order-related reference elements and type definitions |
| `ebics_types_H005.xsd` | simple type definitions |
| `ebics_keymgmt_request_H005.xsd` | protocol schema for key-management requests |
| `ebics_keymgmt_response_H005.xsd` | protocol schema for key-management responses |
| `ebics_hev.xsd` | H000 — OrderType HEV |
| `ebics_signature_S002.xsd` | S002 — electronic signature (minor update of S001) |
| `xmldsig-core-schema.xsd` | W3C — standard schema for XML signature |

**Instant Payments:** For the clearing of Instant Payments there is a
separate request XSD. In `ebics_H005.xsd` it must **replace** the standard request XSD
as the `include`. For details see "EBICS Delta concept" on the
specification page.

> Note from the source: On 07.08.2017 `ebics_orders_H005.xsd` was
> updated (re-introduction of the element group `standardOrderParams`,
> needed among others for HAC downloads).

### H004 (EBICS 2.5) — from the schema archive

Analogous file structure with a `H004` suffix (`ebics_H004.xsd`,
`ebics_request_H004.xsd`, …). Please verify the exact file list against the
archive ZIP when acquiring.

### H003 (EBICS 2.4)

From the schema archive. Older, partly differing structure. The files are
named **without a suffix** (master `ebics.xsd`, plus `ebics_request.xsd`,
`ebics_orders.xsd`, … as well as `ebics_signature.xsd` for S001) — verify when acquiring.

---

## 3. Acquisition — step by step

1. **Download the schema ZIP manually:**
   - H005: open the schema page, confirm the terms "I accept", save the ZIP.
   - H004/H003: the same on the archive page.
2. **Have it processed:**
   ```bash
   ./scripts/fetch-schemas.sh --zip ~/Downloads/<ebics_3.0_schema>.zip --version H005
   ./scripts/fetch-schemas.sh --zip ~/Downloads/<ebics_2.5_schema>.zip --version H004
   ```
   Optionally `--strict`, so that missing expected files cause an error.
3. **Check the result:**
   - files under `schemas/<VERSION>/`
   - `schemas/<VERSION>/MANIFEST.sha256` (checksums per file)
   - `schemas/manifest.json` (aggregated over all versions, incl.
     source-ZIP hash and acquisition time)
4. **Before the commit:** clarify the licence question (see below).

The script is idempotent: it cleanly repopulates the version directory anew and
preserves the metadata of the respective other versions in the aggregated manifest.

---

## 4. Licence / Terms of Use — please note

The schemas and specifications are the **proprietary property of the EBICS SC**.
From the Terms of Use (state at time of recording):

- **Permitted:** downloading and reproducing, provided all copyright notices
  are preserved in full (non-exclusive, non-sublicensable
  licence).
- **Not permitted** (without prior written approval of the EBICS SC):
  modification or other *derivative uses* of the specifications.
- Products/services that are **not** based on the published EBICS specs
  may not be called "EBICS" and may not be marked with the EBICS logo.

### Consequences for the project (to be clarified, not legal advice)

- **XSDs into the repo?** As long as unclarified: **do not commit.** Instead each
  developer/CI job pulls them locally via `fetch-schemas.sh`. A
  `.gitignore` entry for `schemas/**/*.xsd` prevents accidental commits.
- **Generated XSD bindings** (via `XmlSerializer` codegen) are **committed**
  (decision Option B, [../adr/0006-commit-generated-xsd-bindings.md](../adr/0006-commit-generated-xsd-bindings.md));
  the XSDs themselves remain untracked. Approval of the EBICS SC is being pursued in parallel at
  `info@ebics.de`. Details: [xsd-bindings.md](xsd-bindings.md).
- **Copyright notices** of reproduced content: adopt in full.

Decision and rationale belong in an ADR as well as into
`docs/legal/ebics-licensing.md`.

---

## 5. Version states (to keep track of)

| Artefact | State at recording |
|---|---|
| EBICS specification | V 3.0.2, valid from 30.12.2022 (revision of V 3.0.1) |
| BTF External Code List | last updated 23.10.2024 |
| Annex "TLS and KMS" | renamed/extended on 20.03.2026 (formerly "Transport Layer Security") |
| `ebics_orders_H005.xsd` | updated 07.08.2017 |

> These states are a snapshot. When actually acquiring, re-check the
> dates/subversions given on ebics.org and record them in the
> `schemas/manifest.json`.

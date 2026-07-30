# Vendor captures: node-ebics-client

Real EBICS request XML, produced by a **third-party client**, for EBICO's conformance replay
(`tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs`, issue #59).

| Field | Value |
| --- | --- |
| Client | [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (npm) |
| Version | 5.0.0 |
| License | **MIT** |
| EBICS wire version | H004 |
| Produced with | `tools/vendor-capture/` (see its README) |

## Contents

`H004/request/{ini,hia,hpb}.xml` — the three onboarding requests (INI, HIA, HPB), captured at a
local throwaway sink (never at a real bank).

## Why committing this is allowed

These files are the **output of the OSS client**, not property of the EBICS SC and not a derivative of a
proprietary XSD/sample file — unlike the official ebics.org sample XML (which stays
`.gitignore`d). Rationale: [ADR-0026](../../../../../docs/adr/0026-konformitaet-gegen-reale-clients.md).

## Security

**All key material is throwaway material**, generated locally once (RSA keys in the
order data, X002 signature in HPB, nonces/timestamps). It belongs to no real subscriber and no
real bank. The IDs (`EBICOHOST`/`PARTNER1`/`USER1`) are placeholders.

Regenerate: `cd tools/vendor-capture && npm install && node capture.js` (produces fresh throwaway keys).

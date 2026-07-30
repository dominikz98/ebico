# Vendor capture tool (issue #59)

Produces **real EBICS request XML** from a third-party client for EBICO's conformance corpus. The
client used: [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (npm, **MIT**) — a
standalone Node.js EBICS client that speaks the H004 wire formats.

## What it does

Drives the client through the onboarding orders **INI / HIA / HPB** and captures the **exact
request bytes** it puts on the wire — into
`tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/H004/request/{ini,hia,hpb}.xml`. Those captures
are replayed against the real server by `tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs`.

The client posts against a **local throwaway sink** (never a real bank); the response is
discarded, only the request matters. All **key material is generated fresh here and is
throwaway material** (see `PROVENANCE.md` in the corpus).

## Running it (once, locally, offline)

```bash
cd tools/vendor-capture
npm install        # pulls ebics-client (MIT) — locally only, not in CI
npm run capture    # or: node capture.js
```

> **Not part of build/CI.** `dotnet build`/`dotnet test` and CI do not touch this directory.
> `node_modules/` and `package-lock.json` are `.gitignore`d. A repeat run produces new captures
> (fresh throwaway keys, new nonces/timestamps) — only re-commit them deliberately.

Background on how this fits in: [`docs/development/conformance-real-clients.md`](../../docs/development/conformance-real-clients.md)
and [`docs/adr/0026-konformitaet-gegen-reale-clients.md`](../../docs/adr/0026-konformitaet-gegen-reale-clients.md).

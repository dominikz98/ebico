# Getting started — a running emulator in 5 minutes

The fastest path to a running EBICS emulator and a first end-to-end round-trip with the
client. Implements **Issue #63** (Milestone M9 — Packaging & Docs). Prerequisite: either
**Docker** or a **.NET 10 SDK** (any `10.0.x` from `10.0.100`, see
[`global.json`](../global.json)) — nothing more (the generated schema bindings are
committed, see [Schemas & license](#schemas--license)).

## 1. Start the emulator

Two ways — pick one.

### Option A: Docker (no .NET SDK required)

```bash
docker compose up --build
#   server -> http://localhost:5014
#   suite  -> http://localhost:5267   (Blazor admin/inspector UI)
```

Alternatively just the published server image (available after the first tagged release, #62):

```bash
docker run --rm -p 5014:8080 ghcr.io/dominikz98/ebico-server:latest
```

Details & ENV configuration: [Container image](deployment/container.md).

### Option B: `dotnet run` (from source)

```bash
dotnet run --project src/EBICO.Server      # listens on http://localhost:5014
```

### Is it running? (check liveness)

```bash
curl -i http://localhost:5014/health       # -> 200 "Healthy"
```

The EBICS endpoint lives at **`/ebics`**, the (unauthenticated) admin API at **`/admin`** — the
server is a local emulator (like *Azurite*), **not** intended for unprotected networks (see
[Security](deployment/container.md#security)).

A freshly started server has **no master data**. Before a client can onboard, the bank,
partner and subscriber each need a `PUT` against the admin API; the fingerprints of the bank keys — the
emulator equivalent of the bank letter — are returned by `GET /admin/banks/{hostId}/keys`. Both are described in
[Master data management](server/master-data.md). The quickstart in step 2 takes care of this for you, because it
brings up its own server and seeds it in-process.

> **The Suite shows its own sample data.** It does **not** share its state with the server started
> here (ADR-0009/ADR-0015) — so this server's transactions do not appear there. The
> UI points this out with a banner.

## 2. Try the client (Quickstart sample)

You can experience the `EBICO.Connector` right away — the bundled sample starts a server **itself**
in-process and runs the full round-trip (keys → onboarding INI/HIA/HPB → upload CCT →
download C53). It needs **no** separately started server and no real bank:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart
```

Expected output (ports/IDs vary):

```text
EBICO.Server listening on http://127.0.0.1:52341 (EBICS endpoint http://127.0.0.1:52341/ebics, version H005).
Subscriber keys generated (A00x/X002/E002).
Onboarding: INI 000000, HIA 000000, HPB 000000.
Upload (CCT): 000000, TxId ..., 1 segment(s).
Download (C53): 011000, 1 segment(s), ... bytes, entries: ....
Quickstart completed successfully.
```

How to point the same client at a **separately running** server instead (from step 1) or a
real bank is shown by the DI setup (`AddEbicoConnector`, `o.Url = …`) in the
[Client core](connector/client-core.md); the sample code lives in
[`samples/EBICO.Connector.Quickstart`](../samples/EBICO.Connector.Quickstart/README.md).

## Other EBICS versions (H003 / H004 / H005)

EBICO supports **H003, H004 and H005**. The sample runs with all three; the default is H005, switch it
via argument or environment variable:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart -- --version H004
EBICO_QUICKSTART_VERSION=H003 dotnet run --project samples/EBICO.Connector.Quickstart
```

In your own code it is just the single option `o.Version = EbicsVersion.H004;` on the `AddEbicoConnector`
([Client core](connector/client-core.md)); the pipeline is otherwise version-agnostic. Background
on the multi-version dispatch: [Version dispatch](protocol/version-dispatch.md).

## Schemas & license

For the quickstart and operation you need **no** official EBICS schemas: the generated C# bindings
are committed to the repo ([ADR-0006](adr/0006-generierte-xsd-bindings-committen.md)), so the
server, sample and tests build and run without any further setup. The scripts
[`scripts/fetch-schemas.sh`](../scripts/fetch-schemas.sh) /
[`scripts/generate-bindings.sh`](../scripts/generate-bindings.sh) are purely **maintainer tooling** for
updating the bindings.

The EBICO **code** is licensed under **MIT** ([`LICENSE`](../LICENSE)). The EBICS schemas/specifications are
**proprietary property of the EBICS SC** and are **not** checked into the repo — when obtaining them
yourself, observe the Terms of Use of [ebics.org](https://www.ebics.org). Details:
[Schema sources & license](protocol/schema-sources.md), [License & repo policy](legal/ebics-licensing.md).

## Next steps

- [Client core & configuration](connector/client-core.md) — `AddEbicoConnector`, `IEbicsClient.Send`, options/DI
- [Onboarding](connector/onboarding.md) · [Upload](connector/upload.md) · [Download](connector/download.md) — the flows in detail
- [Container image](deployment/container.md) — operation, ENV configuration, docker-compose
- [Documentation index](index.md) — the full overview

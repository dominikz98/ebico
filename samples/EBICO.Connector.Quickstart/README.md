# EBICO.Connector — Quickstart

A **runnable end-to-end sample** for the `EBICO.Connector`. The console app starts the
`EBICO.Server` emulator **in-process** (Kestrel, ephemeral loopback port), seeds the required
master data and then drives the full EBICS round-trip with the connector:

1. generate the subscriber keys (A00x/X002/E002),
2. onboarding **INI → HIA → HPB**,
3. upload of a SEPA credit transfer (**CCT**, `pain.001`),
4. download of an account statement (**C53**, `camt.053`) with a parse hook.

It needs **no external server and no real bank**.

## Running it

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

The process exits with code `0` when every step succeeded functionally (handy for CI/scripts).

## Choosing the EBICS version (H003 / H004 / H005)

The round-trip runs for all three supported versions. The default is **H005**; switch it via an argument
(after `--`) or an environment variable:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart -- --version H004
dotnet run --project samples/EBICO.Connector.Quickstart -- H003          # positional
EBICO_QUICKSTART_VERSION=H004 dotnet run --project samples/EBICO.Connector.Quickstart
```

In code this is just the single line `o.Version = …` in the `AddEbicoConnector` setup (see
`QuickstartRunner.cs`); the rest of the pipeline is version-agnostic. Invalid/missing values fall
back to H005.

## Layout

- `Program.cs` — entry point, calls `QuickstartRunner.RunAsync`.
- `QuickstartRunner.cs` — hosts the server and drives the connector flow; returns a `QuickstartResult`
  per step (callable from tests as well).
- `SamplePain.cs` — builds a minimal, self-authored `pain.001` (no proprietary fixtures).

> Note: a *real* deployment points at your bank's URL or at a separately started
> `EBICO.Server` instead of an in-process one. The rest (DI setup, `IEbicsClient.Send`) stays the same.
> Details: [docs/connector/packaging.md](../../docs/connector/packaging.md) and
> [docs/connector/architecture.md](../../docs/connector/architecture.md).

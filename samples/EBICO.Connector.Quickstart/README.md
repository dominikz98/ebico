# EBICO.Connector — Quickstart

Ein **lauffähiges End-to-End-Beispiel** für den `EBICO.Connector`. Die Konsolenapp startet den
`EBICO.Server`-Emulator **in-process** (Kestrel, ephemerer Loopback-Port), seedet die nötigen
Stammdaten und fährt anschließend mit dem Connector den vollständigen EBICS-Rundlauf:

1. Teilnehmerschlüssel erzeugen (A00x/X002/E002),
2. Onboarding **INI → HIA → HPB**,
3. Upload eines SEPA Credit Transfer (**CCT**, `pain.001`),
4. Download eines Kontoauszugs (**C53**, `camt.053`) mit Parse-Hook.

Es braucht **keinen externen Server und keine echte Bank**.

## Ausführen

```bash
dotnet run --project samples/EBICO.Connector.Quickstart
```

Erwartete Ausgabe (Ports/IDs variieren):

```text
EBICO.Server läuft auf http://127.0.0.1:52341 (EBICS-Endpoint http://127.0.0.1:52341/ebics).
Teilnehmerschlüssel erzeugt (A00x/X002/E002).
Onboarding: INI 000000, HIA 000000, HPB 000000.
Upload (CCT): 000000, TxId ..., 1 Segment(e).
Download (C53): 011000, 1 Segment(e), ... Byte, Einträge: ....
Quickstart erfolgreich abgeschlossen.
```

Der Prozess endet mit Exit-Code `0`, wenn jeder Schritt fachlich erfolgreich war (praktisch für CI/Skripte).

## Aufbau

- `Program.cs` — Einstiegspunkt, ruft `QuickstartRunner.RunAsync`.
- `QuickstartRunner.cs` — hostet den Server und treibt den Connector-Flow; gibt ein `QuickstartResult`
  je Schritt zurück (auch aus Tests aufrufbar).
- `SamplePain.cs` — erzeugt eine minimale, selbst erstellte `pain.001` (keine proprietären Fixtures).

> Hinweis: Ein *echter* Einsatz zeigt statt eines in-process-Servers auf die URL Ihrer Bank bzw.
> auf einen separat gestarteten `EBICO.Server`. Der Rest (DI-Setup, `IEbicsClient.Send`) bleibt gleich.
> Details: [docs/connector/packaging.md](../../docs/connector/packaging.md) und
> [docs/connector/architecture.md](../../docs/connector/architecture.md).

# Vendor-Capture-Tool (Issue #59)

Erzeugt **echte EBICS-Request-XML** eines Fremd-Clients für EBICOs Konformitäts-Corpus. Verwendeter
Client: [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (npm, **MIT**) — ein
eigenständiger Node.js-EBICS-Client, der die H004-Wire-Formate spricht.

## Was es tut

Treibt den Client durch die Onboarding-Orders **INI / HIA / HPB** und greift die **exakten
Request-Bytes** ab, die er auf den Draht legt — in
`tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/H004/request/{ini,hia,hpb}.xml`. Diese Captures
werden von `tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs` gegen den echten Server
replayt.

Der Client postet gegen eine **lokale Wegwerf-Senke** (niemals eine echte Bank); die Antwort wird
verworfen, nur der Request zählt. Alles **Schlüsselmaterial wird hier frisch erzeugt und ist
Wegwerf-Material** (siehe `PROVENANCE.md` im Corpus).

## Ausführen (einmalig, lokal, offline)

```bash
cd tools/vendor-capture
npm install        # zieht ebics-client (MIT) — nur lokal, nicht in der CI
npm run capture    # bzw. node capture.js
```

> **Nicht Teil von Build/CI.** `dotnet build`/`dotnet test` und die CI berühren dieses Verzeichnis nicht.
> `node_modules/` und `package-lock.json` sind `.gitignore`d. Ein erneuter Lauf erzeugt neue Captures
> (frische Wegwerf-Schlüssel, neue Nonces/Timestamps) — nur bewusst neu committen.

Details zur Einordnung: [`docs/development/conformance-real-clients.md`](../../docs/development/conformance-real-clients.md)
und [`docs/adr/0026-konformitaet-gegen-reale-clients.md`](../../docs/adr/0026-konformitaet-gegen-reale-clients.md).

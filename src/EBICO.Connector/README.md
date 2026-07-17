# EBICO.Connector

Ein **EBICS-Client** als NuGet-Paket für den Zugriff auf einen EBICS-Server — konzeptionell
wie *Azurite* für Azure Storage, nur für EBICS. `EBICO.Connector` kapselt die komplette
Client-Pipeline hinter einer typsicheren API nach dem **Mediator-Muster**: Der Aufrufer kennt
nur `IEbicsClient.Send(request)` und erhält ein `EbicsResult<T>`. Unterstützte Protokoll­versionen:
**H003, H004, H005**.

Gegenstück ist der [EBICO-Server-Emulator](https://github.com/dominikz98/ebico) — damit lässt
sich der gesamte Ablauf lokal ohne echte Bank testen.

## Installation

```bash
dotnet add package EBICO.Connector
```

## Schnellstart

```csharp
using EBICO.Connector;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Upload;
using EBICO.Connector.Download;
using EBICO.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Verbindung konfigurieren. Wichtig: Url ist die ABSOLUTE URL inkl. Endpoint-Pfad.
services.AddEbicoConnector(o =>
{
    o.Url = "https://bank.example/ebics";
    o.HostId = "EBICOHOST";
    o.PartnerId = "PARTNER01";
    o.UserId = "USER01";
    o.Version = EbicsVersion.H005;
});

// Feature-Module dazuladen (jeweils optional).
services.AddEbicoOnboarding();
services.AddEbicoUpload();
services.AddEbicoDownload();

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IEbicsClient>();

// 1) Teilnehmerschlüssel (A00x/X002/E002) einmalig erzeugen und im Key-Store ablegen.
await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync();

// 2) Onboarding: INI -> HIA -> HPB.
await client.Send(new IniRequest());
await client.Send(new HiaRequest());
var hpb = await client.Send(new HpbRequest()); // Bank-Fingerprints ggf. gegen den Bankbrief prüfen

// 3) Upload eines SEPA Credit Transfer (pain.001).
var upload = await client.Send(new CctUploadRequest { Pain001 = painBytes });

// 4) Download eines Kontoauszugs (camt.053).
EbicsResult<DownloadResult> download = await client.Send(new C53DownloadRequest());
if (download.IsSuccess)
{
    ReadOnlyMemory<byte> orderData = download.Value!.OrderData; // entschlüsselt, i. d. R. ein ZIP
}
```

## Ergebnis & Fehlerbehandlung

- **Fachliche** Returncodes stehen in `EbicsResult<T>` (`IsSuccess`, `ReturnCode`, `ReturnText`) —
  es wird nichts geworfen. Ein erfolgreicher Download endet mit `011000`
  (`EBICS_DOWNLOAD_POSTPROCESS_DONE`), nicht `000000`.
- **Technische**/Konfigurationsfehler werfen Ausnahmen
  (`EbicsConfigurationException`, `EbicsTransportException`, …).

## Dokumentation

- [Connector-Architektur](https://github.com/dominikz98/ebico/blob/main/docs/connector/architecture.md)
- [Client-Kern & Konfiguration](https://github.com/dominikz98/ebico/blob/main/docs/connector/client-core.md)
- [Onboarding](https://github.com/dominikz98/ebico/blob/main/docs/connector/onboarding.md) ·
  [Upload](https://github.com/dominikz98/ebico/blob/main/docs/connector/upload.md) ·
  [Download](https://github.com/dominikz98/ebico/blob/main/docs/connector/download.md)
- [Packaging & Beispiele](https://github.com/dominikz98/ebico/blob/main/docs/connector/packaging.md)

Ein lauffähiges End-to-End-Beispiel (Server in-process) liegt unter
[`samples/EBICO.Connector.Quickstart`](https://github.com/dominikz98/ebico/tree/main/samples/EBICO.Connector.Quickstart).

## Lizenz

MIT — siehe [LICENSE](https://github.com/dominikz98/ebico/blob/main/LICENSE). Die EBICS-Schemas/Specs
selbst sind proprietäres Eigentum der EBICS SC und nicht Teil dieses Pakets.

# EBICO.Connector

An **EBICS client** as a NuGet package for accessing an EBICS server — conceptually
like *Azurite* for Azure Storage, only for EBICS. `EBICO.Connector` encapsulates the complete
client pipeline behind a type-safe API following the **mediator pattern**: the caller only knows
`IEbicsClient.Send(request)` and receives an `EbicsResult<T>`. Supported protocol versions:
**H003, H004, H005**.

Its counterpart is the [EBICO server emulator](https://github.com/dominikz98/ebico) — with it you can
test the entire flow locally without a real bank.

## Installation

```bash
dotnet add package EBICO.Connector
```

## Quickstart

```csharp
using EBICO.Connector;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Upload;
using EBICO.Connector.Download;
using EBICO.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure the connection. Important: Url is the ABSOLUTE URL incl. the endpoint path.
services.AddEbicoConnector(o =>
{
    o.Url = "https://bank.example/ebics";
    o.HostId = "EBICOHOST";
    o.PartnerId = "PARTNER01";
    o.UserId = "USER01";
    o.Version = EbicsVersion.H005;
});

// Add the feature modules (each one optional).
services.AddEbicoOnboarding();
services.AddEbicoUpload();
services.AddEbicoDownload();

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IEbicsClient>();

// 1) Generate the subscriber keys (A00x/X002/E002) once and store them in the key store.
await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync();

// 2) Onboarding: INI -> HIA -> HPB.
await client.Send(new IniRequest());
await client.Send(new HiaRequest());
var hpb = await client.Send(new HpbRequest()); // verify the bank fingerprints against the bank letter if needed

// 3) Upload of a SEPA credit transfer (pain.001).
var upload = await client.Send(new CctUploadRequest { Pain001 = painBytes });

// 4) Download of an account statement (camt.053).
EbicsResult<DownloadResult> download = await client.Send(new C53DownloadRequest());
if (download.IsSuccess)
{
    ReadOnlyMemory<byte> orderData = download.Value!.OrderData; // decrypted, usually a ZIP
}
```

## Result & error handling

- **Functional** return codes live in `EbicsResult<T>` (`IsSuccess`, `ReturnCode`, `ReturnText`) —
  nothing is thrown. A successful download ends with `011000`
  (`EBICS_DOWNLOAD_POSTPROCESS_DONE`), not `000000`.
- **Technical**/configuration errors throw exceptions
  (`EbicsConfigurationException`, `EbicsTransportException`, …).

## Documentation

- [Connector architecture](https://github.com/dominikz98/ebico/blob/main/docs/connector/architecture.md)
- [Client core & configuration](https://github.com/dominikz98/ebico/blob/main/docs/connector/client-core.md)
- [Onboarding](https://github.com/dominikz98/ebico/blob/main/docs/connector/onboarding.md) ·
  [Upload](https://github.com/dominikz98/ebico/blob/main/docs/connector/upload.md) ·
  [Download](https://github.com/dominikz98/ebico/blob/main/docs/connector/download.md)
- [Packaging & samples](https://github.com/dominikz98/ebico/blob/main/docs/connector/packaging.md)

A runnable end-to-end sample (server in-process) lives under
[`samples/EBICO.Connector.Quickstart`](https://github.com/dominikz98/ebico/tree/main/samples/EBICO.Connector.Quickstart).

## License

MIT — see [LICENSE](https://github.com/dominikz98/ebico/blob/main/LICENSE). The EBICS schemas/specs
themselves are the proprietary property of the EBICS SC and are not part of this package.

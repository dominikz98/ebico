# Connector: Client-Kern & Konfiguration

> Umsetzung von **Issue #46** (Milestone M6 — Connector). Diese Seite beschreibt
> den Grundstein des `EBICO.Connector`: die öffentlichen Abstraktionen, die
> Konfiguration, die DI-Registrierung und den eigenen Request-Dispatch. Der
> übergeordnete Entwurf steht in der [Connector-Architektur](architecture.md);
> die Entscheidung *kein MediatR* in
> [ADR-0005](../adr/0005-connector-dispatch-ohne-mediatr.md).

## Zweck

Der Connector-Kern verdrahtet die Bausteine, auf denen die späteren M6-Issues
(Onboarding INI/HIA/HPB, Upload, Download, NuGet-Packaging) aufsetzen. Eine
aufrufende App kennt nur **eine** Einstiegsmethode — `IEbicsClient.Send(...)` —
und bekommt ein typisiertes `EbicsResult<T>`. #46 liefert das *Skelett*
(Abstraktionen + Konfiguration + Dispatch + Default-Transport + Key-Store);
konkrete Requests/Handler kommen in Folge-Issues.

## Öffentliche Abstraktionen

Alle im Namespace `EBICO.Connector`:

| Typ | Rolle |
| --- | --- |
| `IEbicsRequest<TResult>` | Marker: ein Request „kennt" seinen Ergebnistyp. |
| `IEbicsClient` | Mediator; einzige Einstiegsmethode `Send<TResult>(request, ct)`. |
| `IEbicsRequestHandler<TRequest, TResult>` | Ein Handler je konkretem Request-Typ. |
| `EbicsContext` | Pro `Send` erzeugter Ausführungskontext (Connection, Keys, Transport, Version). |
| `EbicsResult<T>` | Ergebnis-/Returncode-Typ (**vorläufig**, siehe unten). |
| `EbicsConnectorException` | Basis-Exception; Ableitungen `EbicsConfigurationException`, `EbicsTransportException`. |

```csharp
public interface IEbicsClient
{
    Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult> request, CancellationToken ct = default);
}
```

## Konfiguration

`EbicsConnectionOptions` (Namespace `EBICO.Connector.Configuration`) hält die
Verbindungsparameter als bindebare Strings:

| Feld | Bedeutung |
| --- | --- |
| `Url` | absolute HTTP(S)-URL des EBICS-Server-Endpunkts |
| `HostId` | EBICS-`HostID` der Bank/des Servers |
| `PartnerId` | EBICS-`PartnerID` (Kunde) |
| `UserId` | EBICS-`UserID` (Teilnehmer) |
| `Version` | Ziel-Protokollversion (`EbicsVersion`, Default `H005`) |

Vor der Nutzung werden die Optionen validiert und in die unveränderliche
`EbicsConnection` überführt: die IDs werden über die validierten Core-Typen
(`HostId`/`PartnerId`/`UserId` aus `EBICO.Core.Domain`) geparst, die Version über
die `EbicsVersions`-Registry an ihre `EbicsVersionInfo` gebunden.

Die Validierung läuft über den Options-Mechanismus
(`EbicsConnectionOptionsValidator : IValidateOptions<EbicsConnectionOptions>`):
Bei ungültiger Konfiguration wirft die erste Auflösung der `EbicsConnection` eine
`OptionsValidationException` mit allen gefundenen Problemen (fehlende/ungültige
URL, ungültige Identifier, unbekannte Version). Direkte Aufrufe von
`EbicsConnection.FromOptions(...)` werfen bei Ungültigkeit eine
`EbicsConfigurationException`.

## DI-Registrierung

```csharp
services.AddEbicoConnector(o =>
{
    o.Url       = "https://bank.example/ebicsweb";
    o.HostId    = "MYHOST";
    o.PartnerId = "PARTNER01";
    o.UserId    = "USER0001";
    o.Version   = EbicsVersion.H005;
})
.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
.AddStandardResilienceHandler();   // optional; Resilienz-Paket beim Aufrufer
```

`AddEbicoConnector(...)` registriert die Options + ihren Validator, die
`EbicsConnection`, den Default-`InMemoryKeyStore`, den `HttpClientTransport`, den
`IEbicsClient` sowie einen **Named** `HttpClient`
(`EbicoConnector.HttpClientName`). Rückgabe ist der `IHttpClientBuilder` dieses
Named Clients — Timeouts und Resilienz werden also direkt daran konfiguriert und
bleiben so aus dem Connector-Kern heraus (Details:
[Architektur → DI-Registrierung](architecture.md#di-registrierung)).

## Transport

`ITransport` (Namespace `EBICO.Connector.Transport`) ist die schmale
Transport-Abstraktion: `SendAsync(EbicsHttpRequest, ct)` → `EbicsHttpResponse`.
Der Default `HttpClientTransport` bezieht seinen `HttpClient` über
`IHttpClientFactory` (Named Client), sendet den serialisierten XML-Envelope per
`POST` (`Content-Type: text/xml; charset=utf-8`) an die konfigurierte URL und
reicht den `CancellationToken` durch. Nicht-Erfolgs-Status und
HTTP-/Netzwerkfehler werden als `EbicsTransportException` geworfen — technische
Fehler sind Exceptions, fachliche Returncodes landen im `EbicsResult<T>`.

## Schlüsselspeicher

`IKeyStore` (Namespace `EBICO.Connector.Keys`) liefert Schlüsselmaterial
(`RsaKeyMaterial` aus `EBICO.Core.Crypto`), adressiert über `KeyOwner`
(`Subscriber`/`Bank`) und `KeyPurpose` (`Signature`/`Encryption`/
`Authentication`):

- **`InMemoryKeyStore`** — thread-safe, Default-Registrierung, ideal für Tests.
- **`FileKeyStore`** — eine Datei je Schlüssel unter einem konfigurierten
  Verzeichnis; Teilnehmer-Keys als PKCS#8 (privat), Bank-Keys als
  SubjectPublicKeyInfo (public), über das vorhandene `RsaKeyImportExport`.
  **Sicherheitshinweis:** private Schlüssel liegen *unverschlüsselt* auf Platte —
  nur für Entwicklung/einfache Setups; produktiv einen verschlüsselten Store oder
  HSM verwenden (spätere Issues).

Im Skelett ist der Store implizit auf die eine konfigurierte
Subscriber-Verbindung bezogen; Mehr-Teilnehmer-Scoping folgt später.

## Dispatch (ohne MediatR)

`Send<TResult>(IEbicsRequest<TResult>)` kennt statisch nur den Ergebnistyp, nicht
den konkreten Request-Typ. Der Client löst den passenden
`IEbicsRequestHandler<TRequest, TResult>` deshalb über einen **eigenen** Dispatch
auf (kein MediatR, [ADR-0005](../adr/0005-connector-dispatch-ohne-mediatr.md)):

1. Zur Laufzeit wird über `request.GetType()` ein typgebundener Wrapper
   (`RequestHandlerWrapper<TRequest, TResult>`) erzeugt und in einem
   `ConcurrentDictionary<Type, object>` gecacht — Reflection nur beim ersten
   Auftreten eines Request-Typs, danach ein virtueller Aufruf.
2. Pro `Send` öffnet der Client einen DI-Scope, löst `EbicsConnection`,
   `IKeyStore` und `ITransport` auf, baut den `EbicsContext` und ruft den Wrapper.
3. Der Wrapper holt den Handler aus dem Scope. Fehlt er, wirft er eine
   `EbicsConfigurationException`.

Handler werden von späteren Issues als
`services.AddSingleton<IEbicsRequestHandler<TReq, TRes>, THandler>()` registriert
(im Test über einen Fake-Handler).

## Versionsbindung

Die Zielversion aus `o.Version` wird über die Core-`EbicsVersions`-Registry an
ihre `EbicsVersionInfo` gebunden und im `EbicsContext.Version` bereitgestellt;
darauf setzen Envelope-Namespaces und Header-Aufbau der Handler auf
([Versions-Dispatch](../protocol/version-dispatch.md)).

## `EbicsResult<T>` — vorläufig

`EbicsResult<T>` trennt fachlichen Erfolg (mit Wert), fachlichen Returncode (kein
Fehler) und technische Fehler (Exception). Instanzen werden über
`EbicsResult<T>.Success(value, [code], [text])` bzw.
`EbicsResult<T>.Failure(code, [text])` erzeugt.

> **Vorläufig:** Die endgültige Form und der vollständige EBICS-Returncode-Katalog
> werden in **#36 (M4)** definiert; die hier eingeführte connector-lokale Form
> hält #46 self-contained und wird mit #36 abgeglichen.

## Tests

`tests/EBICO.Tests/Connector/` deckt ab: `EbicsResult`-Semantik,
Options-Validierung (Happy Path + Negativfälle), `EbicsConnection`-Auflösung,
In-Memory-/Datei-KeyStore-Round-Trips, den Dispatch (Fake-Handler; kein Handler →
`EbicsConfigurationException`) und den `HttpClientTransport` gegen einen
gestubbten `HttpMessageHandler` (POST/Content-Type/Payload, Nicht-Erfolg →
`EbicsTransportException`, Cancellation).

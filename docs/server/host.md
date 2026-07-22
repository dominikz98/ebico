# Server: Hostable Grundgerüst (ASP.NET Core)

> Umsetzung von **Issue #25** (Milestone M3 — Server). Diese Seite beschreibt das
> *Grundgerüst* des EBICS-Emulator-Hosts (`EBICO.Server`): den HTTP-Endpoint, die
> Request-Pipeline mit **Verify/Handle als Erweiterungspunkten** (No-Op-Defaults),
> die zentrale Fehlerabbildung auf EBICS-Returncodes und den pluggbaren
> In-Memory-Zustandsspeicher. Die eigentlichen Auftragsverarbeitungen (INI/HIA/HPB
> und die Transaktions-Engine) folgen in **#26 ff. / M4**. Der Returncode-Katalog ist
> bewusst vorläufig (voller Katalog → **#36 / M4**), die Vereinigung mit dem
> Suite-Read-Model (`IEmulatorStateProvider`) ist **M4**.

## Zweck

`EBICO.Server` ist der EBICS-Server-Emulator — konzeptionell wie *Azurite* für Azure
Storage, aber für EBICS. #25 liefert das *Skelett*: einen hostbaren ASP.NET-Core-Host,
der EBICS-Requests über HTTP annimmt, sie durch eine testbare Pipeline schickt und
wohlgeformte EBICS-Antworten zurückgibt. Fachliche Auftragslogik kommt in den
Folge-Issues; hier steht die tragende Struktur und ihre Erweiterungspunkte.

Bewusst **im Skelett enthalten**: HTTP-Endpoint, Parsing, Versions-Dispatch,
Fehlerabbildung, Antwort-Serialisierung, DI-Verdrahtung, State-Store-Abstraktion.
Bewusst **noch nicht**: Order-Handler (INI/HIA/HPB), Signatur-/Verschlüsselungsprüfung,
Antwort-Signatur (X002), Persistenz, Segmentierung.

## Host & `Program.cs`

Der Host wird über eine einzige DI-Extension verdrahtet und mappt den EBICS-Endpoint
auf einen konfigurierbaren Pfad (Default `/ebics`):

```csharp
// Program.cs
builder.Services.AddEbicoServer();

var app = builder.Build();
var options = app.Services.GetRequiredService<IOptions<EbicoServerOptions>>().Value;
app.MapEbicsEndpoint(options.EndpointPath);   // POST, text/xml
app.Run();

public partial class Program;   // damit Integrationstests WebApplicationFactory<Program> nutzen können
```

`AddEbicoServer(Action<EbicoServerOptions>?)` registriert die Pipeline, die
Erweiterungspunkte mit ihren Skelett-Defaults, den Error-Mapper, die Response-Factory
und den In-Memory-State-Store. Alle konkreten Services werden mit `TryAdd*` registriert,
sodass Aufrufer jeden Baustein vorab überschreiben können (Muster wie `AddEbicoConnector`).

`EbicoServerOptions` steuert Endpoint-Pfad, Fallback-Antwortversion (wenn die
Request-Version nicht erkennbar ist), maximale Body-Größe und die akzeptierten
Content-Types. Seit **M4** zusätzlich die Transaction-Engine-Parameter: Segmentgröße
(`SegmentSizeBytes`), die Segment-Obergrenzen (`MaxUploadSegments`/`MaxDownloadSegments`)
und — mit [#35](transaction-recovery.md) — der Transaktions-Idle-Timeout
(`TransactionTimeout`), das Sweep-Intervall des Cleanup-Dienstes
(`TransactionCleanupInterval`) und die obere Schranke paralleler Transaktionen
(`MaxConcurrentTransactions`).

## Request-Pipeline

Der Endpoint-Handler bleibt dünn: Er liest den Body transport-sicher und delegiert an
`IEbicsRequestPipeline.ProcessAsync(string) → EbicsPipelineResult`. Die Pipeline ist
**HTTP-frei** (String rein, Bytes raus) und daher ohne Web-Host unit-testbar.

| Stufe | Umsetzung | Fehlerpfad → Returncode |
| --- | --- | --- |
| **Parse** | `EbicsXmlSerializer.DeserializeEnvelope(xml)` (Core) | malformed/leer **oder wohlgeformt-aber-nicht-abbildbar** (#117) → `091010` |
| **Version-Dispatch** | Root-Namespace → Version, Root-Element → Envelope-Typ; Cast auf `IEbicsRequestEnvelope` | nicht unterstützte Version / kein Request-Envelope → `061002` |
| **Verify** | `IEbicsRequestVerifier.VerifyAsync` (Default: No-Op → Erfolg) | Fehlschlag → `061001` |
| **Handle** | `IEbicsOrderHandlerResolver.Resolve(version, orderType)` (Skelett: kein Handler) | kein Handler → `091006`; leerer/unbek. Order-Typ → `091005` |
| **Respond** | `EbicsResponseFactory.BuildErrorResponse(version, code)` → `SerializeToUtf8Bytes` | — |

Parsing und Versions-Dispatch werden aus `EBICO.Core` wiederverwendet
([Versions-Dispatch](../protocol/version-dispatch.md)); das Parsing ist gegen DTD/XXE
gehärtet (`DtdProcessing.Prohibit`, `XmlResolver = null`), da der Server unvertrautes
XML annimmt.

### Erweiterungspunkte

Die Stufen **Verify** und **Handle** sind Interfaces mit Skelett-Defaults, an denen die
M3/M4-Features andocken:

| Typ | Rolle | Skelett-Default |
| --- | --- | --- |
| `IEbicsRequestVerifier` | Signatur-/Zustandsprüfung (X002, HostID/User, Subscriber-State) | seit #58 `X002EbicsRequestVerifier` (prüft die X002-Signatur signierter `ebicsRequest`, [Details](../development/negative-security-cases.md)); das ursprüngliche Skelett war `NoOpEbicsRequestVerifier` |
| `IEbicsOrderHandler` | Verarbeitung genau eines Order-Typs einer Version | *keine Registrierung* |
| `IEbicsOrderHandlerResolver` | Auflösung `(Version, OrderType) → Handler` | `EbicsOrderHandlerResolver` über `IEnumerable<IEbicsOrderHandler>` (leer) |

Da kein Handler registriert ist, beantwortet das Skelett jeden erkannten Request mit
`EBICS_UNSUPPORTED_ORDER_TYPE` (`091006`) — genug, um die Pipeline end-to-end zu zeigen.

### Body-Lesen (transport-sicher)

`EbicsRequestReader` prüft den Content-Type (Default `text/xml`/`application/xml`),
erzwingt die maximale Body-Größe und dekodiert mit dem deklarierten Charset (Default
UTF-8). Es parst **kein** XML — die (gehärtete) XML-Verarbeitung liegt ausschließlich in
`EBICO.Core`.

## Fehlerabbildung & HTTP-Semantik

Die zentrale Exception→Returncode-Abbildung liegt in `EbicsErrorMapper`
(`IEbicsErrorMapper`, pluggbar). Pipeline-interne Fälle (kein Handler, Verify-Fehlschlag)
werden direkt im Orchestrator gesetzt.

| Situation | Returncode | HTTP-Status |
| --- | --- | --- |
| Wohlgeformter Request, kein Handler | `091006` EBICS_UNSUPPORTED_ORDER_TYPE | **200** |
| Leerer/unbekannter Order-Typ | `091005` EBICS_INVALID_ORDER_TYPE | **200** |
| Ungültiges/leeres XML | `091010` EBICS_INVALID_XML | **200** |
| Nicht unterstützte Version / kein Request-Envelope | `061002` EBICS_INVALID_REQUEST | **200** |
| Verify fehlgeschlagen | `061001` EBICS_AUTHENTICATION_FAILED | **200** |
| Unerwarteter interner Fehler | `061099` EBICS_INTERNAL_ERROR | **200** |
| Falscher Content-Type | — | **415** |
| Body zu groß | — | **413** |

**Grundregel:** EBICS ist ein Anwendungsprotokoll *über* HTTP. Protokoll- und
Businessfehler werden mit **HTTP 200** und dem Returncode im `ebicsResponse`
beantwortet — der Client wertet den Returncode aus, nicht den HTTP-Status. Nur echte
Transportfehler (Content-Type, Größe), bei denen der Server nicht sinnvoll ins Envelope
antworten kann, führen zu HTTP 4xx.

## Returncode-Katalog (zentral in `EBICO.Core`)

`EbicsReturnCode` bündelt Code, symbolischen Namen und die Ablage (`Kind`): ein
**technischer** Code landet im `header/mutable/ReturnCode`, ein **business** Code im
`body/ReturnCode`; die jeweils andere Stelle bekommt `000000`. `EbicsResponseFactory`
baut daraus je Version (H003/H004/H005) den typisierten Response-Graphen aus den
committeten Schema-Bindings.

Der Katalog und die Registry (`EbicsReturnCodes`) liegen seit **Issue #36 (M4)** zentral in
`EBICO.Core.ReturnCodes` und werden von Server **und** Connector genutzt; das
Exception→Returncode-Mapping (`IEbicsErrorMapper`/`EbicsErrorMapper`) bleibt server-seitig.
Details, vollständige Code-Tabellen und das Fehlerverhalten:
[Returncode-Katalog](../protocol/return-codes.md) und [ADR-0012](../adr/0012-returncode-katalog.md).

### ⚠️ Spec-Vorbehalte (gegen die offiziellen EBICS-Annexe zu verifizieren)

- **Header- vs. Body-Platzierung** der Codes und mögliche Doppelbelegung (besonders
  `091010` EBICS_INVALID_XML) sind gegen Annex 1 zu prüfen.
- **„Nicht unterstützte Version"** hat im `ebicsResponse` keinen dedizierten Code
  (spec-konform ist die Versionsaushandlung per HEV); das Skelett bildet pragmatisch auf
  `061002` in der Fallback-Version ab.
- Die **Antwort-Signatur (X002)** fehlt im Skelett bewusst (= M4); strikte Clients
  könnten unsignierte Antworten ablehnen.
- `TransactionPhaseType` serialisiert mangels `*Specified`-Flag immer `Initialisation` —
  für eine transaktionsfreie Fehlerantwort gegen Schema/Spec zu prüfen.

## Zustandsspeicher (pluggable, In-Memory)

`IEbicsStateStore` ist der autoritative serverseitige Zustand (Banken/Partner/Teilnehmer,
lesend **und** schreibend) auf Basis der `EBICO.Core.Domain`-Aggregate. Default-Registrierung
ist der thread-sichere `InMemoryEbicsStateStore` (Vorbild `InMemoryKeyStore` im Connector),
pluggbar via `TryAddSingleton`.

```csharp
public interface IEbicsStateStore
{
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default);
    Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default);
    Task RegisterBankAsync(Bank bank, CancellationToken ct = default);
    // … Partner- und Subscriber-Pendants (Subscriber per (HostId, PartnerId, UserId)-Tripel)
}
```

Er ist das **read/write-Gegenstück** zum read-only `IEmulatorStateProvider` der Suite
(siehe [UI-Grundgerüst](../suite/ui-shell.md)). Beide arbeiten auf denselben
Domänen-Aggregaten; die Zusammenführung (In-Process oder HTTP-API, siehe
[ADR-0009](../adr/0009-blazor-render-mode.md)) erfolgt in **M4** — das Suite-Read-Model
bleibt bis dahin am `SampleEmulatorStateProvider`.

Auf diesem Store setzt in **#30** die vollständige **Stammdatenverwaltung** auf: der Store
wurde um `Remove*` und bankscoped Abfragen erweitert (Partner nun per (`HostId`, `PartnerId`)),
darüber liegt der `IMasterDataManager` (referentielle Integrität, kaskadierendes Löschen,
Permission-/Lebenszyklus-Mutation) samt einer unauthentifizierten HTTP-Admin-API
(`MapEbicoAdminApi`). Details: [Stammdatenverwaltung](master-data.md).

### Roh-XML-Erfassung (`IMessageCaptureStore`, #54)

Nach dem Serialisieren der Antwort schreibt die Pipeline die **Roh-XML** (Request und Response) einer
Transaktionsnachricht in den `IMessageCaptureStore` — keyed nach Transaktions-ID, mit Phase und
Returncode. Nur transaktionsbezogene Nachrichten werden erfasst (Key-Management ohne Transaktions-ID
bleibt außen vor); der In-Memory-Default begrenzt Speicher per Ring-Puffer
(`EbicoServerOptions.MaxMessageCaptureEntries`) und Kürzung je Dokument
(`MaxCapturedMessageBytes`). Gelesen wird er ausschließlich vom
[Suite-Transaktions-Inspektor](../suite/transaktions-inspektor.md) ([ADR-0021](../adr/0021-message-capture-store.md)).

## Tests

`tests/EBICO.Tests/Server/` deckt ab (xUnit v3 + AwesomeAssertions; Request-XML wird aus
den committeten Core-Bindings gebaut — **keine** proprietären Fixtures nötig):

- `EbicsErrorMapperTests` — Exception → Returncode (InvalidXml/InvalidRequest/InternalError, null-Guard).
- `EbicsResponseFactoryTests` — je Version: Round-Trip via `DeserializeEnvelope`, Code-Platzierung
  header vs. body, korrekter Versions-Namespace, `ReportText`.
- `InMemoryEbicsStateStoreTests` — Round-Trip, unbekannte Lookups → `null`, Add-or-Replace, null-Guard.
- `EbicoServerServiceCollectionExtensionsTests` — auflösbare Services, Skelett-Defaults
  (No-Op-Verifier, keine Handler), Options-Defaults/Override, null-Guard.
- `EbicsRequestPipelineTests` — Orchestrator direkt: malformed/leer → `091010`, fremder
  Namespace → `061002`, wohlgeformter Request ohne Handler → `091006`, Response-Envelope
  als Eingang → `061002`.
- `EbicsEndpointIntegrationTests` — über `WebApplicationFactory<Program>`: Happy-Path
  (HTTP 200 + `091006`), malformed/leer → 200 + `091010`, fremde Version → 200 in
  Fallback-Version, falscher Content-Type → 415, Body zu groß → 413.

Für die Integrationstests wurde `Microsoft.AspNetCore.Mvc.Testing` aufgenommen und im
Testprojekt eine `FrameworkReference` auf `Microsoft.AspNetCore.App` gesetzt; der globale
`Program`-Typ wird per `extern alias` gegen den der Suite disambiguiert.

## Verwandte Doku

- [Versions-Dispatch](../protocol/version-dispatch.md) — die im Parse-/Dispatch-Schritt genutzte Erkennung
- [XML-Serialisierung & C14N](../protocol/serialization-c14n.md) — deterministische Antwort-Serialisierung
- [Domänenmodell](../protocol/domain-model.md) — die Aggregate hinter dem State-Store
- [Client-Kern & Konfiguration](../connector/client-core.md) — Vorbild für DI/Options/Store und vorläufiges `EbicsResult`
- [UI-Grundgerüst & Navigation](../suite/ui-shell.md) — das Suite-Gegenstück (`IEmulatorStateProvider`)
- [ADR-0004 (Multi-Version)](../adr/0004-multi-version-strategie.md), [ADR-0009 (Suite-Render-Modus)](../adr/0009-blazor-render-mode.md)

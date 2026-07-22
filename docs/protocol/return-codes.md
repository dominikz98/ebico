# EBICS-Returncode-Katalog (H003/H004/H005)

Der zentrale Katalog der sechsstelligen EBICS-Returncodes in `EBICO.Core`: technische und
fachliche Codes als Konstanten, eine Registry für den Lookup und das server-seitige
Exception→Returncode-Mapping. Bis hierher gab es nur einen bewusst vorläufigen, server-lokalen
Satz von neun Codes (Grundgerüst #25) und ein paralleles `EbicsResult<T>` im Connector. Issue
**#36** (Milestone M4) führt beide auf einen zentralen Katalog zusammen. Konventionen:
[ADR-0012](../adr/0012-returncode-katalog.md) und [ADR-0007](../adr/0007-domaenen-value-objects-record-struct.md).

> **Abgrenzung:** Der Katalog liefert die Codes als Konstanten und ordnet Exceptions darauf ab.
> Die eigentliche Antworterzeugung (`EbicsResponseFactory`) und die Request-Pipeline bleiben
> server-seitig. Die HEV-/H000-System-Returncodes (`SystemReturnCodeType`) sind **nicht** Teil
> dieses Katalogs. Die Antwort-Signatur (X002) und die echte ES-/Auth-Prüfung bleiben M4.

## Bausteine

Der Katalog liegt unter `src/EBICO.Core/ReturnCodes/` (Namespace `EBICO.Core.ReturnCodes`); das
Mapping bleibt server-seitig unter `src/EBICO.Server/ReturnCodes/` bzw. `.../Handlers/`
(`EBICO.Core` darf nicht auf `EBICO.Server` referenzieren).

| Baustein | Ort | Aufgabe |
|---|---|---|
| `EbicsReturnCode` | `Core/ReturnCodes/EbicsReturnCode.cs` | Value-Object (`readonly record struct`): `Code`, `SymbolicName`, `Kind`; statische Felder je Code + `const OkCode` |
| `EbicsReturnCodeKind` (Enum) | `Core/ReturnCodes/EbicsReturnCodeKind.cs` | `Technical` (Header) / `Business` (Body) |
| `EbicsReturnCodes` | `Core/ReturnCodes/EbicsReturnCodes.cs` | Registry: `All`, `Get`, `TryFromCode`, `IsSuccess` (Vorbild `KeyVersions`) |
| `IEbicsErrorMapper` / `EbicsErrorMapper` | `Server/ReturnCodes/` | Exception → `EbicsReturnCode` (zentrale, pluggbare Abbildung) |
| `EbicsOrderDataException` | `Server/Handlers/EbicsOrderDataException.cs` | „Order-Data unlesbar" — mappt eindeutig auf `090004` |
| `OrderDataFault` | `Server/Handlers/OrderDataFault.cs` | kapselt den Decode-Schritt der Handler und wirft `EbicsOrderDataException` |

## Aufbau eines Returncodes

Jeder Code trägt den sechsstelligen `Code`, den symbolischen EBICS-Namen (`SymbolicName`, dient
als Report-Text im Header) und die Ablage (`Kind`): ein **technischer** Code landet im
`header/mutable/ReturnCode`, ein **fachlicher** Code im `body/ReturnCode`. Die jeweils andere
Stelle bekommt `000000` (`OkCode`). Der Report-Text folgt dem Header-Code.

```csharp
// Lookup über die Registry:
if (EbicsReturnCodes.TryFromCode("091010", out var rc))
{
    // rc.SymbolicName == "EBICS_INVALID_XML", rc.Kind == EbicsReturnCodeKind.Business
}

bool ok = EbicsReturnCodes.IsSuccess("000000"); // true
```

## Katalog

Werte und symbolische Namen folgen EBICS Annex 1. Die neun vom laufenden Code genutzten Codes
gelten als verifiziert; alle weiteren Einträge sind zur Vollständigkeit aufgenommen und im XML-Doc
mit `⚠️ Spec-Vorbehalt` markiert (gegen die offiziellen Annexe zu verifizieren).

**Technisch** (Header, `header/mutable/ReturnCode`):

| Code | Symbolischer Name | Bedeutung |
|---|---|---|
| `000000` | `EBICS_OK` | Kein Fehler (füllt auch den ungenutzten Slot) |
| `011000` | `EBICS_DOWNLOAD_POSTPROCESS_DONE` | Download-Nachbearbeitung erledigt ⚠️ |
| `011001` | `EBICS_DOWNLOAD_POSTPROCESS_SKIPPED` | Nachbearbeitung übersprungen (negative Quittung) ⚠️ |
| `011101` | `EBICS_TX_SEGMENT_NUMBER_UNDERRUN` | weniger Segmente als angekündigt ⚠️ |
| `031001` | `EBICS_ORDER_PARAMS_IGNORED` | Order-Parameter ignoriert (informativ) ⚠️ |
| `061001` | `EBICS_AUTHENTICATION_FAILED` | Authentifikationssignatur ungültig |
| `061002` | `EBICS_INVALID_REQUEST` | Request nicht spezifikationskonform |
| `061099` | `EBICS_INTERNAL_ERROR` | interner Serverfehler |
| `061101` | `EBICS_TX_RECOVERY_SYNC` | Transaktion muss re-synchronisiert werden ⚠️ |

**Fachlich** (Body, `body/ReturnCode`):

| Code | Symbolischer Name | Bedeutung |
|---|---|---|
| `090003` | `EBICS_AUTHORISATION_ORDER_TYPE_FAILED` | Teilnehmer für Auftragstyp nicht berechtigt ⚠️ |
| `090004` | `EBICS_INVALID_ORDER_DATA_FORMAT` | Order-Data unlesbar/formfehlerhaft |
| `090005` | `EBICS_NO_DOWNLOAD_DATA_AVAILABLE` | keine Download-Daten vorhanden ⚠️ |
| `091002` | `EBICS_INVALID_USER_OR_USER_STATE` | Teilnehmer unbekannt / im falschen Zustand |
| `091003` | `EBICS_USER_UNKNOWN` | Teilnehmer unbekannt ⚠️ |
| `091004` | `EBICS_INVALID_USER_STATE` | Teilnehmer im unzulässigen Zustand ⚠️ |
| `091005` | `EBICS_INVALID_ORDER_TYPE` | Auftragstyp ungültig/unbekannt |
| `091006` | `EBICS_UNSUPPORTED_ORDER_TYPE` | Auftragstyp nicht unterstützt |
| `091008` | `EBICS_BANK_PUBKEY_UPDATE_REQUIRED` | Bankschlüssel müssen (HPB) aktualisiert werden ⚠️ |
| `091009` | `EBICS_SEGMENT_SIZE_EXCEEDED` | Segment zu groß ⚠️ |
| `091010` | `EBICS_INVALID_XML` | XML nicht wohlgeformt/schemakonform |
| `091011` | `EBICS_INVALID_HOST_ID` | `HostID` unbekannt ⚠️ |
| `091101` | `EBICS_TX_UNKNOWN_TXID` | Transaktions-ID unbekannt ⚠️ |
| `091102` | `EBICS_TX_ABORT` | Transaktion abgebrochen ⚠️ |
| `091103` | `EBICS_TX_MESSAGE_REPLAY` | Nachricht eines Schritts wiederholt ⚠️ |
| `091104` | `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` | mehr Segmente als angekündigt ⚠️ |
| `091112` | `EBICS_INVALID_REQUEST_CONTENT` | Request-Inhalt für die Operation unzulässig ⚠️ |
| `091113` | `EBICS_MAX_ORDER_DATA_SIZE_EXCEEDED` | Order-Data zu groß ⚠️ |
| `091114` | `EBICS_MAX_SEGMENTS_EXCEEDED` | zu viele Segmente ⚠️ |
| `091115` | `EBICS_MAX_TRANSACTIONS_EXCEEDED` | zu viele parallele Transaktionen ⚠️ |
| `091116` | `EBICS_PARTNER_ID_MISMATCH` | `PartnerID` passt nicht zur Transaktion ⚠️ |
| `091117` | `EBICS_INCOMPATIBLE_ORDER_ATTRIBUTE` | Order-Attribut inkompatibel zum Auftragstyp ⚠️ |

## Exception → Returncode (Fehlerverhalten)

`EbicsErrorMapper` ist die **einzige** Quelle für die Exception→Code-Abbildung der
Request-Verarbeitung. Order-Data-Fehler werden von den Handlern als
`EbicsOrderDataException` (via `OrderDataFault.Wrap`) sichtbar gemacht; ihr eigener Typ mappt
eindeutig, unabhängig von der Ursache.

| Exception(-Gruppe) | Returncode | Ablage |
|---|---|---|
| `EbicsOrderDataException`; `KeyMaterialException`, `InvalidKeyVersionException`, `KeyVersionNotPermittedException`; `InvalidDataException`, `FormatException`, `CryptographicException` | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` | Body |
| `InvalidEbicsIdentifierException`, `InvalidSubscriberStateTransitionException`, `MasterDataException` (`UnknownBank/Partner/Subscriber`) | `091002` `EBICS_INVALID_USER_OR_USER_STATE` | Body |
| `EbicsEnvelopeFormatException`; `XmlException`; `InvalidOperationException { XmlException }` | `091010` `EBICS_INVALID_XML` | Body |
| `EbicsVersionNotSupportedException`, `EbicsVersionMismatchException` | `061002` `EBICS_INVALID_REQUEST` | Header |
| alles Übrige (z. B. blankes `ArgumentException`/`InvalidOperationException`) | `061099` `EBICS_INTERNAL_ERROR` | Header |

> **Bewusste Entscheidung:** Ein blankes `ArgumentException`/`InvalidOperationException` mappt
> **nicht** auf `090004`, sondern auf `061099` — außerhalb des Order-Data-Decode-Schritts ist es ein
> Serverfehler, kein Client-Datenfehler. Der Decode-Schritt der Handler (`OrderDataFault.Wrap`)
> übersetzt genau die dort erwarteten Low-Level-Fehler in `EbicsOrderDataException`, sodass die
> Kontextabhängigkeit erhalten bleibt (Order-Data-XML → `090004`, Envelope-XML → `091010`).
>
> Genau dieselbe Kontextabhängigkeit erledigt seit **#117** die Envelope-Grenze selbst:
> `EbicsXmlSerializer.DeserializeEnvelope` übersetzt die Abbildungsfehler des `XmlSerializer`
> (wohlgeformtes, aber nicht schemakonformes Client-XML) in `EbicsEnvelopeFormatException` → `091010`,
> statt sie als blankes `InvalidOperationException` auf `061099` durchfallen zu lassen. Der Mapper
> musste dafür **nicht** aufgeweicht werden — nur die Stelle, die weiß, wessen Bytes das sind, trifft
> die Zuordnung ([ADR-0029](../adr/0029-interop-fixes-reale-clients.md)).

## EBICS-Versionsbezug

Der Katalog ist versionsübergreifend (H003/H004/H005) — die Codes selbst sind identisch. Nur die
Ablage im typisierten `ebicsResponse`/`ebicsKeyManagementResponse` erfolgt über die je Version
committeten Schema-Bindings (`EbicsResponseFactory`). Die Antwort-Art (plain `ebicsResponse` vs.
`ebicsKeyManagementResponse`) hängt an der Request-Art, nicht am Code.

## Tests

- `tests/EBICO.Tests/Core/ReturnCodes/EbicsReturnCodeTests.cs` — Value-Object: `OkCode`, Felder,
  Wertgleichheit des `record struct`.
- `tests/EBICO.Tests/Core/ReturnCodes/EbicsReturnCodesTests.cs` — Registry: `All` (6-stellig,
  eindeutig, benannt), `Get`/`TryFromCode`, `IsSuccess`, Known-Answer-Werte gegen Annex 1 (nicht nur
  Selbstkonsistenz).
- `tests/EBICO.Tests/Server/EbicsErrorMapperTests.cs` — Exception→Code für alle Gruppen inkl.
  Fallback → `061099` und `null`-Wurf.
- Fehlerpfade end-to-end über die Pipeline (defekte Order-Data → `090004`, unbekannter/falscher
  Zustand → `091002`) sind in den Handler-Tests abgedeckt (`Server/IniOrderHandlerTests.cs` u. a.,
  via `ServerTestHelpers.ReadReturnCodes`).

Tests sind Tier A (CI-sicher, ohne proprietäre Beispiele).

## Verwandtes

- [Hostable Server-Grundgerüst](../server/host.md) — Pipeline, Response-Erzeugung, HTTP-Statusabbildung
- [ADR-0012 — Returncode-Katalog](../adr/0012-returncode-katalog.md)
- [ADR-0007 — Domänen-Value-Objects als `readonly record struct`](../adr/0007-domaenen-value-objects-record-struct.md)
- [Connector-Architektur](../connector/architecture.md) — `EbicsResult<T>` nutzt `EbicsReturnCode.OkCode`

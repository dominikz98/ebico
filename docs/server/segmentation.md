# Server: Segmentierung, Kompression & Base64-Pipeline

> Umsetzung von **Issue #34** (Milestone M4 — Server: Transaction Engine). Diese Seite
> beschreibt die **Byte-Pipeline** für Order-Data: das Aufteilen des komprimierten (und ggf.
> verschlüsselten) Bytestroms in **Segmente** und das deterministische **Reassemblieren** beim
> Empfang, plus die **konfigurierbare Segmentgröße**.
>
> Bewusst **enthalten**: die reine, wiederverwendbare Segmentierungs-Primitive
> `EbicsSegmentation` (`Split`/`Reassemble`) in `EBICO.Core`, die konfigurierbare Segmentgröße
> (`EbicoServerOptions.SegmentSizeBytes`), Grenzfälle (1 Segment, leeres OrderData), Determinismus.
> Kompression wird über das vorhandene `EbicsCompression` **wiederverwendet**, Base64 erledigt die
> `base64Binary`-Bindung pro Segment.
> Bewusst **noch nicht**: die Transaktions-Zustandsmaschine — Transaction-ID, `DataTransfer`-
> Envelope, Phasen (Initialisation/Transfer/Receipt), das Header-Mapping von
> `NumSegments`/`SegmentNumber`/`lastSegment` und das Auslösen der Segment-Returncodes. Das
> **komponiert und verdrahtet** die Upload- (#32) und Download-Transaktion (#33), die diese
> Primitive als Baustein nutzen. Kein Convenience-Orchestrator.

## Zweck

Auf der Leitung trägt jede Nachricht ihre Order-Data als `base64(compress(orderDataXml))` bzw. —
bei gesicherten Transaktionen — als `base64(encrypt(compress(orderDataXml)))`. Sobald dieser
Bytestrom größer als eine Segmentgröße wird, wird er über **mehrere Nachrichten** ausgeliefert, je
Nachricht ein `DataTransfer/OrderData`-Element. #34 liefert das **Wie** dieser Aufbereitung
(compress → base64 → split bzw. reassemble → base64-decode → decompress) als querschnittlichen
Layer; das **Wann/Wer** (Phasen, Transaction-ID, Envelope) liegt in der Transaktions-Zustandsmaschine
(#32/#33).

Die Primitive ist bewusst **rein und policy-frei**: `Split` ist ein deterministischer Byte-Splitter
und erzwingt **keine** Maximalanzahl Segmente / Maximalgröße — das ist Sache der Transaktions-Engine
(analog zur policy-freien Haltung von [`EncryptionE002`](../protocol/encryption-e002.md), die
ebenfalls keine Protokoll-Erlaubnis prüft). Kompression existiert bereits (Issue #47,
`EbicsCompression`); #34 baut darauf auf, statt sie zu ersetzen.

## Ablauf

Beide Richtungen komponieren dieselben Primitive; `EbicsSegmentation` ist die neue Naht.

**Senderichtung (Server → Client, Download):**

| Schritt | Aktion |
| --- | --- |
| 1. Serialisieren | Order-Data-XML → `byte[]` (`EbicsXmlSerializer`) |
| 2. Komprimieren | `EbicsCompression.Compress` (zlib) |
| 3. (optional) Verschlüsseln | `EncryptionE002.Encrypt` → `EncryptedOrderDataBytes` (nur gesicherte Aufträge) |
| 4. Segmentieren | `EbicsSegmentation.Split(payload, options.SegmentSizeBytes)` → `SegmentedOrderData` (`Segments`, `NumSegments`) |
| 5. Base64 + Envelope | je Segment ein `DataTransfer/OrderData` (`byte[]` → base64 durch die `base64Binary`-Bindung); `NumSegments`/`SegmentNumber`/`lastSegment` setzt die Transaktionsschicht (#33) |

**Empfangsrichtung (Client → Server, Upload):**

| Schritt | Aktion |
| --- | --- |
| 1. Base64-decode | je `OrderData`-Element `byte[]` (durch die `base64Binary`-Bindung) |
| 2. Reassemblieren | die **geordneten** Segmente → `EbicsSegmentation.Reassemble(segments)` |
| 3. (optional) Entschlüsseln | `EncryptionE002.Decrypt` (nur gesicherte Aufträge) |
| 4. Dekomprimieren | `EbicsCompression.Decompress` |
| 5. Deserialisieren | `byte[]` → Order-Data-XML |

`Reassemble` konkateniert in **Listenreihenfolge** — es sortiert **nicht** nach `SegmentNumber` und
erkennt **keine** Lücken/Duplikate. Die Sequenz-Integrität (alle `NumSegments` da, korrekte
Reihenfolge, `lastSegment` gesehen) stellt die Transaktions-Engine sicher, die die geordnete Liste
**vor** dem Aufruf baut. So bleibt die Primitive rein und deterministisch.

## Segmentgröße

`EbicoServerOptions.SegmentSizeBytes` (Default **512 KiB**) begrenzt die **Roh-Bytes vor Base64** je
Segment. `EbicsSegmentation.Split` nimmt diesen Wert als `int`-Parameter (`EBICO.Core` liest die
Server-Optionen nicht — der Server reicht den Wert durch).

Base64 bläht um Faktor **4/3** auf (`base64(N) = 4·⌈N/3⌉`):

| Roh-Segmentgröße | Base64-Drahtgröße | Verhältnis zu `MaxRequestBodyBytes` (1 MiB) |
| --- | --- | --- |
| 512 KiB (Default) | ≈ 683 KiB | ~341 KiB Reserve fürs Envelope |
| 768 KiB | = exakt 1 MiB | keine Reserve (theoretisches Maximum) |

Der Default 512 KiB lässt bewusst Reserve für Header, `AuthSignature` und die `<OrderData>`-Tags, die
alle in denselben HTTP-Body zählen. Determinismus: gleiche Eingabe + gleiche Größe → **byte-identische**
Segmente (`NumSegments = ⌈payload.Length / SegmentSizeBytes⌉`, feste sequentielle Slices).

## Returncodes & Grenzfälle

Die Primitive wirft nur **Argument-Form-Fehler** (BCL, keine eigene Exception-Klasse):

| Situation | Verhalten |
| --- | --- |
| leeres OrderData (0 Bytes) | **1 leeres Segment** (`NumSegments = 1`), kein Fehler; `Reassemble` gibt `[]` zurück |
| genau 1 Segment (`0 < len ≤ Größe`) | 1 Segment == Payload, `NumSegments = 1` |
| `maxSegmentSizeBytes ≤ 0` | `ArgumentOutOfRangeException` |
| `segments` `null` / Element `null` | `ArgumentNullException` |
| `segments` leer (`Count == 0`) | `ArgumentException` (eine gültige Transaktion hat ≥ 1 Segment) |

Die **fachlichen** Segment-Returncodes sind im zentralen Katalog bereits definiert, werden aber von
#34 **nicht** ausgelöst — das übernimmt die Transaktions-Engine (#32/#33), die die Policy kennt:
`EBICS_SEGMENT_SIZE_EXCEEDED` (091009), `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` (091104),
`EBICS_MAX_ORDER_DATA_SIZE_EXCEEDED` (091113), `EBICS_MAX_SEGMENTS_EXCEEDED` (091114). Siehe
[Returncode-Katalog](../protocol/return-codes.md).

### ⚠️ Spec-Vorbehalte

- **Roh- vs. Base64-Bezug der Größe:** `SegmentSizeBytes` zählt Roh-Bytes vor Base64; die Drahtgröße
  ist ≈ 4/3 davon. Ob EBICS seine ~1-MB-Segmentgrenze auf die Roh- oder die base64-kodierte Größe
  bezieht, ist gegen den offiziellen EBICS-Annex zu verifizieren. Die Wahl (Roh-Bytes) ist auf den
  Größenparameter / `SegmentSizeBytes` begrenzt.
- **Base64-Framing:** #34 modelliert jedes Segment als **eigenständig base64-kodiertes `byte[]`** (so
  wie die `base64Binary`-Bindung), nicht als Ausschnitt eines **geteilten** base64-Stroms. Welche der
  beiden Lesarten der Annex meint, ist ebenfalls zu verifizieren; `Split`/`Reassemble`-Round-Trips
  halten unabhängig davon.
- **Kompressions-Framing** (geerbt von `EbicsCompression`, Issue #47): zlib (RFC 1950) vs. raw DEFLATE
  vs. gzip ist nicht gegen den Annex verifiziert.

## EBICS-Versionsbezug

Die Segment-Header-Felder existieren als Bindings pro Version (`EBICO.Core.Schema.<H00x>`), das
Mapping erfolgt in der Transaktionsschicht — die Byte-Primitive ist versionsagnostisch:

| Feld | Upload/Request | Download/Response |
| --- | --- | --- |
| Anzahl Segmente | `StaticHeaderType.NumSegments` (`ulong?`) | `ResponseStaticHeaderType.NumSegments` (nur Initialisation-Phase) |
| Segmentnummer | `MutableHeaderTypeSegmentNumber` (`Value` + `lastSegment`) | `ResponseMutableHeaderTypeSegmentNumber` (`Value` + `lastSegment`) |
| Phase | `TransactionPhaseType` (`Initialisation`/`Transfer`/`Receipt`) | dito |

Die Wire-Felder sind `ulong`; in-memory ist `int` natürlich (ein `byte[][]` hat ≤ `int.MaxValue`
Elemente). Der Cast `int → ulong` erfolgt erst beim Header-Mapping in #32/#33.

## Tests

`tests/EBICO.Tests/Serialization/EbicsSegmentationTests.cs` (xUnit v3 + AwesomeAssertions):

- **Happy Path:** exaktes Vielfaches → gleich große Segmente; mit Rest → letztes Segment kürzer;
  kleiner als Größe → 1 Segment.
- **Grenzfälle:** leerer Input → 1 leeres Segment; `Split` mit Größe `0`/`-1` → `ArgumentOutOfRangeException`;
  `Reassemble` `null`/leere Liste/Null-Element → wirft; `Reassemble([[]])` → `[]`.
- **Determinismus:** zweimal splitten → byte-gleich. Reihenfolge-Test `[a][b][c]` → `abc` (fängt
  versehentliches Sortieren).
- **Round-Trip / Known-Answer:** `[Theory]` über Längen `{0, 1, size-1, size, size+1, 3·size, 100000}`
  × Größen `{1, 16, 1024, 512 KiB}` → `Reassemble(Split(x, s).Segments) == x`; fixe Segmentgrenzen als
  Known-Answer-Vektor.
- **End-to-end mit `EbicsCompression`:** `Decompress(Reassemble(Split(Compress(data), 64).Segments)) == data`
  (kleine Segmentgröße erzwingt mehrere Segmente) — belegt deterministische Reassemblierung real, nicht
  nur Selbstkonsistenz einer Ebene.

## Verwandte Doku

- [Hostable Server-Grundgerüst](host.md) — Pipeline, Returncodes, `MaxRequestBodyBytes`
- [Verschlüsselung E002](../protocol/encryption-e002.md) — der optionale Verschlüsselungsschritt der Pipeline
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — die (noch ungenutzten) Segment-Returncodes
- [Upload-Transaktion](upload-transaction.md) — Empfangsrichtung: `Reassemble` verdrahtet (#32)
- [Download-Transaktion](download-transaction.md) — Senderichtung: `Split` verdrahtet (#33)
- [Connector-Architektur](../connector/architecture.md) — Send-Pipeline & Transaktions-Skelett (Upload/Download)

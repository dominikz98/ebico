# 0012 — EBICS-Returncode-Katalog (Modellierung & Verortung)

- Status: accepted
- Datum: 2026-07-13

## Kontext

EBICS-Antworten tragen einen sechsstelligen Returncode (technisch im
`header/mutable/ReturnCode`, fachlich im `body/ReturnCode`). Bis Issue #36 (M4) existierten dafür
zwei **bewusst vorläufige** Modelle: ein server-lokaler `EbicsReturnCode` mit neun Codes
(Grundgerüst #25) und ein `EbicsResult<T>` im Connector (#46). Beide verwiesen im Code auf #36 als
den Ort, an dem der **zentrale, vollständige** Katalog entsteht. Der ADR-Backlog führte
„Returncode-Modellierung (`EbicsResult<T>` vs. Exceptions, Katalog)" als offene Entscheidung.

Zu klären waren: (1) Wo lebt der Katalog? (2) Wie wird er modelliert? (3) Wie werden Exceptions
darauf abgebildet? (4) Wie ist mit den proprietären EBICS-Annexen umzugehen?

Randbedingung: `EBICO.Core` darf nicht auf `EBICO.Server` referenzieren (Projektabhängigkeiten
Connector→Core, Server→Core). Das Mapping muss aber server-seitige Exceptions
(`EBICO.Server.State.MasterData*`) kennen.

## Entscheidung

- **Katalog nach `EBICO.Core.ReturnCodes`** (geteilte Primitive für Server **und** Connector):
  - `EbicsReturnCode` als `public readonly record struct` (`Code`, `SymbolicName`, `Kind`) mit
    statischen Feldern je Code und `const OkCode` — Muster wie [ADR-0007](0007-domaenen-value-objects-record-struct.md);
  - `EbicsReturnCodeKind` (Enum `Technical`/`Business`) steuert Header- vs. Body-Ablage;
  - `EbicsReturnCodes` als Registry (`All`/`Get`/`TryFromCode`/`IsSuccess`) — Vorbild
    `Crypto/KeyVersions`.
- **Mapping bleibt server-seitig** (`EBICO.Server.ReturnCodes.EbicsErrorMapper` +
  `IEbicsErrorMapper`), weil es Server-Exceptions kennt und reine Request-Verarbeitung ist; der
  Connector braucht es nicht (eigenes Exception-/`EbicsResult`-Modell). Der Katalog (Core) ist die
  zentrale Primitive; das Mapping ist server-lokal.
- **Zentrale, eindeutige Exception→Code-Abbildung:** Handler machen Order-Data-Fehler über
  `OrderDataFault.Wrap` als dedizierte `EbicsOrderDataException` sichtbar; der Mapper bildet diesen
  Typ (und die Low-Level-Crypto-/Format-Fehler) auf `090004`, die Domain-/MasterData-Fehler auf
  `091002` ab. Blanke `ArgumentException`/`InvalidOperationException` mappen bewusst auf `061099`
  (Serverfehler), nicht auf einen fachlichen Code.
- **Umgang mit der Spec:** Codes und symbolische Namen sind Interface-Konstanten und werden
  aufgenommen; Beschreibungen sind in eigenen Worten formuliert (kein Kopieren des Annex-Texts).
  Über die neun verifizierten Codes hinausgehende Einträge tragen `⚠️ Spec-Vorbehalt` und sind
  gegen die offiziellen Annexe zu verifizieren.

## Konsequenzen

- Ein Ort für alle Returncodes; `EbicsResult.OkReturnCode` bezieht sich auf `EbicsReturnCode.OkCode`
  statt das Literal `"000000"` zu duplizieren.
- Die frisch gemergten M3-Handler wurden entkoppelt: die duplizierten `try/catch`-Blöcke (Guard-Liste
  einmal in `OrderDataFault`) sind weg; Verhalten unverändert (Order-Data → `090004`,
  Identifier/State → `091002`), abgesichert durch die bestehenden Pipeline-Tests.
- Der Katalog ist absichtlich umfassender als der laufende Code; unbenutzte Codes sind als
  Konstanten vorhanden (z. B. TX-Codes für die M4-Transaction-Engine) und klar als unverifiziert
  markiert.
- Doku: [protocol/return-codes.md](../protocol/return-codes.md).

## Alternativen

- **Katalog in `EBICO.Server` belassen und nur erweitern:** minimaler Eingriff, aber nicht wirklich
  „zentral" (der Connector behielte sein eigenes Modell) — verworfen.
- **Auch den Mapper nach `EBICO.Core` ziehen:** scheitert an der Abhängigkeitsrichtung (Core dürfte
  die Server-`MasterData*`-Exceptions nicht sehen); hätte das Verschieben dieser Exceptions nach
  Core erzwungen — unnötig große Streuung, verworfen.
- **Handler-`try/catch` unverändert lassen, nur den Mapper erweitern:** ließe die duplizierte
  Guard-Liste stehen — verworfen zugunsten der einmaligen `OrderDataFault`-Kapselung.
- **Kompletten Annex-1-Text mitcommitten:** lizenzrechtlich heikel (proprietär) — verworfen; nur
  Codes/Namen als Konstanten, eigene Kurzbeschreibungen.

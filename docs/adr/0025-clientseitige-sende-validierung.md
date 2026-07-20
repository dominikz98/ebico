# 0025 — Clientseitige Sende-Validierung (Berechtigung/BTF) im Connector

- Status: accepted
- Datum: 2026-07-20

## Kontext

Die Send-Pipeline des `EBICO.Connector` (siehe [Connector-Architektur](../connector/architecture.md))
sieht als **Stufe 1** eine Validierung *„Berechtigung, BTF"* vor. Sie war als Einzige noch nicht als
eigener Baustein umgesetzt: die Prüfungen lagen verstreut und liefen *spät* in den Executoren (`UploadExecutor`/
`DownloadExecutor`) — leere Payload, Segmentgröße und die Order-Identitäts-Auflösung (`NormalizeOrderIdentity`)
wurden erst **nach** dem Laden der Schlüssel erreicht, und eine Berechtigungsprüfung gab es clientseitig gar
nicht (jede Abweisung kostete einen Server-Roundtrip). Issue #44 (Connector-Epic, M6) schließt diese Lücke.

Zu entscheiden war: (a) wo die Stufe verankert wird, (b) wie Fehler ausgedrückt werden, (c) ob und wie eine
clientseitige „Berechtigung" konfiguriert wird — und in welchem Verhältnis das zur **serverseitigen**
Berechtigungsprüfung aus [ADR-0016](0016-btf-framework-und-berechtigung.md) steht.

## Entscheidung

1. **Statischer Helfer `RequestValidator` (`EBICO.Connector.Validation`), kein DI-Service.** Er wird als
   erste Anweisung in `UploadExecutor.ExecuteAsync`/`DownloadExecutor.ExecuteAsync` aufgerufen — vor jeder
   Key-I/O, Krypto, Serialisierung und Transport. Bewusst dasselbe Muster wie serverseitig
   ([ADR-0016](0016-btf-framework-und-berechtigung.md), Punkt 4: statische Prüf-Logik statt
   `IOrderAuthorizationService`) und konsistent mit den vorhandenen statischen Helfern (`UploadSupport`,
   `EncryptionE002`). Die Executoren sind der einzige Choke-Point für den generischen **und** alle
   Convenience-Handler; das vermeidet Duplikation über die Handler-Typen. Die früher verstreuten Checks und
   die `NormalizeOrderIdentity`-Logik wurden dorthin **verschoben** (nicht dupliziert) — der Validator ist
   damit die alleinige Autorität für Order-Identität + Header-Tupel.

2. **Asymmetrische Fehlersemantik.** Struktur-/BTF-Verstöße (Order-Identität nicht auflösbar, im Katalog
   bekannter Code in falscher Richtung, leere Upload-Payload, nicht-positive Segmentgröße) sind Programmier-/
   Konfigfehler → **`EbicsConfigurationException`** (konsistent mit dem bisherigen `NormalizeOrderIdentity`).
   Eine Berechtigungs-Verweigerung ist ein fachliches Ergebnis → **`EbicsResult<T>.Failure("090003", …)`**
   (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`), exakt der Code, den die Bank zurückgäbe. Der Validator wirft
   Struktur-Fehler direkt und liefert sonst ein kleines Outcome (`RequestValidation<TIdentity>`), das der
   Executor in `Failure` bzw. die weiterverwendete Identität übersetzt.

3. **Opt-in Allow-List, Default aus.** `EbicsConnectionOptions.AllowedOrderTypes` (normalisiert zu einem
   Ordinal-`IReadOnlySet<string>` auf `EbicsConnection`) listet die erlaubten **klassischen** OrderType-Codes.
   Ist sie gesetzt, weist der Connector einen Request mit nicht gelistetem **effektiven klassischen** Code
   lokal ab (fail-fast, kein Roundtrip). Der Schlüssel ist der effektive klassische Code — konsistent mit
   ADR-0016 (H005 `CCT` matcht `"CCT"`, nicht den Draht-Code `"BTU"`); administrative Codes (HTD/…) unterliegen
   der Liste ebenfalls. Eine **leere** Liste (Default) überspringt die Prüfung.

4. **Bewusste Divergenz zur Server-Seite.** Der Server erzwingt **strikt** und lehnt „leere Berechtigungsmenge
   = alles erlaubt" ausdrücklich ab (ADR-0016, Punkt 3), weil er echtes Bankverhalten abbildet. Der Client ist
   umgekehrt **opt-in/lenient by default**: er kennt die Teilnehmer-Berechtigungen nicht von sich aus, und die
   Bank bleibt die Autorität. Die Allow-List ist eine reine Vorab-Optimierung (Roundtrip sparen, klarer
   Fehler), keine Durchsetzungsinstanz.

## Konsequenzen

- Malformte oder (opt-in) unautorisierte Requests scheitern **vor** jeder Key-I/O/Krypto/Transport — schneller
  und ohne Nebenwirkungen. Bestehende E2E-/Tier-A-Tests bleiben unverändert grün (Allow-List Default leer;
  serverseitige 090003-Abweisung weiterhin separat E2E-abgedeckt).
- Onboarding (INI/HIA/HPB) läuft nicht über die Executoren und wird daher nie hier validiert — die Allow-List
  kann Onboarding nicht blockieren.
- Die Divergenz (strikt server-seitig vs. opt-in client-seitig) ist dokumentiert; wer clientseitige
  Absicherung will, opted per Konfiguration ein.
- `AllowedOrderTypes` ist als get-only, initialisierte Collection modelliert (Options-Konvention; vermeidet
  CA2227/CA1819 unter `TreatWarningsAsErrors`).

## Alternativen

- **DI-Service `IEbicsRequestValidator`** — verworfen: keine Laufzeit-Substituierbarkeit nötig, unnötige
  Kopplung/Ctor-Ripple (dieselbe Begründung wie ADR-0016).
- **Validierung in den Handlern** statt in den Executoren — verworfen: Duplikation über alle Convenience-
  Handler und ohne die Identitäts-Auflösung (BTU/FUL/BTD/FDL).
- **Striktes Enforcement wie server-seitig** (leere Liste = nichts erlaubt) — verworfen: der Client ist nicht
  die Autorisierungsautorität und darf ohne explizite Konfiguration nichts blockieren.
- **`SubscriberPermission`-Liste statt String-Codes** — verworfen: SignatureClass hat clientseitig keine
  Durchsetzungsbedeutung; String-Codes binden sauber aus der Konfiguration und sind direkt mit dem
  effektiven Auflösungs-Output vergleichbar.

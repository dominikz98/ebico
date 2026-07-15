# 0018 — Kontoauszug-/Report-Download-Orders (synthetische Generierung, camt.05x.001.08, ZIP-Container)

- Status: accepted
- Datum: 2026-07-15

## Kontext

Issue #40 (Milestone M5) fordert serverseitige **Download-Orders für Kontoauszüge & Reports**: STA (MT940),
VMK (MT942), C53 (camt.053), C52 (camt.052), C54 (camt.054) — mit **serverseitig generierbaren Testdaten**
und **Zeitraum-Filter**. Die generische [Download-Transaktion](../server/download-transaction.md) (#33)
existiert bereits, wertet aber weder den fachlichen Order-Typ noch die Order-Parameter aus; sie liefert nur
vorab eingestellte Roh-Payloads. Es gibt **kein** Konten-/Buchungs-Domänenmodell und **keine**
camt/MT940-XSD-Bindings (die ISO/SWIFT-Schemas sind nicht Teil des Repos, ADR-0003/0006).

Zu entscheiden war: (1) woher die Auszugsdaten stammen, (2) welche camt-Nachrichtenversion erzeugt wird,
(3) ob der BTF-`Container=Zip` als echtes ZIP umgesetzt wird, und (4) wie die Generierung in die
Download-Engine eingehängt wird.

## Entscheidung

1. **Synthetischer, deterministischer Generator** (`SyntheticStatementGenerator`): Konto (gültige DE-IBAN
   mit ISO-7064-Prüfziffern), Salden und Buchungen werden reproduzierbar aus dem Teilnehmer-Tripel
   (Host/Partner/User) + Zeitraum erzeugt (stabiler FNV-1a-Seed, kein `DateTime.Now`, kein
   `string.GetHashCode()`). Kein neues Konten-Stammdatenmodell. Admin-seedbare Roh-Payloads bleiben parallel
   möglich und haben **Vorrang** vor der Generierung.

2. **Feste camt-Version `camt.05x.001.08`** (moderne ISO/CGI-MP-Variante, strukturiertes
   `<Sts><Cd>BOOK</Cd></Sts>`), analog zum fest gewählten `pain.002.001.03` in ADR-0017. Die Version ist je
   Builder eine einzelne Konstante.

3. **Echter ZIP-Container** (`StatementZipContainer` über `System.IO.Compression.ZipArchive`), da die
   BTF-Einträge `Container=Zip` deklarieren. Für byte-stabile Ausgabe wird der Entry-Zeitstempel fixiert und
   die Kompressionsstufe gepinnt. Die Download-Engine komprimiert (zlib) und verschlüsselt (E002) darauf —
   entspricht der realen Schichtung `base64(E002(zlib(zip(dokument))))`.

4. **Generate-on-Demand über `IDownloadOrderProcessor`** (Default `StatementDownloadProcessor`), das
   Download-Gegenstück zu `IUploadOrderProcessor` (#39). Die Engine entnimmt zuerst nach dem **aufgelösten**
   Order-Typ, dann (rückwärtskompatibel) nach dem rohen `FDL`/`BTD`, dann generiert sie. Die Auflösung
   erfolgt zentral über `BtfOrderTypeCatalog.ResolveDownloadOrderType` (BTF → FileFormat → direkt); der
   fehlende **VMK/mt942**-Katalogeintrag wurde ergänzt. Die Formaterzeugung liegt vollständig in
   `EBICO.Core` (`StatementContentFactory`), der Server ruft nur diesen einen Seam.

## Konsequenzen

- Der Emulator liefert für alle fünf Order-Typen ohne Vorbereitung plausible, zeitraum-gefilterte Auszüge;
  Tests sind durch die Determinismus-Garantie ohne proprietäre Fixtures möglich.
- Das Umstellen des Entnahme-Schlüssels vom rohen `FDL`/`BTD` auf den aufgelösten Code ist durch die
  Kompat-Probe **strikt additiv** — bestehende #33-Tests bleiben unverändert grün.
- Die synthetischen Formate sind minimal und gegen die offiziellen Annexe/XSDs **ungeprüft** (dokumentierte
  Spec-Vorbehalte). Ein echtes Konten-Stammdatenmodell, das DK-Profil `.02` und das PSR/pain.002-Download-
  Mapping (#39) bleiben Folgeschritte.

## Alternativen

- **Konten-/Buchungs-Stammdatenmodell** (neues Aggregat, Admin-API, Store, Suite-UI) — deutlich größerer
  Umfang, für „generierbare Testdaten" nicht nötig; verworfen zugunsten des synthetischen Generators.
- **Kein ZIP** (Dokument direkt der zlib-Kompression der Engine überlassen) — einfacher, weicht aber vom
  deklarierten `Container=Zip` ab; verworfen zugunsten des echten ZIP.
- **Provider-Dekorator** statt eigener Processor-Abstraktion — hätte den `IDownloadDataProvider` (auch von
  Admin-Seeding/Re-Enqueue genutzt) mit einem Zeitraum-Parameter belastet; verworfen zugunsten der
  separaten, pluggbaren `IDownloadOrderProcessor`-Abstraktion.

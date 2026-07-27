# 0030 — Abgestimmte Transport-Defaults, konsistente Returncode-Texte und clientseitige VEU-Anbindung

- Status: accepted
- Datum: 2026-07-27

## Kontext

Ein explorativer End-to-End-Test des Gesamtstands (**#124**) hat den laufenden Emulator nicht gegen die
Testsuite, sondern gegen die **Doku** geprüft: Server und Suite per `dotnet run` gestartet, den Quickstart
gefahren und einen eigenen Connector-Client gegen den *separat laufenden* Server gerichtet — also den Pfad
aus [Erste Schritte](../getting-started.md), Schritt 1 + 2. Der Kern trug (Onboarding, Upload, Download in
allen drei Versionen), aber drei Befunde ließen sich mit der Testsuite bauartbedingt nicht sehen:

1. **Uploads ab 768 KiB waren mit den ausgelieferten Defaults unmöglich.** Der Connector segmentierte mit
   `768 KiB` roh — der theoretischen Obergrenze, deren Base64-Form *exakt* 1 MiB ergibt — während der Server
   `MaxRequestBodyBytes = 1 MiB` akzeptiert. Das Segment allein füllte das Limit; der Envelope kam obendrauf.
   Jeder Upload, dessen komprimierte und verschlüsselte Auftragsdaten ein volles Segment füllten, starb mit
   **HTTP 413**, bevor der Server antworten konnte. Beide Defaults waren einzeln getestet, nie gemeinsam:
   `UploadE2ETests` prüfte explizit `NumSegments == 1`.
2. **Fachliche Fehler meldeten `EBICS_OK` als Report-Text.** Der Returncode wurde korrekt aus dem
   nicht-OK-Slot gelesen (bei fachlichen Fehlern also dem Body), der Text dagegen immer aus dem Header — der
   in genau diesem Fall `000000`/`EBICS_OK` trägt. Aufrufer sahen `090005: EBICS_OK`.
3. **Der VEU-Workflow war vom Connector aus nicht fahrbar**, obwohl die
   [Abdeckungsmatrix](../server/order-coverage-matrix.md) HVU–HVS für alle Versionen als ✅ führt. Drei
   Lücken griffen ineinander: H005-Uploads verlangten einen BTF (den administrative Orders nicht haben),
   `UploadRequest` konnte keine `OrderID` transportieren, und das `OrderAttribute` war hart auf `DZHNN`
   verdrahtet — es ließ sich also nicht einmal ein Auftrag parken.

Befund 3 ist die eigentliche Lehre: Die Matrix beschreibt den **Server**. Aus Anwendersicht ist eine
Auftragsart erst verfügbar, wenn der mitgelieferte Client sie auch senden kann.

## Entscheidung

**1. Ein geteilter Segment-Default in `EBICO.Core`.** `EbicsSegmentation.DefaultSegmentSizeBytes` (512 KiB)
ist die eine Zahl, auf die sich `EbicoServerOptions.SegmentSizeBytes` **und** die Upload-Pipeline des
Connectors beziehen. Der Wert lässt einer 1-MiB-Anfrage ~341 KiB Luft für den Envelope. Beide Seiten sind
damit **per Konstruktion** kompatibel statt per Zufall.

**2. Die Beziehung wird als Test festgeschrieben, nicht die Zahlen.**
`SegmentSizeCompatibilityTests` prüft `Base64Length(SegmentSizeBytes) + Envelope-Reserve ≤ MaxRequestBodyBytes`
und hält den historischen 768-KiB-Default als Negativbeispiel fest. Dazu
`EbicsSegmentation.MaxSegmentSizeForRequestBody(…)`, mit dem sich für ein abweichendes Body-Limit eine sichere
Segmentgröße *ableiten* statt raten lässt. `EbicsSegmentation.Split` bleibt policy-frei — der Default ist eine
Konstante daneben, keine Vorgabe im Splitter.

**3. Ein E2E-Upload über mehrere Segmente gehört zur Standardmatrix.**
`CctUpload_LargerThanOneSegment_RoundTripsWithTheShippedDefaults` fährt je H003/H004/H005 mit den
ausgelieferten Defaults. Die Nutzlast ist bewusst **inkompressibel** (base64-Rauschen in den Creditor-Namen):
ein normales pain.001 deflatiert auch bei zehn Megabyte auf ein einziges Segment — genau deshalb blieb die
Lücke unentdeckt.

**4. Code und Report-Text werden gemeinsam aufgelöst.**
`EbicsReturnCodes.CombineOutcome(headerCode, headerText, bodyCode)` liefert ein `EbicsResponseOutcome`
(Code + Text). Gewinnt der Body, kommt der Text aus der Registry (`SymbolicName`), **nie** aus dem Header.
Die Funktion liegt in Core statt doppelt in den beiden Connector-Basisklassen; die View-Records nehmen sie
über einen zusätzlichen Konstruktor entgegen, sodass die 15 Parse-Stellen unverändert lesbar bleiben.

**5. Der H005-Upload-Pfad behandelt administrative Order-Typen wie der Download-Pfad.** Löst sich ein
Order-Typ nicht auf einen BTF auf, wird er als `AdminOrderType` gesendet statt clientseitig abgelehnt. Das
ist keine Aufweichung, sondern die Beseitigung einer **Asymmetrie**: der Download-Pfad macht das seit jeher
(sonst wären HTD/HKD/HAA/HPD/HAC/PTK auf H005 nie erreichbar gewesen).

**6. VEU wird als eigene Order-Familie im Connector modelliert.** Neu: `VeuOrderReference` (OrderID plus
Identität des referenzierten Auftrags), `UploadRequest.DistributedSignature` (Park-Trigger: H005
`SignatureFlag`, H003/H004 `OrderAttribute=OZHNN`), `UploadRequest.Veu`/`DownloadRequest.Veu` sowie
Convenience-Requests `Hvu`/`Hvz`/`Hvd`/`Hvt`/`Hve`/`Hvs` nach dem Muster der übrigen Familien. Fehlt die
Referenz bei HVE/HVS/HVD/HVT, schlägt der Aufruf **clientseitig** fehl — mit einer Meldung, die sagt was
fehlt, statt mit dem generischen `091121` der Bank.

**7. Die Bank-Fingerprints bekommen einen Admin-Endpunkt.** `GET /admin/banks/{hostId}/keys` liefert
Fingerprint (Hex + Briefformat), Version, Schlüssellänge und das öffentliche PEM — das Emulator-Äquivalent
des Bankbriefs. Ohne ihn hat ein Client gegen einen separat gehosteten Emulator keinen Kanal außerhalb von
HPB, gegen den er die Fingerprints prüfen könnte; `HpbResult.FingerprintsVerified` konnte dort nie `true`
werden. Nur öffentliche Bestandteile werden exponiert.

**8. Die Suite kennzeichnet ihren Datenbestand.** Ein `DemoDataBanner` im Layout sagt, dass die Oberfläche
auf eigenem In-Memory-Zustand arbeitet und **nicht** mit einem separat laufenden Server verbunden ist. Die
Trennung ist seit [ADR-0009](0009-blazor-render-mode.md)/[ADR-0015](0015-ereignis-protokollspeicher.md)
gewollt und dokumentiert — sie war nur in der Oberfläche selbst unsichtbar, wo geseedete Transaktionen wie
Live-Daten aussahen.

**9. Der SDK-Pin nennt die niedrigste taugliche Version.** `global.json` pinnt `10.0.100` statt `10.0.300`
(je `rollForward: latestFeature`). `latestFeature` rollt nur **aufwärts**: der hohe Pin machte das Repo auf
jeder Maschine mit einem SDK 10.0.2xx unbaubar, während die CI unauffällig grün blieb, weil
`actions/setup-dotnet` die gepinnte Version herunterlädt.

## Konsequenzen

- **Große Uploads funktionieren mit den Defaults.** Wer die Segmentgröße bewusst anhebt, muss
  `MaxRequestBodyBytes` mitziehen; beide XML-Doc-Kommentare sagen das jetzt explizit und verweisen auf
  `MaxSegmentSizeForRequestBody`.
- **Verhaltensänderung im H005-Upload:** ein Order-Typ ohne BTF-Mapping ist kein clientseitiger Fehler mehr,
  sondern geht als `AdminOrderType` an die Bank (die ihn mit `091006` ablehnt, wenn sie ihn nicht kennt). Der
  Test `H005_upload_with_an_unmapped_order_type_and_no_btf_throws` wurde entsprechend umgedreht. Vertretbar,
  weil der BTF-Katalog ein ausdrücklich **best-effort** Seed ist ([ADR-0016](0016-btf-framework-und-berechtigung.md))
  und damit kein verlässliches „gibt es das?"-Orakel. Ohne Order-Typ *und* ohne BTF wirft es weiterhin.
- **`EbicsResult.ReturnText` kann jetzt `null` sein**, wo vorher fälschlich `"EBICS_OK"` stand — nämlich bei
  einem fachlichen Code, den der Katalog nicht kennt. Ein widerspruchsfreies `null` ist einer irreführenden
  Erfolgsmeldung vorzuziehen.
- **Die Coverage-Matrix trennt jetzt Server- und Client-Verfügbarkeit.** VEU steht auf beiden Seiten auf ✅;
  die Matrix hat dafür eine eigene Spalte bekommen, damit dieselbe Lücke nicht erneut unsichtbar entsteht.
- **Spec-Vorbehalte bleiben.** Die VEU-Order-Params tragen neben der OrderID die Identität des
  referenzierten Auftrags (PartnerID + OrderType bzw. Service); der Emulator schlüsselt seinen VEU-Speicher
  allein über die OrderID und ignoriert den Rest. Gegen eine reale Bank ist das ungeprüft. Ebenso ungeprüft:
  dass `OZHNN` bzw. `SignatureFlag` die einzigen Park-Trigger sind, und die HVE-Signatur selbst bleibt
  serverseitig unverifiziert ([ADR-0020](0020-veu-orders.md)).
- **Der Admin-Endpunkt vergrößert die unauthentifizierte Angriffsfläche** — allerdings nur um öffentliche
  Schlüssel, die HPB ohnehin an jeden onboardeten Teilnehmer herausgibt. Die Admin-API bleibt wie gehabt
  ausschließlich für den lokalen Emulatorbetrieb gedacht.

## Alternativen

- **Nur den Connector-Default senken, ohne geteilte Konstante:** verworfen — hätte denselben Fehler wieder
  möglich gemacht, sobald eine Seite ihren Wert anfasst. Die Kopplung ist real und gehört sichtbar gemacht.
- **`MaxRequestBodyBytes` auf 2 MiB anheben statt die Segmentgröße zu senken:** verworfen — verschiebt die
  Grenze nur und macht den Emulator toleranter gegenüber Payloads, die eine reale Bank ablehnen würde. Die
  EBICS-übliche 1-MiB-Schranke pro Segment bleibt der Bezugspunkt.
- **Den Report-Text bei Body-Fehlern auf `null` setzen statt aus der Registry zu holen:** verworfen — der
  symbolische Name ist die Information, die der Aufrufer erwartet, und er steht ohnehin schon im Katalog.
  Für unbekannte Codes ist `null` weiterhin das Ergebnis.
- **VEU nur über die generische `UploadRequest`/`DownloadRequest` erreichbar machen:** verworfen — hätte
  Punkt 5 und 6 (BTF-Pflicht, `OrderID`) zwar gelöst, aber jede Order-Familie im Connector hat
  Convenience-Requests; VEU als einzige Ausnahme wäre inkonsistent und ließe den Park-Trigger unentdeckt.
- **`GET /admin/banks/{hostId}/keys` auch als `PUT` (Keypair seeden):** zurückgestellt — der Befund war das
  *Lesen* der Fingerprints. Ein Import bräuchte PEM-Parsing und eine Entscheidung über private Anteile;
  in-process bleibt `IServerBankKeyStore.SetAsync` der Weg.
- **Den SDK-Pin ganz entfernen:** verworfen — [ADR-0001](0001-solution-layout-und-paketverwaltung.md) stützt
  die Reproduzierbarkeit ohne Lock-Files ausdrücklich auf den Pin.

## Verwandte Entscheidungen

- [ADR-0029 — Interop-Fixes für reale Clients](0029-interop-fixes-reale-clients.md) — derselbe Mechanismus:
  ein Test gegen etwas Echtes findet, was Selbstkonsistenz-Tests bauartbedingt verstecken.
- [ADR-0020 — VEU-Orders](0020-veu-orders.md) — die serverseitige Umsetzung, die hier clientseitig
  erschlossen wird.
- [ADR-0016 — BTF-Framework & Berechtigung](0016-btf-framework-und-berechtigung.md) — warum der BTF-Katalog
  kein Vollständigkeitsorakel ist.
- [ADR-0012 — Returncode-Katalog](0012-returncode-katalog.md) — die Header-/Body-Ablage, deren Text-Seite
  hier nachgezogen wird.
- [ADR-0015 — Ereignis-/Protokollspeicher](0015-ereignis-protokollspeicher.md) — die dokumentierte Trennung
  von Suite- und Server-Zustand, die der Banner sichtbar macht.

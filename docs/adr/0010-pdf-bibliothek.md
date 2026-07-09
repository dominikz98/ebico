# 0010 — PDF-Bibliothek für den INI-/HIA-Brief: QuestPDF (Community)

- Status: accepted
- Datum: 2026-07-09

## Kontext

Mit dem Connector-Onboarding (Issue **#47**, Milestone M6) erzeugt der `EBICO.Connector`
den **INI-/HIA-Brief** — das Dokument mit den öffentlichen Schlüssel-Fingerprints, das der
Teilnehmer unterschrieben zur Bank sendet, damit diese die per INI/HIA übertragenen Schlüssel
gegen den Brief abgleichen kann. Gefordert ist der Brief als **Text und PDF**.

Bisher enthält das Repo **keine** PDF-Bibliothek (`Directory.Packages.props` führt nur Test-,
DI- und HTTP-Pakete). Für die PDF-Erzeugung ist also eine neue Abhängigkeit nötig. Das Projekt
ist bei Abhängigkeiten bewusst zurückhaltend (BCL-only bei Krypto — [ADR-0008](0008-krypto-bibliothek.md);
Verzicht auf das kommerziell lizenzierte FluentAssertions v8 zugunsten von AwesomeAssertions —
[ADR-0002](0002-test-stack.md)). Die Lizenzlage eines PDF-Pakets ist daher explizit zu prüfen.

## Entscheidung

Der INI-/HIA-Brief wird mit **QuestPDF** unter der **Community-Lizenz** erzeugt. Die
Version wird zentral in `Directory.Packages.props` gepinnt (`PackageVersion`), referenziert wird
sie ohne Version im `EBICO.Connector.csproj` ([ADR-0001](0001-solution-layout-und-paketverwaltung.md)).

Der Brief ist hinter der Abstraktion `IInitializationLetterRenderer` gekapselt:

- `TextInitializationLetterRenderer` erzeugt den Brief **ohne** jede Abhängigkeit (reiner Text).
- `PdfInitializationLetterRenderer` erzeugt zusätzlich das PDF (QuestPDF) — es ist die per
  `AddEbicoOnboarding()` registrierte Standard-Implementierung und liefert Text **und** PDF.

Der geteilte Textkörper (`InitializationLetterTextBuilder`) stellt sicher, dass Text- und
PDF-Variante inhaltlich identisch sind. Die Community-Lizenz wird einmalig im statischen
Konstruktor des PDF-Renderers gesetzt (`QuestPDF.Settings.License = LicenseType.Community`).

## Konsequenzen

- **Lizenz:** QuestPDF Community ist kostenlos für Organisationen unterhalb der von QuestPDF
  definierten Umsatzschwelle (aktuell 1 Mio USD Jahresumsatz). Oberhalb ist eine kommerzielle
  QuestPDF-Lizenz erforderlich. Dies ist vor produktivem Einsatz zu prüfen; der Text-Renderer
  bleibt als lizenzfreier Fallback jederzeit verfügbar.
- **Abhängigkeitsgewicht:** QuestPDF wird eine transitive Abhängigkeit des Connector-NuGets
  (samt gebündeltem SkiaSharp-Renderer). Das steht in Spannung zum „schlanke Abhängigkeitsliste"-
  Ziel der [Connector-Architektur](../connector/architecture.md); der Text-Renderer ist deshalb
  dependency-frei gehalten, und die saubere Entkopplung (PDF-Renderer in einem separaten Paket
  `EBICO.Connector.Pdf`) bleibt als Option für später dokumentiert.
- **Headless/CI:** QuestPDF rendert über ein gebündeltes SkiaSharp; die PDF-Tests prüfen nur die
  Gültigkeit (PDF-Magic `%PDF-`, nicht leer), kein Layout, damit sie plattformunabhängig laufen.
- **Risiko/Revision:** Ändert QuestPDF seine Lizenzbedingungen oder ergeben sich CI-Probleme mit
  SkiaSharp, wird diese ADR neu bewertet — bevorzugt durch Auslagern des PDF-Renderers in ein
  optionales Paket oder Wechsel der PDF-Bibliothek; der Text-Brief bleibt unberührt.

## Alternativen

- **Nur Text (kein PDF):** keine neue Abhängigkeit, aber die geforderte PDF-Ausgabe fehlt —
  verworfen (Anforderung des Issues).
- **PdfSharp/MigraDoc:** MIT-lizenziert, keine Umsatzschwelle; API weniger fluent, Layout
  aufwändiger. Bleibt Rückfalloption, falls die QuestPDF-Lizenz zum Hindernis wird.
- **iText:** AGPL bzw. kommerziell — für ein potenziell öffentliches NuGet ungeeignet, verworfen.
- **PDF-Renderer in eigenem Paket `EBICO.Connector.Pdf`:** sauberste Entkopplung, hält den
  Connector-Kern dependency-frei; für dieses Issue zurückgestellt (Solution-/Packaging-Aufwand),
  als bevorzugte Migration im Risiko-Abschnitt vermerkt.

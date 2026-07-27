# ADR-0031 — Änderungsbenachrichtigung zwischen den Stammdaten-Inseln der Suite

- **Status:** accepted
- **Datum:** 2026-07-27
- **Kontext-Issue:** [#126](https://github.com/dominikz98/ebico/issues/126)

## Kontext

Die Seite `/stammdaten` rendert `BankManager`, `PartnerManager` und `SubscriberManager` als **drei
getrennte interaktive Inseln** (ADR-0009, „Interaktivität pro Komponente"). Jede Komponente lädt
ihren Zustand einmal in `OnInitializedAsync` und aktualisiert nach einer Mutation nur sich selbst.

Alle drei schreiben aber durch **denselben** `IMasterDataManager`, und die Beziehungen sind
kaskadierend (Bank → Partner → Teilnehmer, siehe [#30](../server/master-data.md)). Damit entwertete
jede Mutation in einer Insel den Zustand der anderen beiden, ohne dass diese es erfuhren. Ein
explorativer Test der laufenden Anwendung (#126) zeigte die Folgen:

- Eine neu angelegte Bank fehlte in den Auswahlfeldern von Partner- und Teilnehmer-Formular. Da das
  Formular auf die **erste** Bank der veralteten Liste vorbelegt, landete ein Partner
  stillschweigend unter einer fremden Bank — mit grüner Erfolgsmeldung.
- Eine gelöschte Bank blieb auswählbar; Speichern darauf ergab die widersprüchliche Meldungspaarung
  „Bank X gelöscht." + „Bank X existiert nicht.".
- Kaskadierend gelöschte Partner und Teilnehmer blieben als Karteileichen in den Tabellen stehen —
  mit aktiven „Bearbeiten"/„Löschen"-Buttons — bis zum nächsten vollständigen Seiten-Reload.

## Entscheidung

Ein eigener **`IMasterDataChangeNotifier`** (`src/EBICO.Suite/Services/`) als **Singleton**. Jede
Insel abonniert ihn in `OnInitializedAsync`, gibt das Abo in `Dispose` zurück und ruft nach **jeder**
erfolgreichen Mutation `NotifyChangedAsync()`.

Ein Abonnent tut zweierlei:

1. seinen Zustand neu laden, und
2. **transiente UI-Zustände gegen die frischen Daten prüfen** — ein offenes Formular darf keine
   gelöschte Bank mehr anbieten, eine Löschbestätigung für einen kaskadierten Datensatz ist
   gegenstandslos, ein Detailbereich ohne Datensatz schließt sich.

Punkt 2 ist der Teil, der leicht übersehen wird: Neuladen allein behebt die Tabellen, nicht die
bereits geöffneten Formulare.

Weil der Notifier ein Singleton ist, treffen Benachrichtigungen auf dem Thread des **auslösenden**
Circuits ein. Ein Abonnent muss deshalb über `ComponentBase.InvokeAsync` auf seinen eigenen Renderer
zurückwechseln, bevor er Komponenten-Zustand anfasst.

## Konsequenzen

- Die Inseln bleiben untereinander konsistent, ohne Seiten-Reload.
- **Auch über Sitzungen hinweg:** die Stores sind prozessweite Singletons (ADR-0009), der Notifier
  ist es ebenso — eine Änderung in einem Browser-Tab erreicht die anderen.
- Der Broadcast ist **best-effort**: ein fehlschlagender Abonnent stoppt die übrigen nicht, die
  Fehler werden gesammelt als `AggregateException` gemeldet statt stillschweigend verschluckt.
- Jede neue Komponente, die Stammdaten anzeigt, muss den Notifier abonnieren — sonst veraltet sie
  wieder still. Das ist die Kehrseite der Insel-Architektur und in
  [stammdaten.md](../suite/stammdaten.md) sowie im Skill `ebics-suite` festgehalten.
- Der Notifier trägt **keine** Nutzlast („was hat sich geändert"). Bei drei kleinen In-Memory-Listen
  ist vollständiges Neuladen billiger als ein differenziertes Ereignismodell; ein
  Blazor-`StateHasChanged` rendert die Insel ohnehin komplett neu.

## Alternativen

- **Die drei Inseln zu einer Komponente zusammenziehen**, die den Zustand hält und an die Manager
  als Parameter durchgibt. Löst das Problem ebenfalls und braucht keinen neuen Dienst — verwirft aber
  die bewusst gewählte Granularität aus ADR-0009 und macht aus drei überschaubaren Komponenten eine
  große. Verworfen.
- **Scoped statt Singleton.** Pro Circuit isoliert, damit ohne Marshalling-Pflicht und ohne
  Thread-Sicherheits-Bedarf. Löst die Fälle innerhalb *einer* Sitzung, aber nicht zwischen
  Sitzungen — obwohl der Zustand dahinter geteilt ist. Verworfen, weil die Inkonsistenz zwischen zwei
  Tabs derselben Ursache entspringt.
- **Polling** (Timer je Insel). Kein neuer Kontrakt, aber Latenz, Dauerlast und ein Formular, das
  unter den Händen springt, ohne dass etwas passiert wäre. Verworfen.
- **Nichts tun und einen Reload-Knopf anbieten.** Verlagert einen Konsistenzfehler auf die
  Anwenderin, und der schädlichste Fall (Partner landet stillschweigend unter der falschen Bank)
  bleibt bestehen. Verworfen.

## Verwandtes

- [ADR-0009 — Blazor Render-Modus (In-Process-Zustand)](0009-blazor-render-mode.md)
- [Suite: Stammdaten-Verwaltung](../suite/stammdaten.md)
- [Server: Stammdatenverwaltung (#30)](../server/master-data.md) — Kaskaden und Upsert-Semantik

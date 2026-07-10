# 0011 — Server-Stammdatenverwaltung (Manager über Store, Admin-API)

- Status: accepted
- Datum: 2026-07-10

## Kontext

Das Server-Grundgerüst (#25, [ADR-Backlog](README.md)) brachte den read/write
`IEbicsStateStore` mit einer In-Memory-Default-Implementierung, aber nur mit `Get*`/`Register*`
(Upsert). Issue #30 (M3) fordert eine echte **Stammdatenverwaltung**: vollständiges CRUD,
Berechtigungen pro OrderType/BTF und **Mehr-Banken-/Mehr-Mandanten-Fähigkeit**. Dabei sind mehrere
Entscheidungen zu treffen: wo die referentielle Integrität liegt, wie Partner mandantenscharf
modelliert werden, was beim Löschen mit abhängigen Objekten passiert und wie das CRUD nach außen
exponiert wird. Der ADR-Backlog listete „Persistenz des Server-States (In-Memory-Default,
pluggable Store) — M3/M4" als offen.

## Entscheidung

1. **Zweischichtig: Manager über Store.** Der `IEbicsStateStore` bleibt eine „dumme"
   Persistenz-Abstraktion (Get/Register/Remove/scoped Queries) ohne Business-Regeln. Darüber liegt
   ein neuer `IMasterDataManager`, der referentielle Integrität und kaskadierendes Löschen erzwingt
   und die Permission-/Lebenszyklus-Mutation kapselt. Admin-API und (spätere) Onboarding-Handler
   gehen ausschließlich über den Manager.
2. **Partner pro Bank gescoped.** `Partner` trägt jetzt `HostId`; der Store keyt Partner nach
   (`HostId`, `PartnerId`) statt global. Derselbe `PartnerId`-String bezeichnet an verschiedenen
   Banken verschiedene Kunden (Mehr-Mandanten-Fähigkeit).
3. **Kaskadierendes Löschen.** Das Löschen einer Bank entfernt deren Partner und Teilnehmer, das
   Löschen eines Partners dessen Teilnehmer. (Alternative „Löschen verbieten, solange Abhängige
   existieren" wurde zugunsten der einfacheren Emulator-Bedienung verworfen.)
4. **Unauthentifizierte HTTP-Admin-API.** `MapEbicoAdminApi` mappt eine geschachtelte REST/JSON-
   Oberfläche über den Manager. Sie ist bewusst **ohne** AuthN/AuthZ — passend zum lokalen
   Emulator-/Testbetrieb (wie Azurite). AuthN/AuthZ ist ein späteres Server-Issue.
5. **In-Memory-Default, pluggbar.** Der Zustand bleibt per Default In-Memory; ein persistenter
   Store ist über `TryAddSingleton`-Override einhängbar, ohne Aufrufer zu ändern (das Interface ist
   async vorbereitet). Damit ist der Backlog-Punkt „Persistenz des Server-States" adressiert.

## Konsequenzen

- **Klare Verantwortungstrennung:** Store = Persistenz, Manager = Invarianten. Ein späterer
  persistenter Store erbt die Business-Regeln automatisch, weil sie im Manager liegen.
- **Referentielle Integrität nur über den Manager.** Wer den Store direkt bespielt, umgeht die
  Prüfungen — das ist akzeptiert (Teststubs, Seeding), die öffentlichen Wege gehen über den Manager.
- **Kaskaden können Daten still löschen.** Für einen Emulator gewollt; die Admin-API macht das
  Verhalten dokumentiert transparent.
- **Sicherheit:** Die Admin-API darf nicht in nicht-vertrauenswürdigen Netzen exponiert werden.
- **Fehler-Modell vorläufig:** Der Manager wirft typisierte Exceptions (`UnknownBank/Partner/
  Subscriber`), die die Admin-API auf HTTP-Status abbildet. Das zentrale `EbicsResult<T>`/
  Returncode-Modell bleibt #36 (M4) vorbehalten.

## Alternativen

- **CRUD direkt im Store (ohne Manager):** vermischt Business-Regeln mit der Persistenz und macht
  einen Austausch des Stores riskant — verworfen.
- **Partner global (nur `PartnerId`):** minimaler Eingriff, aber nicht EBICS-getreu für
  Mehr-Banken-Szenarien (Kundennummern sind bankspezifisch) — verworfen.
- **Nur State-Layer ohne Admin-API:** ließe das CRUD ohne bedienbare Oberfläche bis #53 (M7);
  eine schlanke Admin-API macht den Emulator sofort nutzbar — daher aufgenommen.

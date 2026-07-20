<!--
  Danke für deinen Beitrag! Bitte fülle die Checkliste aus.
  Die "Definition of Done" ist projektweit verbindlich (siehe CLAUDE.md / docs/).
-->

## Beschreibung

<!-- Was ändert dieser PR und warum? Bezug zur EBICS-Version (H003/H004/H005), falls relevant. -->

## Issue

<!--
  Pflicht: Jeder PR referenziert genau ein Issue — auch reine Tooling-/Doku-Änderungen.
  Trage die Issue-Nummer ein (z. B. "Closes #42").
-->
Closes #

## Definition of Done

- [ ] **Issue verlinkt:** `Closes #<nr>` oben ausgefüllt (jeder PR referenziert genau ein Issue)
- [ ] **Doku:** Feature in Markdown unter `docs/` beschrieben (Zweck, Ablauf,
      Beispiel-XML/Code, EBICS-Versionsbezug) und im Doku-Index (`docs/index.md`) verlinkt
- [ ] **Docs/Skills aktualisiert:** betroffene `docs/`, `CLAUDE.md` und `.claude/skills/`
      im selben PR nachgezogen (siehe CLAUDE.md → „Wartung von Kontext, Doku & Skills")
- [ ] **Tests:** Unit-Tests für die Kernlogik (Happy Path + relevante
      Negativ-/Grenzfälle); bei Protokoll-/Krypto-Themen mit Testvektoren/Sample-XML
- [ ] **CI grün:** `dotnet build` + `dotnet test` erfolgreich, **keine neuen Warnungen**
- [ ] **XML-Doc-Kommentare** an öffentlichen APIs
- [ ] **Code-Review** durchgeführt

## Lizenz-Check (falls Schemas/Specs/Beispiele berührt)

- [ ] Keine proprietären EBICS-Schemas/Specs/Beispiel-XML ins Repo committet
      (siehe `docs/protocol/schema-sources.md`)

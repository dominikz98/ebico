<!--
  Danke für deinen Beitrag! Bitte fülle die Checkliste aus.
  Die "Definition of Done" ist projektweit verbindlich (siehe CLAUDE.md / docs/).
-->

## Beschreibung

<!-- Was ändert dieser PR und warum? Bezug zur EBICS-Version (H003/H004/H005), falls relevant. -->

Closes #<!-- Issue-Nummer -->

## Definition of Done

- [ ] **Doku:** Feature in Markdown unter `docs/` beschrieben (Zweck, Ablauf,
      Beispiel-XML/Code, EBICS-Versionsbezug) und im Doku-Index (`docs/index.md`) verlinkt
- [ ] **Tests:** Unit-Tests für die Kernlogik (Happy Path + relevante
      Negativ-/Grenzfälle); bei Protokoll-/Krypto-Themen mit Testvektoren/Sample-XML
- [ ] **CI grün:** `dotnet build` + `dotnet test` erfolgreich, **keine neuen Warnungen**
- [ ] **XML-Doc-Kommentare** an öffentlichen APIs
- [ ] **Code-Review** durchgeführt

## Lizenz-Check (falls Schemas/Specs/Beispiele berührt)

- [ ] Keine proprietären EBICS-Schemas/Specs/Beispiel-XML ins Repo committet
      (siehe `docs/protocol/schema-sources.md`)

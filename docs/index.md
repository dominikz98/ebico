# EBICO — Dokumentations-Index

Zentraler Einstieg in die EBICO-Doku. **Docs-as-Code:** Jedes Feature wird hier
verlinkt; Doku gehört in denselben PR wie der Code (Definition of Done).

## Überblick & Planung

- [Ticket-Übersicht](ticket-overview.md) — alle Milestones (M0–M9), Issues und Epics
- [Handover-Prompt für Claude Code](handover-claude-code.md) — Kontextblock für neue Sessions

## Entwicklung

- [Solution-Layout & Build-Konventionen](development/solution-layout.md) — Projektaufteilung, `Directory.Build.props`, zentrale Paketverwaltung
- *(folgt mit #7)* CI-Pipeline — `development/ci.md`
- *(folgt mit #8)* Test-Harness & Fixtures — `development/testing.md`

## Connector

- [Connector-Architektur](connector/architecture.md) — Mediator-Muster, Send-Pipeline, Designentscheidungen

## Protokoll & Schemas

- [Schema-Quellen & Lizenz](protocol/schema-sources.md) — Bezug der EBICS-XSDs, Lizenzlage

---

> Konvention: Neue Doku-Seiten werden hier unter der passenden Rubrik eingetragen.
> Tote Links werden in der CI per Link-Checker erkannt.

# EBICO — Dokumentations-Index

Zentraler Einstieg in die EBICO-Doku. **Docs-as-Code:** Jedes Feature wird hier
verlinkt; Doku gehört in denselben PR wie der Code (Definition of Done).

## Überblick & Planung

- [Ticket-Übersicht](ticket-overview.md) — alle Milestones (M0–M9), Issues und Epics
- [Handover-Prompt für Claude Code](handover-claude-code.md) — Kontextblock für neue Sessions

## Entwicklung

- [Solution-Layout & Build-Konventionen](development/solution-layout.md) — Projektaufteilung, `Directory.Build.props`, zentrale Paketverwaltung
- [CI-Pipeline (GitHub Actions)](development/ci.md) — Build/Test, Coverage-Artefakt, Doku-Link-Check
- [Test-Harness & Fixtures](development/testing.md) — xUnit v3, AwesomeAssertions, `CanonicalXmlComparer`, Fixtures

## Architektur-Entscheidungen

- [ADR-Index](adr/README.md) — Architecture Decision Records (Solution-Layout, Test-Stack, Schemas, Multi-Version, Connector-Dispatch)

## Connector

- [Connector-Architektur](connector/architecture.md) — Mediator-Muster, Send-Pipeline, Designentscheidungen

## Protokoll & Schemas

- [Schema-Quellen & Lizenz](protocol/schema-sources.md) — Bezug der EBICS-XSDs, Lizenzlage
- [XSD-Bindings](protocol/xsd-bindings.md) — generierte C#-Klassen je Version, Namespaces/Layout, Regenerierung, XmlSerializer-Hinweise
- [Versions-Dispatch](protocol/version-dispatch.md) — `EbicsVersion`-Registry, Envelope-Schnittstellen, Versionserkennung (`EbicsVersionDetector`)
- [XML-Serialisierung & C14N](protocol/serialization-c14n.md) — deterministische Serialisierung (Namespaces/Präfixe, stabile Ausgabe), Kanonisierung (inklusiv/exklusiv)
- [Domänenmodell](protocol/domain-model.md) — IDs (HostID/PartnerID/UserID/SystemID), Berechtigungen/Signaturklassen, Subscriber-Zustände, Aggregate
- [Schlüsselpaare & -repräsentation (A/E/X)](protocol/key-representation.md) — Schlüsselversionen (A00x/E002/X002), RSA-Container, Import/Export (PKCS#8/X.509/PEM/RSAKeyValue), Versions-Mapping
- [Lizenz & Repo-Policy](legal/ebics-licensing.md) — proprietäre Schemas: keine Commits, fetch-on-demand; Bindings committet (ADR-0006)

---

> Konvention: Neue Doku-Seiten werden hier unter der passenden Rubrik eingetragen.
> Tote Links werden in der CI per Link-Checker erkannt.

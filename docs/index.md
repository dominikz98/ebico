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
- [GitHub MCP-Server (Claude Code)](development/github-mcp.md) — MCP-Anbindung an GitHub, `.mcp.json`, PAT-Setup (`GITHUB_MCP_PAT`), benötigte Permissions

## Architektur-Entscheidungen

- [ADR-Index](adr/README.md) — Architecture Decision Records (Solution-Layout, Test-Stack, Schemas, Multi-Version, Connector-Dispatch)

## Connector

- [Connector-Architektur](connector/architecture.md) — Mediator-Muster, Send-Pipeline, Onboarding (INI/HIA/HPB), Transaktions-Skelett (Upload/Download), Designentscheidungen
- [Client-Kern & Konfiguration](connector/client-core.md) — `IEbicsClient`/`Send`-Dispatch (kein MediatR), Options/DI (`AddEbicoConnector`), `ITransport`/`IKeyStore`, vorläufiges `EbicsResult<T>`
- [Onboarding-Flows INI / HIA / HPB](connector/onboarding.md) — Schlüsselgenerierung, INI/HIA senden, HPB abrufen + Bankschlüssel-Hash-Abgleich, INI-Brief (Text/PDF), Versions-Dispatch (H003/H004/H005), `AddEbicoOnboarding`

## Server (Emulator)

- [Hostable Server-Grundgerüst (ASP.NET Core)](server/host.md) — EBICS-HTTP-Endpoint (POST, `text/xml`), Request-Pipeline (Parse → Version-Dispatch → Verify → Handle → Respond) mit Verify/Handle als Erweiterungspunkten, zentrale Fehlerabbildung auf EBICS-Returncodes, pluggbarer In-Memory-State-Store (`AddEbicoServer`)
- [Stammdatenverwaltung (Banken/Partner/Teilnehmer)](server/master-data.md) — CRUD im Server-Zustand, referentielle Integrität & kaskadierendes Löschen, Berechtigungen pro OrderType/BTF, Mehr-Banken-/Mehr-Mandanten-Fähigkeit, unauthentifizierte HTTP-Admin-API (`MapEbicoAdminApi`)

## Suite (Blazor UI)

- [UI-Grundgerüst & Navigation](suite/ui-shell.md) — Render-Modus (Interactive Server, ADR-0009), Navigation/Layout/Theming, Anbindung an den Emulator-Zustand (`IEmulatorStateProvider` + Stub)
- [Schlüssel-/Zertifikats-Ansicht](suite/schluessel-ansicht.md) — Public-Key-Fingerprints anzeigen, INI-Brief-Vergleich (`PublicKeyFingerprint.Verify`), Test-CA/Schlüssel-Werkzeuge (RSA-Generierung, self-signed Zertifikat + X.509-Verify, PEM-Download)

## Protokoll & Schemas

- [Schema-Quellen & Lizenz](protocol/schema-sources.md) — Bezug der EBICS-XSDs, Lizenzlage
- [XSD-Bindings](protocol/xsd-bindings.md) — generierte C#-Klassen je Version, Namespaces/Layout, Regenerierung, XmlSerializer-Hinweise
- [Versions-Dispatch](protocol/version-dispatch.md) — `EbicsVersion`-Registry, Envelope-Schnittstellen, Versionserkennung (`EbicsVersionDetector`)
- [XML-Serialisierung & C14N](protocol/serialization-c14n.md) — deterministische Serialisierung (Namespaces/Präfixe, stabile Ausgabe), Kanonisierung (inklusiv/exklusiv)
- [Domänenmodell](protocol/domain-model.md) — IDs (HostID/PartnerID/UserID/SystemID), Berechtigungen/Signaturklassen, Subscriber-Zustände, Aggregate
- [Schlüsselpaare & -repräsentation (A/E/X)](protocol/key-representation.md) — Schlüsselversionen (A00x/E002/X002), RSA-Container, Import/Export (PKCS#8/X.509/PEM/RSAKeyValue), Versions-Mapping
- [Banktechnische Signatur A005/A006](protocol/bank-signature.md) — Order-Hash (SHA-256), Signieren/Verifizieren A005 (PKCS1-v1.5) und A006 (PSS), registry-getriebenes Padding-Mapping
- [Authentifikationssignatur X002](protocol/auth-signature-x002.md) — XML-DSig `AuthSignature` über die `authenticate="true"`-Knoten: Reference-Digest (SHA-256) + SignatureValue (RSA-PKCS1-v1.5), Dokumentkontext-C14N (inklusiv), registry-getriebenes Padding-Mapping
- [Verschlüsselung E002 (RSA-OAEP + AES-128-CBC)](protocol/encryption-e002.md) — hybride Transportverschlüsselung: AES-128-CBC über die Auftragsdaten, RSAES-OAEP-SHA256 über den Transaktionsschlüssel, registry-getriebenes Padding-Mapping
- [Public-Key-Fingerprints (HPB/INI/HIA)](protocol/public-key-fingerprint.md) — SHA-256-Hashwerte öffentlicher Schlüssel (Exponent/Modulus-Hash-Input), Darstellung für INI-Brief und HPB-Antwort, konstantzeitige Verifikation client-gesendeter Hashes
- [Zertifikatsverifizierung (X.509)](protocol/certificate-verification-x509.md) — Kette/Vertrauensanker (konfigurierbar, Test-CA), Gültigkeit und Verwendungszweck (KeyUsage je Schlüsselrolle), optionales Key-Binding; reine-Schlüssel-Verfahren (H003/H004) als Policy (`CertificateRequirement`)
- [Lizenz & Repo-Policy](legal/ebics-licensing.md) — proprietäre Schemas: keine Commits, fetch-on-demand; Bindings committet (ADR-0006)

---

> Konvention: Neue Doku-Seiten werden hier unter der passenden Rubrik eingetragen.
> Tote Links werden in der CI per Link-Checker erkannt.

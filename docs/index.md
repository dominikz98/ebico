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
- [INI — Senden der Signaturschlüssel (A00x)](server/ini.md) — erster Order-Handler: Empfang/Speicherung des bankfachlichen Signaturschlüssels (`IServerKeyStore`), Teilnehmer-Übergang `New → Initialized`, Antwort als `ebicsKeyManagementResponse`, Returncodes (bereits initialisiert / defekte Order-Data), H003/H004 (`RSAKeyValue`) vs. H005 (X.509)
- [HIA — Senden der Auth- & Enc-Schlüssel (X002/E002)](server/hia.md) — zweiter Order-Handler: Empfang/Speicherung des Authentifizierungs- (`X00x`) und Verschlüsselungsschlüssels (`E00x`) im `IServerKeyStore`, Teilnehmer-Übergang `Initialized → Ready`, Antwort als `ebicsKeyManagementResponse`, Returncodes (INI fehlt / bereits abgeschlossen / defekte Order-Data), H003/H004 (`RSAKeyValue`) vs. H005 (X.509)
- [HPB — Abruf der Bankschlüssel](server/hpb.md) — dritter Order-Handler: Rückgabe der öffentlichen **Bankschlüssel** (`X00x`/`E00x`) an einen `Ready`-Teilnehmer via signiertem `ebicsNoPubKeyDigestsRequest`, Antwort mit **verschlüsseltem** `DataTransfer` (E002-Hybrid für den Teilnehmer, `HPBResponseOrderData` + `EncryptionPubKeyDigest`), eigener `IServerBankKeyStore` (auto-generiert/seedbar), Returncodes (Teilnehmer nicht `Ready`/unbekannt), H003/H004 (`RSAKeyValue`) vs. H005 (X.509)
- [Schlüsselwechsel & Sperrung (HCA/HCS/SPR/HSA)](server/hca-hcs-spr-hsa.md) — die abschließenden Key-Management-Handler: **HCA/HCS** ersetzen Schlüssel via signiertem `ebicsRequest` mit **E002-verschlüsselter** Order-Data (Entschlüsselung mit dem privaten Bankschlüssel, Purpose-Upsert im `IServerKeyStore`, Teilnehmer bleibt `Ready`), **SPR** setzt den Teilnehmer auf `Suspended` (Statusmaschine), **HSA** als Legacy-HIA (nur H003/H004); Antwort `ebicsResponse` (HCA/HCS/SPR) bzw. `ebicsKeyManagementResponse` (HSA), Returncodes (`091002`/`090004`), Spec-Vorbehalt: keine ES/X002-Prüfung (M4)
- [Segmentierung, Kompression & Base64-Pipeline](server/segmentation.md) — die querschnittliche Byte-Pipeline der Transaction Engine (M4): deterministisches Aufteilen des komprimierten (ggf. E002-verschlüsselten) Order-Data-Bytestroms in Segmente (`EbicsSegmentation.Split`/`Reassemble` in `EBICO.Core`) und konfigurierbare Segmentgröße (`EbicoServerOptions.SegmentSizeBytes`, Roh-Bytes vor Base64, Default 512 KiB); baut auf `EbicsCompression` auf, Base64 via `base64Binary`-Bindung, Grenzfälle (1 Segment, leeres OrderData); policy-frei — Transaction-ID/Phasen/Envelope & Segment-Returncodes verdrahten Upload (#32)/Download (#33)
- [Upload-Transaktion (Initialisation + Transfer)](server/upload-transaction.md) — die erste mehrphasige Transaktion der Engine (M4): zweiphasiger Upload mit **Transaction-ID-Vergabe** (Initialisation) und **segmentweisem Empfang** (Transfer) → Reassemblierung/Entschlüsselung (`EncryptionE002`)/Dekompression der Order-Data; dedizierte `UploadTransactionEngine` + In-Memory-`IUploadTransactionStore` (ADR-0013), Phasen-Routing in der Pipeline, angebunden an **FUL** (H003/H004) / **BTU** (H005); Transaktions-/Segment-Returncodes (`091101`/`091104`/`091103`/`011101`/`091114`); Spec-Vorbehalt: ES-Signaturprüfung des OrderData zurückgestellt
- [Download-Transaktion (Initialisation + Transfer + Receipt)](server/download-transaction.md) — die dreiphasige Sende-Transaktion der Engine (M4): serverseitige **Datenbereitstellung** (`IDownloadDataProvider` + Admin-API, verbrauchend) → Komprimieren/E002-Verschlüsseln (für den Teilnehmer-Enc-Key)/**Segmentieren** (`EbicsSegmentation.Split`) → Ausliefern von `NumSegments`+Segment 1 (Init) und Segmenten 2…N (Transfer) → **Receipt** (positiv `011000` / negativ `011001`, negativ stellt die Daten wieder ein); dedizierte `DownloadTransactionEngine` + In-Memory-`IDownloadTransactionStore` (ADR-0014), Upload/Download-Routing per `OwnsTransaction`, angebunden an **FDL** (H003/H004) / **BTD** (H005); Returncodes (`090005`/`091101`/`091104`/`091112`/`091114`); Spec-Vorbehalt: unsignierte Antwort (X002 = M4)
- [Transaktions-Recovery & Timeouts](server/transaction-recovery.md) — der Lebenszyklus-Abschluss der Transaction Engine (M4): **Idle-Timeout** je Transaktion (gleitendes `LastActivityAt`-Fenster, `IsExpired`/`Touch`), **Eviction** abgelaufener/verwaister Transaktionen **lazy beim Zugriff** (→ `091101`) **und** über den `TransactionCleanupService` (BackgroundService + `ITransactionEvictor`), **Re-Enqueue** entnommener Download-Daten bei Ablauf (genau-einmal-Guard), **Idempotenz** doppelter Segmente/Init/Receipt (Retention = Replay-Fenster, `091103`), obere Schranke paralleler Transaktionen (`091115`); neue `EbicoServerOptions` (`TransactionTimeout`/`TransactionCleanupInterval`/`MaxConcurrentTransactions`); Spec-Vorbehalt: kein aktiver Recovery-Sync-Flow (`061101` verfügbar, nicht ausgelöst)

## Suite (Blazor UI)

- [UI-Grundgerüst & Navigation](suite/ui-shell.md) — Render-Modus (Interactive Server, ADR-0009), Navigation/Layout/Theming, Anbindung an den Emulator-Zustand (`IEmulatorStateProvider` + Stub)
- [Stammdaten-Verwaltung](suite/stammdaten.md) — CRUD für Banken/Partner/Teilnehmer über den `IMasterDataManager` (in-process, ADR-0009), Teilnehmer-Status & Berechtigungen, Seeding der Sample-Daten
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
- [EBICS-Returncode-Katalog](protocol/return-codes.md) — zentrale technische/fachliche Returncodes als Konstanten (`EbicsReturnCode`) + Registry (`EbicsReturnCodes`) in `EBICO.Core`, Header- vs. Body-Ablage (`Kind`), Exception→Returncode-Mapping (`EbicsErrorMapper`), Spec-Vorbehalte gegen Annex 1
- [Lizenz & Repo-Policy](legal/ebics-licensing.md) — proprietäre Schemas: keine Commits, fetch-on-demand; Bindings committet (ADR-0006)

---

> Konvention: Neue Doku-Seiten werden hier unter der passenden Rubrik eingetragen.
> Tote Links werden in der CI per Link-Checker erkannt.

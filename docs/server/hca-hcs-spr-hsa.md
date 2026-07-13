# Server: Schlüsselwechsel & Sperrung — HCA / HCS / SPR / HSA

> Umsetzung von **Issue #29** (Milestone M3 — Server: Key Management). Diese Seite beschreibt
> die vier abschließenden Key-Management-Order-Handler des Emulators und schließt die Reihe
> [INI](ini.md) → [HIA](hia.md) → [HPB](hpb.md) ab:
>
> - **HCA** — Schlüsselwechsel: ersetzt **Auth** (`X00x`) + **Enc** (`E00x`) eines onboardeten Teilnehmers.
> - **HCS** — Schlüsselwechsel: ersetzt **alle** Schlüssel (Sig `A00x` + Auth + Enc).
> - **SPR** — Suspendierung/Sperrung: setzt den Teilnehmer in den Zustand `Suspended`.
> - **HSA** — Legacy-Initialisierung von Auth + Enc (nur **H003/H004**, in H005 entfallen).
>
> Bewusst **enthalten**: OrderType-Verarbeitung von HCA/HCS/SPR (signierter `ebicsRequest`) und
> HSA (`ebicsUnsecuredRequest`), **E002-Entschlüsselung** der HCA/HCS-Order-Data mit dem privaten
> Bankschlüssel, Schlüssel-**Ersetzung** per Purpose-Upsert, konsistente **Statusübergänge**
> (SPR → `Suspended`), Antwort als `ebicsResponse` (HCA/HCS/SPR) bzw. `ebicsKeyManagementResponse`
> (HSA), Returncodes für die Fehlerfälle.
> Bewusst **noch nicht**: Prüfung von **Auftragssignatur (ES)** und **X002-Request-Signatur** (M4),
> die generische Upload-**Transaktionsmaschine** mit Segmentierung (M4), ein eigener
> `Blocked`-Status (SPR nutzt `Suspended`), Zertifikatsketten-Prüfung bei H005 (M8), vollständiger
> Returncode-Katalog (#36/M4).

## Zweck

Nach dem Onboarding ([INI](ini.md) → [HIA](hia.md), Zustand `Ready`) muss ein Teilnehmer seine
Schlüssel **wechseln** können (turnusmäßig oder bei Kompromittierung) und im Störfall **gesperrt**
werden können:

- **HCA** (*change authentication/encryption*) tauscht den Authentifizierungs- (`X00x`) und den
  Verschlüsselungsschlüssel (`E00x`) aus. Der bankfachliche Signaturschlüssel bleibt.
- **HCS** (*change subscriber's keys*) tauscht **alle drei** Schlüssel aus (Signatur `A00x` +
  Auth + Enc) — die Kombination aus INI + HIA als einzelner Schlüsselwechsel.
- **SPR** (*suspension*) sperrt den Teilnehmer für die Auftragsabwicklung; er wird `Suspended` und
  kann erst nach Reaktivierung (`Suspended → Ready`, über die [Admin-API](master-data.md)) wieder
  transagieren. Die Schlüssel bleiben erhalten.
- **HSA** ist die historische Variante von HIA (nur H003/H004): sie überträgt Auth + Enc in einem
  `ebicsUnsecuredRequest` und ist damit funktional identisch zu HIA.

Anders als INI/HIA (ungesicherte Uploads) sind **HCA/HCS/SPR** signierte `ebicsRequest`-Uploads.
Bei HCA/HCS ist die Order-Data mit dem **öffentlichen `E002`-Schlüssel der Bank** verschlüsselt —
die Gegenrichtung zu [HPB](hpb.md): der Server entschlüsselt sie mit seinem **privaten**
`E002`-Schlüssel (`IServerBankKeyStore`).

## Ablauf

Die Pipeline (`EbicsRequestPipeline`) zieht den OrderType aus dem Header (H003/H004: `OrderType`,
H005: `AdminOrderType`) und leitet an den versionspassenden Handler weiter. `ebicsRequest`-Orders
werden mit `ebicsResponse` beantwortet, HSA (`ebicsUnsecuredRequest`) mit
`ebicsKeyManagementResponse` — beides ohne Pipeline-Änderung (siehe [host.md](host.md)).

### HCA / HCS — verschlüsselter Schlüsselwechsel

Versionsagnostischer Ablauf in `HcaOrderHandlerBase` / `HcsOrderHandlerBase`, die
versionsspezifische Schlüssel-Extraktion in `H003`/`H004`/`H005{Hca,Hcs}OrderHandler`:

| Schritt | Aktion |
| --- | --- |
| 1. Envelope | `Header/Static` (IDs) + `Body/DataTransfer` (`DataEncryptionInfo/TransactionKey` + `OrderData`) aus dem `ebicsRequest` lesen (`ExtractEnvelope`) |
| 2. Bankschlüssel | `IServerBankKeyStore.GetOrCreateAsync(hostId)` liefert das Bankpaar **mit privatem `E002`-Schlüssel** |
| 3. Entschlüsseln | `EncryptionE002.Decrypt(TransactionKey + OrderData, bankE002)` → `EbicsCompression.Decompress` → Order-Data-XML |
| 4. Extraktion | `ParseOrderData` liest Auth+Enc (HCA) bzw. Sig+Auth+Enc (HCS); H003/H004 `RSAKeyValue`, H005 `X509Data` |
| 5. Key-Policy | Purpose je Schlüssel korrekt + `KeyVersions.EnsurePermitted(version, protokoll)` |
| 6. Teilnehmer | `GetSubscriberAsync` — muss existieren und `Ready` sein |
| 7. Ersetzen | `IServerKeyStore.StoreAsync` je Purpose (Upsert **ersetzt** den Alt-Schlüssel) → `000000` |

**Kein** Zustandsübergang: ein Schlüsselwechsel lässt den Teilnehmer `Ready`.

Beispiel — HCA-Order-Data (H004, gekürzt), **vor** Kompression/Verschlüsselung:

```xml
<HCARequestOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <AuthenticationPubKeyInfo>
    <PubKeyValue><ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue></PubKeyValue>
    <AuthenticationVersion>X002</AuthenticationVersion>
  </AuthenticationPubKeyInfo>
  <EncryptionPubKeyInfo>
    <PubKeyValue><ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue></PubKeyValue>
    <EncryptionVersion>E002</EncryptionVersion>
  </EncryptionPubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</HCARequestOrderData>
```

HCS ergänzt darin ein `SignaturePubKeyInfo` (H003/H004 im `S001`-, H005 im `S002`-Namespace).
Auf dem Draht steht die Order-Data base64-kodiert als `E002-encrypt(zlib(orderData))`:

```xml
<ebicsRequest xmlns="urn:org:ebics:H005" Version="H005">
  <header authenticate="true">
    <static><HostID>EBICOHOST</HostID><PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID>
      <OrderDetails><AdminOrderType>HCA</AdminOrderType></OrderDetails></static>
    <mutable/>
  </header>
  <body>
    <DataTransfer>
      <DataEncryptionInfo authenticate="true">
        <EncryptionPubKeyDigest Version="E002" Algorithm="http://www.w3.org/2001/04/xmlenc#sha256">…</EncryptionPubKeyDigest>
        <TransactionKey>…</TransactionKey>
      </DataEncryptionInfo>
      <OrderData>…</OrderData>
    </DataTransfer>
  </body>
</ebicsRequest>
```

Erfolgsantwort ist ein `ebicsResponse` mit `000000` in Header **und** Body.

### SPR — Suspendierung

`SprOrderHandlerBase` (+ Versionsableitungen) liest nur die Header-IDs — SPR trägt **keine**
Order-Data (es gibt kein `SPRRequestOrderData`):

| Schritt | Aktion |
| --- | --- |
| 1. Identifikation | `Header/Static` (IDs) aus dem `ebicsRequest` lesen |
| 2. Teilnehmer | `GetSubscriberAsync` — muss existieren und darf **nicht** bereits `Suspended` sein |
| 3. Übergang | `IMasterDataManager.TransitionSubscriberAsync(…, Suspended)` → `000000` |

Der Übergang `New/Initialized/Ready → Suspended` ist in der Statusmaschine
(`Subscriber.IsAllowedTransition`) erlaubt. Etwaige `DataTransfer`/Auftragssignatur wird ignoriert
(ES-Prüfung ist M4).

### HSA — Legacy-Initialisierung (H003/H004)

`HsaOrderHandlerBase` (+ H003/H004) spiegelt [HIA](hia.md): Auth + Enc aus einem
`ebicsUnsecuredRequest` speichern und `Initialized → Ready` übergehen. Antwort ist — wie bei INI/HIA
— ein `ebicsKeyManagementResponse` mit `000000`.

## Schlüssel-Store & Statusmaschine

- **Schlüssel-Ersetzung:** `IServerKeyStore` ist ein Upsert pro `(Teilnehmer, KeyPurpose)`; das
  Speichern eines Schlüssels gleichen Zwecks **überschreibt** den alten. HCA ersetzt so Auth+Enc,
  HCS Sig+Auth+Enc; der jeweils andere Schlüssel bleibt unangetastet.
- **Bankschlüssel:** die HCA/HCS-Entschlüsselung nutzt den **privaten** `E002`-Schlüssel der Bank
  aus `IServerBankKeyStore` (dasselbe Paar, das [HPB](hpb.md) ausliefert und mit dem der Client die
  Order-Data verschlüsselt hat).
- **Statusübergänge** (`SubscriberState`, `Subscriber.IsAllowedTransition`) bleiben **unverändert**:
  `Suspended` und die Kanten `New/Initialized/Ready → Suspended` bzw. `Suspended → Ready` existieren
  bereits. HCA/HCS führen **keinen** Übergang aus (bleiben `Ready`); SPR geht nach `Suspended`; HSA
  geht `Initialized → Ready`. SPR **entfernt keine Schlüssel** — die Suspendierung ist reversibel.

## Returncodes & Fehlerfälle

Wie am gesamten `/ebics`-Endpoint werden Fehler mit **HTTP 200** und einem Returncode im Envelope
beantwortet (siehe [host.md](host.md)); der fachliche Code steht in `body/ReturnCode`.

| Situation | Returncode |
| --- | --- |
| HCA/HCS/SPR/HSA erfolgreich | `000000` EBICS_OK |
| Teilnehmer unbekannt oder in falschem Zustand (HCA/HCS: nicht `Ready`; HSA: nicht `Initialized`; SPR: unbekannt oder bereits `Suspended`) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order-Data nicht entschlüsselbar/entpackbar/deserialisierbar, unzulässige/zweckfremde Schlüsselversion, falscher Request-Typ | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

### ⚠️ Spec-Vorbehalte

- **Keine Signaturprüfung.** HCA/HCS/SPR sind signierte Uploads (Auftragssignatur/ES + X002); diese
  Signaturen werden **nicht** geprüft (die Verify-Stufe bleibt No-Op, **M4** — konsistent mit
  INI/HIA/HPB). Die Vertraulichkeit der HCA/HCS-Order-Data bleibt gewahrt, weil sie für den
  Bank-`E002`-Schlüssel verschlüsselt ist.
- **Vereinfachte Single-Phase-Verarbeitung.** Der signierte Upload wird in einem Schritt behandelt;
  die generische Transaktionsmaschine (Initialisierung/Transfer, Segmentierung) ist **M4**.
- **SPR → `Suspended`.** EBICS unterscheidet u. U. temporäre Suspendierung von dauerhafter Sperrung;
  EBICO bildet SPR auf den vorhandenen `Suspended`-Zustand ab (kein eigener `Blocked`-Status). Die
  Reaktivierung läuft out-of-band über die [Admin-API](master-data.md). Ein bereits `Suspended`er
  Teilnehmer kann nicht per INI/HIA re-onboarden (kein `Suspended → New/Initialized`-Kante).
- **HSA-Zustandsannahme.** HSA verlangt `Initialized` (INI gelaufen) und geht nach `Ready` — analog
  HIA; der genaue Legacy-Ablauf ist gegen den offiziellen Annex zu verifizieren.
- **H005:** Schlüssel werden als `X509Data`-Zertifikate transportiert; nur der Public-Key wird
  entnommen, eine Zertifikatsketten-Prüfung ist **M8**.
- **Nur `E002`.** Die HCA/HCS-Entschlüsselung nutzt `EncryptionE002` (RSA-OAEP); ein Legacy-`E001`
  wird nicht unterstützt. Die konkreten Codes (`091002`/`090004`) sind gegen EBICS-Annex 1 zu
  verifizieren; der zentrale Returncode-Katalog kommt mit **#36 (M4)**.

## EBICS-Versionsbezug

| Order | Envelope | Order-Data | Schlüsseltransport | Versionen |
| --- | --- | --- | --- | --- |
| HCA | signierter `ebicsRequest` (verschlüsselt) | `HCARequestOrderData` (Auth + Enc) | H003/H004 `RSAKeyValue`, H005 `X509Data` | H003/H004/H005 |
| HCS | signierter `ebicsRequest` (verschlüsselt) | `HCSRequestOrderData` (Sig + Auth + Enc) | H003/H004 `RSAKeyValue` (Sig: `S001`), H005 `X509Data` (Sig: `S002`) | H003/H004/H005 |
| SPR | signierter `ebicsRequest` | — (keine) | — | H003/H004/H005 |
| HSA | `ebicsUnsecuredRequest` | `HSARequestOrderData` (Auth + Enc) | `RSAKeyValue` | **nur H003/H004** |

OrderType-Feld: H003/H004 `OrderType`, H005 `AdminOrderType`. Antworttyp: `ebicsResponse` für
HCA/HCS/SPR, `ebicsKeyManagementResponse` für HSA.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten
Core-Bindings, keine proprietären Fixtures), End-to-End über `EbicsRequestPipeline`:

- `HcaOrderHandlerTests` — `[Theory]` über H003/H004/H005: Happy Path (Antwort `ebicsResponse`
  `000000`, Auth+Enc im `IServerKeyStore` **ersetzt** — neuer Modulus ≠ alter, Teilnehmer bleibt
  `Ready`) plus Negativfälle (Teilnehmer nicht `Ready`, unbekannt → `091002`; unentpackbare
  Order-Data, zweckfremde Schlüsselversion → `090004`).
- `HcsOrderHandlerTests` — wie HCA, zusätzlich der **Signaturschlüssel** ersetzt.
- `SprOrderHandlerTests` — Suspendierung aus `New`/`Initialized`/`Ready` (→ `Suspended`, Antwort
  `ebicsResponse` `000000`); Negativfälle (unbekannt, bereits `Suspended` → `091002`).
- `HsaOrderHandlerTests` — `[Theory]` über H003/H004: Happy Path (`Initialized → Ready`, Auth+Enc
  gespeichert); Negativfälle (Teilnehmer `New`/`Ready`/unbekannt → `091002`; defekte Order-Data,
  zweckfremde Version → `090004`).
- `EbicoServerServiceCollectionExtensionsTests` — die DI-Registrierung führt HCA/HCS/SPR je Version
  und HSA für H003/H004.

Die Request-Builder (u. a. `BuildEncryptedHcaRequest`/`BuildEncryptedHcsRequest` mit
`EncryptionE002.Encrypt` gegen den Bank-Public-Key, `BuildSprRequest`, `BuildUnsecuredHsaRequest`)
liegen in `ServerTestHelpers`.

## Verwandte Doku

- [INI — Senden der Signaturschlüssel (A00x)](ini.md) — erster Onboarding-Schritt
- [HIA — Senden der Auth- & Enc-Schlüssel (X002/E002)](hia.md) — HSA ist die Legacy-Variante hiervon
- [HPB — Abruf der Bankschlüssel](hpb.md) — Gegenrichtung der E002-Verschlüsselung
- [Hostable Server-Grundgerüst](host.md) — Host, Pipeline, Returncodes, Response-Factory
- [Stammdatenverwaltung](master-data.md) — Teilnehmer-Lebenszyklus, Statusmaschine, Admin-API
- [Transportverschlüsselung E002](../protocol/encryption-e002.md) — E002-Hybrid (AES + RSA-OAEP)

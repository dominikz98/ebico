# Server: HPB — Abruf der Bankschlüssel

> Umsetzung von **Issue #28** (Milestone M3 — Server: Key Management). Diese Seite
> beschreibt den dritten fachlichen **Order-Handler** des Emulators: die Rückgabe der
> öffentlichen **Bankschlüssel** — Authentifizierung (`X00x`) und Verschlüsselung (`E00x`)
> — an einen bereits initialisierten Teilnehmer per **HPB**, als **verschlüsselte**,
> HPB-konforme Antwort.
>
> Bewusst **enthalten**: OrderType-`HPB`-Verarbeitung für H003/H004/H005, ein serverseitiger
> **Bank-Schlüssel-Store** (`IServerBankKeyStore`, auto-generiert & seedbar), Aufbau der
> `HPBResponseOrderData`, Kompression + **E002-Verschlüsselung** der Order-Data für den
> Teilnehmer, `EncryptionPubKeyDigest` zum Abgleich, Antwort als `ebicsKeyManagementResponse`
> mit gefülltem `DataTransfer`, Returncodes für die Fehlerfälle.
> Bewusst **noch nicht**: Prüfung der **X002-Request-Signatur** und **Signatur der Antwort**
> (X002, M4), Persistenz des Schlüssel-Stores (In-Memory bleibt Default),
> Zertifikatsketten-Prüfung bei H005 (M8), vollständiger Returncode-Katalog (#36/M4).

## Zweck

HPB ist der **Download-Gegenpart** zu [INI](ini.md) und [HIA](hia.md): nachdem ein
Teilnehmer seine öffentlichen Schlüssel hochgeladen hat (Zustand `Ready`), holt er per
**HPB** die öffentlichen Schlüssel **der Bank** ab — den Authentifizierungsschlüssel
(`X002`) und den Verschlüsselungsschlüssel (`E002`) —, um künftige Server-Antworten
verifizieren bzw. entschlüsseln zu können.

Anders als INI/HIA (ungesicherte Uploads, deren Antwort nur ein Returncode ist) ist HPB
ein **signierter** `ebicsNoPubKeyDigestsRequest`, dessen Antwort einen **verschlüsselten
Nutzlast-Body** trägt: die `HPBResponseOrderData` mit den Bankschlüsseln, **komprimiert**
und mit dem `E002`-Schlüssel des Teilnehmers **verschlüsselt** (E002-Hybrid: AES-128-CBC
für die Daten, RSA-OAEP für den Transaktionsschlüssel). So kann nur der Teilnehmer, der den
privaten E002-Schlüssel hält, die Antwort lesen.

Der Client-Gegenpart (HPB senden, Antwort entschlüsseln, Fingerprint-Abgleich) ist im
Connector umgesetzt (siehe [Onboarding-Flows](../connector/onboarding.md)).

## Ablauf

Die Pipeline (`EbicsRequestPipeline`) erkennt den `ebicsNoPubKeyDigestsRequest`, zieht den
OrderType `HPB` aus dem Header und leitet an den versionspassenden Handler weiter. Der
versionsagnostische Ablauf liegt in `HpbOrderHandlerBase`, der versionsspezifische Aufbau der
Bankschlüssel-Order-Data in `H003`/`H004`/`H005HpbOrderHandler`:

| Schritt | Aktion |
| --- | --- |
| 1. Identifikation | `Header/Static` (`HostID`/`PartnerID`/`UserID`) aus dem `ebicsNoPubKeyDigestsRequest` lesen (`ExtractHpbRequest`) |
| 2. Teilnehmer | `IMasterDataManager.GetSubscriberAsync` — muss existieren und im Zustand `Ready` sein (INI **und** HIA gelaufen) |
| 3. Empfänger-Key | E002-Public-Key des Teilnehmers aus `IServerKeyStore.GetAsync(…, KeyPurpose.Encryption)` (bei HIA abgelegt) |
| 4. Bankschlüssel | `IServerBankKeyStore.GetOrCreateAsync(hostId)` liefert das eigene `X002`/`E002`-Paar der Bank |
| 5. Order-Data | versionsspezifisch `HPBResponseOrderData` bauen (H003/H004: `PubKeyValue/RSAKeyValue`; H005: `X509Data`) → `EbicsXmlSerializer.SerializeOrderData` |
| 6. Verschlüsseln | `EbicsCompression.Compress` → `EncryptionE002.Encrypt(compressed, teilnehmerE002, version)` → `EncryptedOrderData` |
| 7. Digest | `PublicKeyFingerprint.Compute(teilnehmerE002)` für `DataEncryptionInfo/EncryptionPubKeyDigest` |
| 8. Antwort | `ebicsKeyManagementResponse` mit `000000` und gefülltem `Body/DataTransfer` (`EbicsResponseFactory.BuildKeyManagementResponse(version, payload)`) |

**Kein** Zustandsübergang: HPB ist lesend, der Teilnehmer bleibt `Ready`.

Beispiel — HPB-Order-Data (H004, gekürzt), vor Kompression/Verschlüsselung:

```xml
<HPBResponseOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <AuthenticationPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <AuthenticationVersion>X002</AuthenticationVersion>
  </AuthenticationPubKeyInfo>
  <EncryptionPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <EncryptionVersion>E002</EncryptionVersion>
  </EncryptionPubKeyInfo>
  <HostID>EBICOHOST</HostID>
</HPBResponseOrderData>
```

Erfolgsantwort (H004, gekürzt) — die Order-Data ist base64(E002-encrypt(zlib(orderData))):

```xml
<ebicsKeyManagementResponse xmlns="urn:org:ebics:H004" Version="H004">
  <header authenticate="true">
    <static/>
    <mutable><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body>
    <DataTransfer>
      <DataEncryptionInfo authenticate="true">
        <EncryptionPubKeyDigest Version="E002"
          Algorithm="http://www.w3.org/2001/04/xmlenc#sha256">…</EncryptionPubKeyDigest>
        <TransactionKey>…</TransactionKey>
      </DataEncryptionInfo>
      <OrderData>…</OrderData>
    </DataTransfer>
    <ReturnCode>000000</ReturnCode>
  </body>
</ebicsKeyManagementResponse>
```

## Bank-Schlüssel & Schlüssel-Store

Für HPB braucht der Server sein **eigenes** Schlüsselpaar. Das hält der neue
`IServerBankKeyStore` (Default `InMemoryServerBankKeyStore`, via `TryAddSingleton`
überschreibbar), gekeyt auf `HostId` (Mehr-Banken-fähig). `GetOrCreateAsync` **generiert
das Paar bei Bedarf** (`RsaKeyMaterial.Generate()`, Versionen `X002`/`E002`) und **cacht es
prozessweit** — so liefert wiederholtes HPB dieselben Schlüssel (wichtig für den
Fingerprint-Abgleich des Clients). `SetAsync` erlaubt das Seeden eines bekannten Paars
(Tests, feste Emulator-Identitäten).

Der zum **Verschlüsseln** der Antwort nötige `E002`-Public-Key des **Teilnehmers** liegt im
`IServerKeyStore` (aus [HIA](hia.md), `KeyPurpose.Encryption`). Das Domänen-Aggregat `Bank`
bleibt bewusst schlüsselfrei (siehe [Stammdaten](master-data.md)); Schlüsselmaterial ist eine
Sache der Server-Schicht.

## Returncodes & Fehlerfälle

Wie am gesamten `/ebics`-Endpoint werden Protokoll-/Businessfehler mit **HTTP 200** und einem
Returncode im Envelope beantwortet (siehe [host.md](host.md)); der fachliche Code steht in
`body/ReturnCode`.

| Situation | Returncode |
| --- | --- |
| HPB erfolgreich (verschlüsselte Bankschlüssel) | `000000` EBICS_OK |
| Teilnehmer unbekannt **oder** nicht `Ready` (INI/HIA nicht abgeschlossen) **oder** `Ready` ohne hinterlegten E002-Schlüssel | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Falscher Request-Typ (z. B. ein `ebicsRequest` mit OrderType `HPB` statt `ebicsNoPubKeyDigestsRequest`) | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

### ⚠️ Spec-Vorbehalte

- **Request-Signatur (X002) wird nicht geprüft.** Der HPB-Request ist X002-signiert; die
  Prüfung der `AuthSignature` ist **M4** (die Verify-Stufe bleibt No-Op wie bei INI/HIA). Die
  Vertraulichkeit bleibt gewahrt, weil die Antwort mit dem **E002-Schlüssel des Teilnehmers**
  verschlüsselt ist — nur dessen privater Schlüssel kann sie entschlüsseln.
- **Antwort ist unsigniert** — die Antwort-Authentifikationssignatur (X002) ist ebenfalls
  **M4** (konsistent mit `EbicsResponseFactory`); strikte Clients könnten unsignierte Antworten
  ablehnen.
- **`Ready` wird vorausgesetzt.** HPB verlangt einen `Ready`-Teilnehmer (INI + HIA gelaufen).
  Die EBICS-Praxis erlaubt HPB u. U. schon vor der finalen Freischaltung; diese Vereinfachung
  ist gegen den offiziellen Ablauf zu verifizieren (vgl. [hia.md](hia.md)).
- **Auto-generierte Bankschlüssel.** Der Emulator erzeugt das Bankschlüsselpaar bei Bedarf und
  hält es nur im Speicher; über `IServerBankKeyStore.SetAsync` ist ein festes/gepersistetes Paar
  einsetzbar.
- **H005:** die Bankschlüssel werden als frisch **self-signed** Zertifikate ausgeliefert; eine
  Zertifikatsketten-Prüfung ist ein Conformance-Thema (**M8**). Trust entsteht über den
  Public-Key-Fingerprint, nicht die Kette.
- **Nur `E002`-Verschlüsselungsschlüssel werden bedient.** Die Antwort wird über die
  Transportverschlüsselung `EncryptionE002` (RSA-OAEP) für den Teilnehmerschlüssel verschlüsselt.
  Ein hochgeladener **Legacy-`E001`-Schlüssel** (bei HIA auf H003/H004 zulässig, PKCS#1-v1.5) wird
  vom E002-Baustein nicht unterstützt; HPB scheitert dann mit `061099` (EBICS_INTERNAL_ERROR) statt
  einer fachlichen Ablehnung. `E002` ist der projektweite Standard; ein allgemeiner Verschlüsselungs-
  Dispatch (E001/PKCS#1) ist nicht Teil von #28.
- Die konkreten Codes (`091002`, `090004`) sind gegen den offiziellen EBICS-Annex 1 zu
  verifizieren; der zentrale Returncode-Katalog kommt mit **#36 (M4)**. Der `E002`-OAEP-vs-PKCS1-
  sowie IV-/Kompressions-Vorbehalt liegt in den Krypto-Bausteinen (`EncryptionE002`,
  `EbicsCompression`).

## EBICS-Versionsbezug

| Version | Order-Data | Schlüsseltransport | OrderType-Feld |
| --- | --- | --- | --- |
| H003 / H004 | `H00x.HPBResponseOrderData` | `RSAKeyValue` (Modulus/Exponent) je Schlüssel | `OrderType` |
| H005 | `H005.HPBResponseOrderData` | `X509Data` (self-signed Zertifikat) je Schlüssel | `AdminOrderType` |

Die Bankschlüssel-Versionen sind `X002` (Auth) und `E002` (Enc) — die für alle unterstützten
Protokollversionen zulässigen Defaults (`KeyVersions.Default`). Der `EncryptionPubKeyDigest`
verwendet SHA-256 (`http://www.w3.org/2001/04/xmlenc#sha256`) über den E002-Schlüssel des
Teilnehmers.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten
Core-Bindings, keine proprietären Fixtures):

- `HpbOrderHandlerTests` — End-to-End über `EbicsRequestPipeline`, `[Theory]` über H003/H004/H005:
  Happy Path (Antwort `ebicsKeyManagementResponse` `000000`, Response tatsächlich mit dem privaten
  E002-Schlüssel des Teilnehmers **entschlüsselt**, Bankschlüssel = Inhalt des `IServerBankKeyStore`,
  `EncryptionPubKeyDigest` geprüft, Teilnehmer bleibt `Ready`), Stabilität über wiederholte Aufrufe,
  voller **INI → HIA → HPB**-Durchlauf (HPB mit dem bei HIA übermittelten Schlüssel entschlüsselbar)
  plus Negativfälle: Teilnehmer `New`/`Initialized`, unbekannter Teilnehmer, `Ready` ohne E002-Key
  (`091002`).
- `InMemoryServerBankKeyStoreTests` — GetOrCreate cacht/stabil pro Host (X002/E002, mit privatem
  Schlüssel), unterscheidet sich je Host, `SetAsync` überschreibt/validiert.

## Verwandte Doku

- [INI — Senden der Signaturschlüssel (A00x)](ini.md) — erster Onboarding-Schritt
- [HIA — Senden der Auth- & Enc-Schlüssel (X002/E002)](hia.md) — zweiter Onboarding-Schritt, liefert den E002-Empfängerschlüssel
- [Hostable Server-Grundgerüst](host.md) — Host, Pipeline, Returncodes, Response-Factory
- [Stammdatenverwaltung](master-data.md) — Teilnehmer-Lebenszyklus, `IMasterDataManager`, Store
- [Onboarding-Flows INI / HIA / HPB](../connector/onboarding.md) — der Client-Gegenpart
- [Public-Key-Fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — Fingerprint-Abgleich der Bankschlüssel
- [Transportverschlüsselung E002](../protocol/encryption-e002.md) — E002-Hybrid (AES + RSA-OAEP)

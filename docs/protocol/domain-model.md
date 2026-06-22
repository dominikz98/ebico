# Domänenmodell: Bank / Partner / User / Subscriber (H003/H004/H005)

Die ersten handgeschriebenen Domänen-Primitives in `EBICO.Core`: typsichere
Identifikatoren, der Subscriber-Lebenszyklus und die Berechtigungs-/Signaturklassen.
Bis hierher existierten `HostID`/`PartnerID`/`UserID`/`SystemID` nur als rohe
`string`-Felder auf den [generierten Bindings](xsd-bindings.md) (z. B.
`StaticHeaderType.HostId`). Issue **#16** (Milestone M1),
Konvention: [ADR-0007](../adr/0007-domaenen-value-objects-record-struct.md).

> **Abgrenzung:** Bewusst nur Identität, Zustand und Berechtigungen. Persistenz,
> Krypto und Key-Management gehören nicht hierher — sie folgen in M2 (Krypto) und M3
> (Server/Stammdaten, u. a. #30). Auftrags-/BTF-Typen sind hier noch freie Strings;
> das typisierte Modell kommt in M5.

## Bausteine

Alle unter `src/EBICO.Core/Domain/` (Namespace `EBICO.Core.Domain`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `HostId`, `PartnerId`, `UserId`, `SystemId` | `Identifiers.cs` | typsichere ID-Value-Objects (`readonly record struct`) |
| `EbicsIdentifier` | `EbicsIdentifier.cs` | gemeinsame Validierung gegen das Schema-Pattern (intern) |
| `SubscriberState` (Enum) | `SubscriberState.cs` | Lebenszyklus: `New`/`Initialized`/`Ready`/`Suspended` |
| `SignatureClass` (Enum) + `SignatureClassExtensions` | `SignatureClass.cs` | Signaturklasse `E`/`A`/`B`/`T` + Transport-vs-Bank-Klassifikation |
| `SubscriberPermission` | `SubscriberPermission.cs` | Berechtigung: Auftragstyp × Signaturklasse |
| `Bank`, `Partner`, `Subscriber` | `Bank.cs`, `Partner.cs`, `Subscriber.cs` | schlanke, unveränderliche Aggregate |
| `EbicsDomainException` (+ abgeleitete) | `DomainExceptions.cs` | Validierungsfehler des Domänenmodells |

## Identifikatoren

Alle vier IDs teilen dieselbe Schema-Restriktion — **1–35 Zeichen aus
`[a-zA-Z0-9,=]`** — und werden deshalb über einen gemeinsamen, internen Validator
(`EbicsIdentifier`, Quellgenerator-Regex) geprüft. Als Value-Objects sind es vier
**distinkte** Typen: eine `UserId` lässt sich nicht versehentlich dort übergeben, wo
eine `PartnerId` erwartet wird.

| ID | Bedeutung | Pflicht | Constraint |
|---|---|---|---|
| `HostId` | Bank-/Server-Endpunkt (`HostID`) | ja | `[a-zA-Z0-9,=]{1,35}` |
| `PartnerId` | Kunde (`PartnerID`) | ja | `[a-zA-Z0-9,=]{1,35}` |
| `UserId` | Teilnehmer (`UserID`) | ja | `[a-zA-Z0-9,=]{1,35}` |
| `SystemId` | technisches System (`SystemID`) | optional (Multi-User) | `[a-zA-Z0-9,=]{1,35}` |

```csharp
var host = HostId.Create("BANKDE01");          // wirft InvalidEbicsIdentifierException bei ungültig

if (UserId.TryCreate(input, out var user))     // nicht-werfende Variante
{
    // user.Value ist garantiert valide
}

HostId.Create("A,B=C");                          // ok: Komma und Gleichheitszeichen sind erlaubt
HostId.Create("AB CD");                          // InvalidEbicsIdentifierException (Leerzeichen)
HostId.Create(new string('X', 36));              // InvalidEbicsIdentifierException (zu lang)
```

> **Caveat (struct-bedingt):** `default(HostId)` / `new HostId()` umgeht die Factory und
> trägt `Value == null`. Gültige Instanzen entstehen ausschließlich über
> `Create`/`TryCreate`. Werte-Gleichheit gilt typweise: zwei `HostId` mit gleichem
> `Value` sind gleich.

## Berechtigungen — Transport- vs. Bankunterschrift

`SignatureClass` ist das versionsunabhängige Domänen-Pendant zum generierten
`AuthorisationLevelType` (in H003/H004/H005 identisch). Die zentrale Unterscheidung
ist **Transport (`T`)** gegen **bankfachlich/autorisierend (`E`/`A`/`B`)**:

| Klasse | Bedeutung | Autorisierend? |
|---|---|---|
| `E` | Einzelunterschrift | ja (`IsBankTechnical`) |
| `A` | Erstunterschrift | ja (`IsBankTechnical`) |
| `B` | Zweitunterschrift | ja (`IsBankTechnical`) |
| `T` | Transportunterschrift (nur Einreichung, keine Autorisierung) | nein (`IsTransportOnly`) |

```csharp
SignatureClass.T.IsTransportOnly();    // true
SignatureClass.E.IsBankTechnical();    // true

var perm = new SubscriberPermission("CCT", SignatureClass.T);  // CCT nur einreichen, nicht freigeben
perm.IsTransportOnly;                                          // true
```

Ein `Subscriber` bündelt seine Berechtigungen und beantwortet daraus:
`CanAuthorize(orderType)` (hält eine bankfachliche Berechtigung) bzw.
`IsTransportOnlyFor(orderType)` (nur Transport für diesen Auftragstyp).

## Subscriber-Zustände

Der Lebenszyklus eines Teilnehmers. Übergänge sind in
`Subscriber.Transition(SubscriberState)` gekapselt; unerlaubte Übergänge werfen
`InvalidSubscriberStateTransitionException`. Da das Aggregat unveränderlich ist,
liefert `Transition` eine **neue** Instanz.

| Zustand | Bedeutung |
|---|---|
| `New` | angelegt, noch keine Schlüssel gesendet (kein INI/HIA) |
| `Initialized` | Signaturschlüssel via INI gesendet, noch nicht einsatzbereit |
| `Ready` | vollständig onboarded und aktiviert |
| `Suspended` | gesperrt, bis zur Reaktivierung |

Erlaubte Übergänge:

| von → nach | erlaubt |
|---|---|
| `New` → `Initialized` | ✅ |
| `Initialized` → `Ready` | ✅ |
| `New`/`Initialized`/`Ready` → `Suspended` | ✅ |
| `Suspended` → `Ready` (Reaktivierung) | ✅ |
| alles andere (inkl. Selbstübergang, Überspringen) | ❌ → Exception |

```csharp
var subscriber = new Subscriber(host, partner, user);   // State = New
subscriber = subscriber.Transition(SubscriberState.Initialized)
                       .Transition(SubscriberState.Ready);
subscriber.Transition(SubscriberState.New);             // InvalidSubscriberStateTransitionException
```

## Aggregate

Schlank und unveränderlich (`sealed class`, Get-only-Properties), analog zu
`EbicsVersionInfo`:

- `Bank` — Identität `HostId`, optionaler `Name`, unterstützte `EbicsVersion`s (Default: alle).
- `Partner` — Identität `PartnerId`, optionaler `Name`; gruppiert Subscriber.
- `Subscriber` — Identität über das Tripel (`HostId`, `PartnerId`, `UserId`), optionale
  `SystemId` (technischer Teilnehmer → `IsTechnicalSubscriber`), `SubscriberState` und
  `SubscriberPermission`s.

## EBICS-Versionsbezug

IDs (Pattern/Länge) und Signaturklassen (`E`/`A`/`B`/`T`) sind über **H003, H004 und
H005 identisch**; das Domänenmodell ist daher versionsunabhängig. Nur die
XML-Namespaces der Schemas unterscheiden sich — das betrifft die
[Bindings](xsd-bindings.md), nicht dieses Modell.

## Tests

`tests/EBICO.Tests/Domain/` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- `IdentifierTests` — gültige Werte & Grenzlängen (1/35), ungültige (leer, zu lang,
  illegale Zeichen, `null`), `TryCreate`, Werte-Gleichheit, `default`-Caveat, alle vier Typen.
- `SignatureClassTests` — `IsTransportOnly`/`IsBankTechnical`, Partition über alle Werte.
- `SubscriberTests` — erlaubte/unerlaubte Zustandsübergänge, Identitäts-/Permission-Erhalt,
  `SystemId`/technischer Teilnehmer, `CanAuthorize`/`IsTransportOnlyFor`.
- `BankPartnerTests` — Konstruktion, Default-Versionen, Identität.

## Verwandtes

- [ADR-0007 — Domänen-Value-Objects als `readonly record struct`](../adr/0007-domaenen-value-objects-record-struct.md)
- [Versions-Dispatch](version-dispatch.md) — die `EbicsVersion`-Abstraktion, auf die `Bank` aufsetzt
- [XSD-Bindings](xsd-bindings.md) — die generierten Typen mit den rohen ID-Feldern und `AuthorisationLevelType`

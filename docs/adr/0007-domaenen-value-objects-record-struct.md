# 0007 — Domänen-Value-Objects als `readonly record struct`

- Status: accepted
- Datum: 2026-06-22

## Kontext

Mit Issue #16 entsteht die erste handgeschriebene Domänenschicht in `EBICO.Core`
(`Domain/`): Identifikatoren (`HostId`, `PartnerId`, `UserId`, `SystemId`) und kleine
Wertobjekte (`SubscriberPermission`). Diese Werte sind unveränderlich, durch ihren
Inhalt definiert (Werte-Gleichheit) und sollen typsicher sein — eine `UserId` darf nicht
versehentlich als `PartnerId` durchgehen, obwohl beide nur einen String kapseln.

Bisher nutzt der handgeschriebene Core ausschließlich `sealed class` mit explizitem
Konstruktor (z. B. `EbicsVersionInfo`); Records kamen noch nicht vor. Für die neuen,
zahlreichen kleinen Wertobjekte ist eine Konvention festzulegen.

## Entscheidung

Domänen-**Value-Objects** werden als `public readonly record struct` umgesetzt:

- privater Konstruktor + statische Factory `Create` (wirft) / `TryCreate` (nicht-werfend),
  damit nur validierte Instanzen entstehen;
- Werte-Gleichheit und `GetHashCode` vom Record automatisch, kein Boilerplate;
- `struct` (allokationsfrei) für die typischerweise kleinen, kurzlebigen IDs.

Größere **Aggregate mit Identität** (`Bank`, `Partner`, `Subscriber`) bleiben bei der
bestehenden Konvention `sealed class` (unveränderlich, Get-only-Properties).

## Konsequenzen

- Typsicherheit und Werte-Semantik ohne manuelles `Equals`/`==`.
- **Caveat:** Ein `struct` hat stets einen impliziten parameterlosen Konstruktor;
  `default(HostId)` / `new HostId()` umgeht damit die Validierung und trägt
  `Value == null`. Konvention: Instanzen nur über `Create`/`TryCreate` erzeugen; der
  `default`-Fall ist dokumentiert und durch Tests abgedeckt.
- Neue Konvention im Projekt — Records sind ab hier für Value-Objects erlaubt und werden
  in der Doku ([domain-model.md](../protocol/domain-model.md)) referenziert.

## Alternativen

- **`sealed class` auch für IDs:** konsistent mit dem bestehenden Code, aber viel
  Boilerplate (`Equals`/`GetHashCode`/`==`/`!=` von Hand) und eine Heap-Allokation je ID.
- **Ein generischer `EbicsId`-Typ:** weniger Code, aber keine Typsicherheit zwischen den
  vier ID-Arten — verworfen.
- **Roher `string`:** keine Validierung an der Quelle, keine Typsicherheit — verworfen.

# Versionsabstraktion / Protokoll-Dispatch (H003/H004/H005)

Die zentrale Abstraktion in `EBICO.Core`, über die Server und Connector
versionsabhängig arbeiten, ohne die Logik dreifach zu duplizieren. Sie ist der
Dreh- und Angelpunkt zwischen dem `EbicsVersion`-Enum und den generierten
[XSD-Bindings](xsd-bindings.md). Issue **#14** (Milestone M1),
Entwurf: [ADR-0004](../adr/0004-multi-version-strategie.md).

## Bausteine

Alle unter `src/EBICO.Core/` (Bindings unter `Schema/`, der Rest unter `Versioning/`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `EbicsVersion` (Enum) | `EbicsVersion.cs` | `H003`/`H004`/`H005` — der Diskriminator |
| `EbicsVersionInfo` | `Versioning/EbicsVersionInfo.cs` | unveränderliche Metadaten je Version (Code, Namespace, 6 Envelope-CLR-Typen) |
| `EbicsVersions` | `Versioning/EbicsVersions.cs` | statische Registry (Single Source of Truth) + Reverse-Lookups |
| `IEbicsEnvelope` (+ Request/Response-Marker) | `Versioning/IEbicsEnvelope.cs` u. a. | versionsunabhängige Sicht auf jedes Envelope |
| `EbicsVersionDetector` | `Versioning/EbicsVersionDetector.cs` | erkennt die Version aus rohem XML (Inbound-Dispatch) |
| `EbicsVersion*Exception` | `Versioning/EbicsVersionExceptions.cs` | Fehler beim Erkennen/Dispatchen |

## Registry (`EbicsVersions`)

Die eine Stelle, die das Enum mit Schema-Code, Wurzel-Namespace und den
Envelope-Bindings verdrahtet:

- `All` — alle Versionen, geordnet von alt (H003) nach neu (H005).
- `Get(EbicsVersion)` → `EbicsVersionInfo` (wirft `ArgumentOutOfRangeException` bei
  undefiniertem Enum-Wert). So wählt aufrufender Code die Zielversion:
  z. B. `EbicsVersions.Get(options.Version).RequestType`.
- `TryFromNamespace(string?, out EbicsVersionInfo?)` — Reverse-Lookup über den
  Wurzel-Namespace; kennt den **H003-Legacy-Sonderfall**.
- `TryFromCode(string?, out EbicsVersionInfo?)` — Reverse-Lookup über den
  vierstelligen Code (z. B. `"H005"`). Beide Lookups vergleichen **ordinal**
  (case-sensitiv) und liefern bei Unbekanntem/`null` einfach `false`.

| Version | Code | Wurzel-Namespace |
|---|---|---|
| H003 | `H003` | `http://www.ebics.org/H003` (legacy) |
| H004 | `H004` | `urn:org:ebics:H004` |
| H005 | `H005` | `urn:org:ebics:H005` |

## Envelope-Schnittstellen & Partial-Wiring

`IEbicsEnvelope` bietet die versionsunabhängige Sicht (`Version`, `Revision`,
`ProtocolVersion`). Die Marker `IEbicsRequestEnvelope` / `IEbicsResponseEnvelope`
trennen Sende- (`ebicsRequest`, `ebicsUnsecuredRequest`, `ebicsUnsignedRequest`,
`ebicsNoPubKeyDigestsRequest`) von Empfangsrichtung (`ebicsResponse`,
`ebicsKeyManagementResponse`).

`Version`/`Revision` liefern bereits die generierten Bindings (via ihres
per-Version-`IVersionAttrGroup`). Hinzu kommt nur `ProtocolVersion` — aus dem CLR-
Namespace abgeleitet und daher zuverlässig, **unabhängig vom (frei wählbaren)
`@Version`-Attribut auf der Leitung**.

Die Anbindung geschieht über **hand­geschriebene partielle Klassen**
(`src/EBICO.Core/Versioning/Bindings/EnvelopeBindings.{H003,H004,H005}.cs`):

```csharp
namespace EBICO.Core.Schema.H005;

public partial class EbicsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}
```

> **Warum nicht neben den generierten Dateien in `Schema/{Hxxx}/`?**
> `scripts/generate-bindings.sh` löscht und erzeugt diese Ordner bei jedem Lauf neu
> (`rm -rf`, vgl. [XSD-Bindings → Regenerierung](xsd-bindings.md#tooling--regenerierung)).
> Handgeschriebener Code dort ginge verloren. Der C#-Namespace bleibt trotzdem
> `EBICO.Core.Schema.Hxxx` — Ordner ≠ Namespace, das SDK kompiliert alle `*.cs`.

## Versionserkennung (`EbicsVersionDetector`)

Erkennt die Version eines rohen Envelopes, **ohne** das ganze Dokument zu
deserialisieren — es wird nur das Wurzelelement via `XmlReader` gelesen. Der
Wurzel-Namespace ist der Diskriminator (aufgelöst über `TryFromNamespace`).

- `Detect(string)` / `Detect(string, bool strict)` / `Detect(Stream, bool strict = false)`
  → `EbicsVersionInfo`. Der Stream wird **nicht** geschlossen.
- `TryDetect(…, out EbicsVersionInfo?)` → `bool` (nicht-werfende, lenient Variante).

**Lenient als Default:** Das `@Version`-Attribut ist freier Text auf der Leitung;
maßgeblich ist der Namespace, weil er bestimmt, welches Schema greift. `strict: true`
verlangt zusätzlich, dass ein vorhandenes `@Version` zum Namespace passt.

| Eingabe | Ergebnis |
|---|---|
| Wurzel in bekanntem Namespace (inkl. H003-Legacy) | `EbicsVersionInfo` |
| unbekannter / fehlender Namespace | `EbicsVersionNotSupportedException` |
| `null` | `ArgumentNullException` (kein Versions-Fehler) |
| leer / nur Whitespace | `EbicsEnvelopeFormatException` |
| kein XML / abgeschnittenes Tag / kein Wurzelelement / DOCTYPE | `EbicsEnvelopeFormatException` |
| `strict` und `@Version`-Code ≠ Namespace-Code | `EbicsVersionMismatchException` |
| `strict` und `@Version` fehlt | OK (Namespace-Version) |
| lenient und `@Version` widerspricht Namespace | OK (Namespace gewinnt) |

> **Sicherheit:** Der Reader läuft mit `DtdProcessing.Prohibit` und
> `XmlResolver = null` — ein `<!DOCTYPE …>` wird abgelehnt (XXE-Härtung), da der
> Server unvertrautes XML verarbeitet.

`TryDetect` schluckt nur `EbicsVersionException` (also leeres/fehlerhaftes/unbekanntes
XML) und liefert dann `false`; ein `null`-Argument bleibt eine `ArgumentNullException`,
weil es ein Aufrufer-Bug ist, keine schlechten Eingabedaten.

## Verwendung

```csharp
// Zielversion auswählen (z. B. Connector-DI: o.Version = EbicsVersion.H005)
var requestType = EbicsVersions.Get(options.Version).RequestType;

// Inbound-Dispatch im Server: Version aus den Bytes der Anfrage erkennen
var info = EbicsVersionDetector.Detect(rawRequestXml);
// info.Version / info.RequestType → die passende Bindings-Familie
```

## Tests

`tests/EBICO.Tests/Versioning/` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- `EbicsVersionsTests` — `All`-Reihenfolge, `Get` (inkl. CLR-Typ-Verdrahtung und
  Out-of-Range), `TryFromNamespace`/`TryFromCode` (bekannt inkl. H003-Legacy,
  unbekannt, `null`, Case-Sensitivität).
- `EnvelopeBindingWiringTests` — alle 18 Envelopes implementieren den richtigen
  Marker und melden die korrekte `ProtocolVersion`; `Version`/`Revision` über das
  Interface round-trippen.
- `EbicsVersionDetectorTests` — Erfolgs- und alle vier Exception-Pfade, lenient vs.
  strict, Stream, Prolog/Kommentar, DOCTYPE-Härtung, `TryDetect`.

## Verwandtes

- [ADR-0004 — Multi-Version-Strategie](../adr/0004-multi-version-strategie.md)
- [XSD-Bindings](xsd-bindings.md) — die generierten Klassen, auf denen dies aufsetzt
- [Connector-Architektur](../connector/architecture.md) — die app-seitige
  `IEbicsRequest<TResult>`-Abstraktion (anderer Layer als `IEbicsRequestEnvelope`)

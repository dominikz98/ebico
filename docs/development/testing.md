# Test-Harness & Fixtures

Beschreibt das Test-Setup von EBICO. Gehört zu Issue **#8 — Test-Harness &
Fixtures** (Milestone M0).

## Framework: xUnit v3 + AwesomeAssertions

- **xUnit v3** (`xunit.v3` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)
  ist das Testframework. Das Testprojekt `tests/EBICO.Tests` ist ausführbar
  (`OutputType=Exe`, von xUnit v3 verlangt) und referenziert `EBICO.Core`,
  `EBICO.Connector` und `EBICO.Server`.
- **AwesomeAssertions** liefert die fluente Assertion-API (`value.Should()…`).

> **Warum AwesomeAssertions statt FluentAssertions?** FluentAssertions ist seit
> v8 (Januar 2025) kommerziell lizenziert (Xceed) und damit für ein öffentliches
> OSS-Repo ungeeignet. [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions)
> ist ein MIT-lizenzierter Fork der FluentAssertions-v7-API — gleiche `Should()`-
> Syntax. Hinweis: Der Root-Namespace ist `AwesomeAssertions` (nicht
> `FluentAssertions`).

Ausführen:

```bash
dotnet test                 # alle Tests
dotnet test --collect:"XPlat Code Coverage"   # mit Coverage (wie in der CI)
```

## Verzeichnis-Layout

```
tests/EBICO.Tests/
├── Core/                       # Tests zu EBICO.Core (z. B. EbicsVersion)
├── E2E/                        # Connector ↔ Server Round-Trips (#57, #58)
├── Conformance/                # Konformität gegen reale Fremd-Clients (#59)
│   └── Vendor/<client>/<VERSION>/request/  # committete OSS-Client-Captures (kein gitignore)
├── Infrastructure/             # Harness-Helfer + deren Tests
│   ├── CanonicalXmlComparer.cs
│   ├── TestCertificates.cs
│   └── SampleXml.cs
└── Fixtures/
    ├── Xml/<VERSION>/<direction>/   # EBICS-Beispiel-XML (proprietär, nicht eingecheckt)
    └── Keys/                        # Schlüssel-Fixtures (in-process generiert)
```

Die übrigen Ordner folgen dem **Prüfgegenstand** (`Connector/`, `Server/`, `Suite/`, `Schema/`, …).
`E2E/` fällt bewusst in keine dieser Schichten: Prüfgegenstand ist dort die *Nahtstelle zwischen zwei*
von ihnen — ein Fehler auf beiden Seiten lässt diese Tests rotlaufen. **`Conformance/`** (Issue #59)
prüft EBICO gegen **echte, fremde Clients**: committete **Vendor-Captures** unter
`Conformance/Vendor/<client>/<version>/request/` (Output eines OSS-Clients, committbar — nicht
`.gitignore`d, anders als `Fixtures/Xml/`), plus Parser-/Wire-Shape-Toleranz (`XmlShape`),
C14N-Adaptivität und Known-Gap-Negativfälle. Siehe
[Konformität gegen reale Clients](conformance-real-clients.md).

Der Ordner `Fixtures/**` wird in den Build-Output kopiert
(`CopyToOutputDirectory`), damit die Helfer die Dateien zur Laufzeit relativ zum
Test-Assembly finden.

## Helfer

### `CanonicalXmlComparer` — kanonisierter XML-Vergleich

Vergleicht XML nach **Canonical XML 1.0** (C14N) — die Kanonisierung, auf die
EBICS-XML-Signaturen aufsetzen. Die Kanonform liefert seit #15 der **produktive**
Canonicalizer (`EBICO.Core.Serialization.XmlCanonicalizer`, Modus `Inclusive`),
an den dieser Test-Helfer delegiert; zusätzlich verwirft er belanglosen Whitespace,
sodass reine Formatierungsunterschiede gleich verglichen werden. Unempfindlich gegen
belanglose Whitespace/Einrückung, Attribut-Reihenfolge und Reihenfolge der
Namespace-Deklarationen; empfindlich gegen Inhalt und Struktur.

```csharp
CanonicalXmlComparer.AreEqual("<a><b/></a>", "<a>\n  <b></b>\n</a>");  // true
```

Eigene Unit-Tests decken Happy Path (Whitespace, Attribut-Reihenfolge,
leeres Element ↔ explizites Schließen) und Negativ-/Grenzfälle (abweichender
Inhalt/Attributwert, `null`, nicht-wohlgeformtes XML) ab. Die produktive
C14N-Implementierung (inkl./exkl.) liegt in
[XML-Serialisierung & C14N](../protocol/serialization-c14n.md) (Issue #15).

### `TestCertificates` — Schlüssel- und Zertifikat-Fixtures

Erzeugt **in-process** self-signed X.509-Zertifikate und RSA-Schlüsselpaare für
Krypto-/Onboarding-Tests (M2/M3). Es liegt **kein** echtes oder proprietäres
Schlüsselmaterial im Repo. Details: [Fixtures/Keys/README](../../tests/EBICO.Tests/Fixtures/Keys/README.md).

### `SampleXml` — Loader für Beispiel-XML

Lädt EBICS-Beispiele aus `Fixtures/Xml/<VERSION>/<direction>/`. Da die offiziellen
Beispiele proprietär und **nicht eingecheckt** sind, liefert `TryLoad` bei
fehlender Datei `false`; Tests überspringen sich dann via `Assert.Skip` — die
Suite bleibt auch ohne Beispiele (z. B. in der CI) grün. Details:
[Fixtures/Xml/README](../../tests/EBICO.Tests/Fixtures/Xml/README.md).

## Gegenstelle: Fake vs. echt

Quer zur bekannten Tier-A/Tier-B-Achse (*ohne* vs. *mit* proprietären Beispiel-XML, siehe
[XSD-Bindings](../protocol/xsd-bindings.md)) gibt es seit #57 eine zweite Unterscheidung: **womit
spricht die getestete Seite?**

- **Fake-Gegenstelle** — der Regelfall. `OnboardingTestHarness`, `FakeUploadServer`,
  `FakeDownloadServer` bauen die Bankantwort selbst; `ServerTestHelpers` baut umgekehrt das
  Request-XML. Schnell und präzise steuerbar (Fehlerinjektion!), prüft aber jede Seite nur gegen ein
  *Modell* der anderen.
- **Echte Gegenstelle** — [`E2E/`](e2e-connector-server.md). Der echte Connector spricht gegen den
  in-process gehosteten echten Server. Findet genau die Klasse von Fehlern, die Fakes bauartbedingt
  verstecken: Annahmen, die beide Seiten konsistent, aber falsch teilen.

Beides ist **Tier A** — es geht hier nicht um Lizenz/CI-Tauglichkeit, sondern um die Aussagekraft.

## Lizenz-Hinweis

Wie die Schemas sind die **EBICS-Beispiel-XML proprietär (EBICS SC)** und werden
nicht committet (`.gitignore`: `tests/**/Fixtures/Xml/**/*.xml`). Siehe
[../protocol/schema-sources.md](../protocol/schema-sources.md) und Lizenz-Issue #5.

# Test harness & fixtures

Describes EBICO's test setup. Belongs to issue **#8 — Test harness &
fixtures** (Milestone M0).

## Framework: xUnit v3 + AwesomeAssertions

- **xUnit v3** (`xunit.v3` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)
  is the test framework. The test project `tests/EBICO.Tests` is executable
  (`OutputType=Exe`, required by xUnit v3) and references `EBICO.Core`,
  `EBICO.Connector` and `EBICO.Server`.
- **AwesomeAssertions** provides the fluent assertion API (`value.Should()…`).

> **Why AwesomeAssertions instead of FluentAssertions?** FluentAssertions has been
> commercially licensed (Xceed) since v8 (January 2025) and is thus unsuitable for a public
> OSS repo. [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions)
> is an MIT-licensed fork of the FluentAssertions v7 API — same `Should()`
> syntax. Note: the root namespace is `AwesomeAssertions` (not
> `FluentAssertions`).

Running:

```bash
dotnet test                 # all tests
dotnet test --collect:"XPlat Code Coverage"   # with coverage (as in CI)
```

## Directory layout

```
tests/EBICO.Tests/
├── Core/                       # tests for EBICO.Core (e.g. EbicsVersion)
├── E2E/                        # connector ↔ server round-trips (#57, #58)
├── Conformance/                # conformance against real third-party clients (#59)
│   └── Vendor/<client>/<VERSION>/request/  # committed OSS client captures (not gitignored)
├── Infrastructure/             # harness helpers + their tests
│   ├── CanonicalXmlComparer.cs
│   ├── TestCertificates.cs
│   └── SampleXml.cs
└── Fixtures/
    ├── Xml/<VERSION>/<direction>/   # EBICS sample XML (proprietary, not checked in)
    └── Keys/                        # key fixtures (generated in-process)
```

The remaining folders follow the **subject under test** (`Connector/`, `Server/`, `Suite/`, `Schema/`, …).
`E2E/` deliberately falls into none of these layers: the subject under test there is the *seam between two*
of them — an error on both sides makes these tests run red. **`Conformance/`** (issue #59)
tests EBICO against **real, third-party clients**: committed **vendor captures** under
`Conformance/Vendor/<client>/<version>/request/` (output of an OSS client, committable — not
`.gitignore`d, unlike `Fixtures/Xml/`), plus parser/wire-shape tolerance (`XmlShape`),
C14N adaptivity and known-gap negative cases. See
[Conformance against real clients](conformance-real-clients.md).

The folder `Fixtures/**` is copied into the build output
(`CopyToOutputDirectory`), so that the helpers find the files at runtime relative to
the test assembly.

## Helpers

### `CanonicalXmlComparer` — canonicalized XML comparison

Compares XML by **Canonical XML 1.0** (C14N) — the canonicalization that
EBICS XML signatures build on. The canonical form is delivered since #15 by the **production**
canonicalizer (`EBICO.Core.Serialization.XmlCanonicalizer`, mode `Inclusive`),
to which this test helper delegates; in addition it discards insignificant whitespace,
so that pure formatting differences compare as equal. Insensitive to
insignificant whitespace/indentation, attribute order and order of the
namespace declarations; sensitive to content and structure.

```csharp
CanonicalXmlComparer.AreEqual("<a><b/></a>", "<a>\n  <b></b>\n</a>");  // true
```

Its own unit tests cover the happy path (whitespace, attribute order,
empty element ↔ explicit closing) and negative/edge cases (deviating
content/attribute value, `null`, non-well-formed XML). The production
C14N implementation (incl./excl.) is in
[XML serialization & C14N](../protocol/serialization-c14n.md) (issue #15).

### `TestCertificates` — key and certificate fixtures

Creates **in-process** self-signed X.509 certificates and RSA key pairs for
crypto/onboarding tests (M2/M3). There is **no** real or proprietary
key material in the repo. Details: [Fixtures/Keys/README](../../tests/EBICO.Tests/Fixtures/Keys/README.md).

### `SampleXml` — loader for sample XML

Loads EBICS examples from `Fixtures/Xml/<VERSION>/<direction>/`. Since the official
examples are proprietary and **not checked in**, `TryLoad` returns `false` when a file is
missing; tests then skip themselves via `Assert.Skip` — the
suite stays green even without examples (e.g. in CI). Details:
[Fixtures/Xml/README](../../tests/EBICO.Tests/Fixtures/Xml/README.md).

## Counterpart: fake vs. real

Orthogonal to the familiar Tier-A/Tier-B axis (*without* vs. *with* proprietary sample XML, see
[XSD bindings](../protocol/xsd-bindings.md)) there is, since #57, a second distinction: **what does
the tested side talk to?**

- **Fake counterpart** — the regular case. `OnboardingTestHarness`, `FakeUploadServer`,
  `FakeDownloadServer` build the bank response themselves; `ServerTestHelpers` conversely builds the
  request XML. Fast and precisely controllable (error injection!), but checks each side only against a
  *model* of the other.
- **Real counterpart** — [`E2E/`](e2e-connector-server.md). The real connector talks against the
  in-process hosted real server. Finds exactly the class of errors that fakes hide by
  construction: assumptions that both sides share consistently, but wrongly.

Both are **Tier A** — the point here is not license/CI suitability, but expressiveness.

## License note

Like the schemas, the **EBICS sample XML is proprietary (EBICS SC)** and is
not committed (`.gitignore`: `tests/**/Fixtures/Xml/**/*.xml`). See
[../protocol/schema-sources.md](../protocol/schema-sources.md) and license issue #5.

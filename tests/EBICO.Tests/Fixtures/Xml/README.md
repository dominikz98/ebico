# Sample XML fixtures

This is where the EBICS sample messages live, per protocol version and direction:

```
Xml/<VERSION>/<direction>/<file>.xml
   VERSION   = H003 | H004 | H005
   direction = request | response
```

They are loaded via the helper
[`SampleXml`](../../Infrastructure/SampleXml.cs):

```csharp
if (SampleXml.TryLoad(EbicsVersion.H005, SampleDirection.Request, "ebicsRequest_HPB.xml", out var xml))
{
    // ... check against CanonicalXmlComparer
}
```

## ⚠️ License: samples are NOT checked in

The official EBICS sample XML comes from ebics.org and is the
**proprietary property of the EBICS SC** — just like the schemas themselves. It is
therefore **not** committed to this repo; `.gitignore` excludes
`tests/**/Fixtures/Xml/**/*.xml` (cf. license issue #5 and
`docs/protocol/schema-sources.md`).

Source: <https://www.ebics.org/en/technical-information/examples>

## Providing them locally

Download the samples manually from ebics.org and put the `.xml` files into the
matching subfolders. Tests that need samples **skip themselves**
(`Assert.Skip`) when the files are missing — so the suite stays green even
without them (e.g. in CI).

The `.gitkeep` files only keep the directory structure in the repo.

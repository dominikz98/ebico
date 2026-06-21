# Sample-XML-Fixtures

Hier liegen EBICS-Beispiel-Nachrichten je Protokollversion und Richtung:

```
Xml/<VERSION>/<direction>/<datei>.xml
   VERSION   = H003 | H004 | H005
   direction = request | response
```

Geladen werden sie über den Helfer
[`SampleXml`](../../Infrastructure/SampleXml.cs):

```csharp
if (SampleXml.TryLoad(EbicsVersion.H005, SampleDirection.Request, "ebicsRequest_HPB.xml", out var xml))
{
    // ... gegen CanonicalXmlComparer prüfen
}
```

## ⚠️ Lizenz: Beispiele werden NICHT eingecheckt

Die offiziellen EBICS-Beispiel-XML stammen von ebics.org und sind
**proprietäres Eigentum der EBICS SC** — wie die Schemas selbst. Sie werden
daher **nicht** in dieses Repo committet; `.gitignore` schließt
`tests/**/Fixtures/Xml/**/*.xml` aus (vgl. Lizenz-Issue #5 und
`docs/protocol/schema-sources.md`).

Quelle: <https://www.ebics.org/en/technical-information/examples>

## Lokal bereitstellen

Lade die Beispiele manuell von ebics.org und lege die `.xml`-Dateien in die
passenden Unterordner. Tests, die Beispiele brauchen, **überspringen sich
selbst** (`Assert.Skip`), wenn die Dateien fehlen — die Suite bleibt also auch
ohne sie (z. B. in der CI) grün.

Die `.gitkeep`-Dateien halten nur die Verzeichnisstruktur im Repo.

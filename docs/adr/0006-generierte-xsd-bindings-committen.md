# 0006 — Generierte XSD-Bindings committen (Option B)

- Status: accepted
- Datum: 2026-06-21

## Kontext

M1 erzeugt aus den EBICS-XSDs C#-Bindings (Issues #11–#13). [ADR-0003](0003-umgang-mit-proprietaeren-schemas.md)
hat die XSDs selbst aus dem Repo ausgeschlossen und die **Folgefrage offen
gelassen**, ob die generierten Bindings als *derivative work* gelten und committet
werden dürfen. Die Optionen sind in [../legal/ebics-licensing.md](../legal/ebics-licensing.md)
beschrieben:

- **(A)** Bindings nicht committen, beim Build aus lokalen XSDs generieren.
- **(B)** Bindings committen (XSDs bleiben ungetrackt).
- **(C)** Handgeschriebene Modelle.

Option (A) hat einen schweren Nachteil: Ohne lokal bezogene (proprietäre) XSDs ist
der schema-abhängige Teil von `EBICO.Core` **nicht baubar** — die CI (die keine
Schemas hat) könnte die Bindings und alles darauf Aufbauende weder kompilieren noch
testen. Das würde den Kern des Projekts dauerhaft von der CI-Absicherung ausnehmen.

## Entscheidung

**Option (B): Die generierten Bindings werden committet; die XSDs bleiben
ungetrackt (`.gitignore`, ADR-0003).**

- Quelle der Wahrheit für den Build sind die **committeten `.cs`** unter
  `src/EBICO.Core/Schema/`. CI und Contributor bauen/testen ohne Schemas.
- Die Generierung ist **reproduzierbar**: `dotnet-xscgen` exakt gepinnt in
  `.config/dotnet-tools.json`, getrieben von `scripts/generate-bindings.sh`. Sie
  ist ein **Maintainer-Schritt** (nach einem Schema-Update), kein Build-Schritt.
- Details zu Tool, Namespaces und Layout: [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md).
- **Lizenz:** Die schriftliche Genehmigung der EBICS SC wird **parallel** verfolgt
  (`info@ebics.de`); M1 wird darauf nicht blockiert. Es werden nur generierte
  Artefakte committet, nicht der XSD-Originaltext; die Herkunft ist dokumentiert.

## Konsequenzen

- **CI deckt den Protokoll-Kern real ab** (Build + Round-Trip-Tests laufen ohne
  Schemas). Bindings sind im Diff reviewbar.
- Bei Schema-Updates müssen die Bindings neu generiert und mit-committet werden;
  ein nicht-deterministischer Generator-Wechsel würde rauschen — daher der exakte
  Versions-Pin.
- **Restrisiko (Lizenz):** Sollte die EBICS SC widersprechen, lassen sich die
  Bindings entfernen/neu generieren — die XSDs waren nie committet. Dies ist
  **kein Rechtsrat** (vgl. `ebics-licensing.md`); die finale Verantwortung liegt
  beim Betreiber.

## Alternativen

- **(A) Generierung beim Build, nicht committen** — verworfen: macht den Kern in
  der CI nicht baubar/testbar.
- **(C) Handgeschriebene Modelle** — verworfen: hoher Aufwand und Fehlerrisiko bei
  der großen EBICS-Schemafläche; kein direkter Mehrwert gegenüber generierten,
  reviewten Bindings.

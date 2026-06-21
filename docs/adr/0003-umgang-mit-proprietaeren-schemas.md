# 0003 — Umgang mit proprietären EBICS-Schemas

- Status: accepted
- Datum: 2026-06-21

## Kontext

Die EBICS-Schemas (XSDs), Spezifikationen und offiziellen Beispiel-XML sind
**proprietäres Eigentum der EBICS SC**. Reproduktion mit Copyright-Vermerk ist
erlaubt, Modifikation/derivative uses nicht ohne Genehmigung. Das Projekt braucht
die Schemas zum Bauen der Bindings (ab M1), darf sie aber nicht ungeprüft
veröffentlichen.

## Entscheidung

- **Keine XSDs und keine offiziellen Beispiel-XML im Repository.** `.gitignore`
  schließt `schemas/**/*.xsd`, die Manifeste und `tests/**/Fixtures/Xml/**/*.xml`
  aus.
- **Lokaler, reproduzierbarer Bezug** über `scripts/fetch-schemas.sh` (manueller
  Download → entpacken/sortieren/SHA-256-Manifest).
- Tests, die offizielle Beispiele brauchen, **überspringen sich** (`Assert.Skip`),
  wenn die Dateien fehlen — die Suite bleibt in der CI grün.

Vollständige Einordnung: [../legal/ebics-licensing.md](../legal/ebics-licensing.md).

## Konsequenzen

- Lizenzkonform: keine proprietären Inhalte im öffentlichen Repo.
- Contributor/CI müssen Schemas (und ggf. Beispiele) lokal beziehen, um die
  schema-abhängigen Teile zu bauen/zu testen.
- **Folgeentscheidung (M1-Gate) — entschieden:** ob generierte **Bindings**
  committet werden — geklärt in [ADR-0006](0006-generierte-xsd-bindings-committen.md)
  (Option B: Bindings committen, XSDs bleiben ungetrackt).

## Alternativen

- **XSDs/Beispiele committen:** beste DX, aber ohne Genehmigung lizenzrechtlich
  riskant — verworfen (bis ggf. Genehmigung vorliegt).

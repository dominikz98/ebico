# 0024 — NuGet-Packaging & Versionierung (CalVer) des Connectors

- Status: accepted
- Datum: 2026-07-17

## Kontext

M6 (#50) verlangt, `EBICO.Connector` als NuGet-Paket auslieferbar zu machen: Paketmetadaten, Symbols,
README, Beispiele und eine Versionierungs-Strategie. Ausgangslage: keinerlei Packaging-Metadaten, keine
`LICENSE`, keine Versionierung/Tags, kein `samples/`-Ordner. Der Connector referenziert `EBICO.Core` per
`ProjectReference` — ein auslieferbares Connector-Paket braucht Core daher ebenfalls als Paket.

Mehrere Punkte waren zu entscheiden: Lizenz, Versionsschema, Paket-Granularität (ein vs. zwei Pakete),
Aufbau des Beispiels und die Abgrenzung zum Publish (M9 / #62).

## Entscheidung

1. **Zwei Pakete:** `EBICO.Core` und `EBICO.Connector` werden beide gepackt (`IsPackable=true`); der
   Connector hängt als Paket-Abhängigkeit am Core-Paket **gleicher Version**. Core wird **nicht** in den
   Connector eingebettet (keine DLL-Bündelung/ILRepack). Server/Suite und Tests bleiben nicht packbar.
2. **Lizenz MIT:** Der EBICO-**Code** steht unter MIT (`PackageLicenseExpression=MIT`, `LICENSE` im
   Repo-Root). Davon unberührt bleiben die proprietären EBICS-Schemas/Specs (nicht Teil der Pakete).
3. **Versionierung: CalVer `{JAHR}.{MONAT}.{BUILD}`** (bewusst **statt** SemVer, auf ausdrücklichen
   Wunsch). `VersionPrefix` wird in `Directory.Build.props` aus UTC-Jahr/-Monat + `EbicoBuildNumber`
   berechnet; BUILD kommt in der CI aus `github.run_number`, lokal Default `0`. NuGet normalisiert die
   Komponenten zu Integern (`2026.07.1` → `2026.7.1`).
4. **Symbols + SourceLink:** `IncludeSymbols=true` + `SymbolPackageFormat=snupkg` und
   `Microsoft.SourceLink.GitHub` (build-only) für Step-Debugging bis in die Commit-Quellen.
5. **Paket-READMEs:** je eine `README.md` in `src/EBICO.Core` und `src/EBICO.Connector`
   (`PackageReadmeFile`), mit absoluten GitHub-Links.
6. **Beispiel:** ein selbstständiger Quickstart (`samples/EBICO.Connector.Quickstart`), der den
   `EBICO.Server` in-process hostet und den vollen Rundlauf fährt — `dotnet run` ohne Setup.
7. **CI:** ein **build-only** `pack`-Job (Core+Connector → Artefakt, kein Push), analog zu
   `container-build`. **Publish/Push** in einen Feed bleibt M9 / #62.

Gemeinsame Metadaten stehen zentral in `Directory.Build.props` (konditioniert auf die zwei Bibliotheken),
projekt-spezifische Felder in der jeweiligen `.csproj`. Details:
[../connector/packaging.md](../connector/packaging.md).

## Konsequenzen

- Beide Pakete sind reproduzierbar packbar; die CI belegt das bei jedem Lauf (u. a. `NU5039` bei
  fehlendem README).
- Ein Konsument zieht `EBICO.Connector` **und** transitiv `EBICO.Core` in exakt gleicher Version.
- **Trade-off CalVer:** die Version kodiert **keine** API-Kompatibilität (anders als SemVer). Breaking
  Changes müssen über Release-Notes/Changelog kommuniziert werden. Der Monat verliert in der
  normalisierten Version die führende Null.
- Der Quickstart referenziert bewusst auch `EBICO.Server` (in-process-Hosting) — er ist ein
  self-contained Demo, keine reine Consumer-Sicht.
- `Authors`/`Company` sind Platzhalter und bei einem offiziellen Release ggf. anzupassen.

## Alternativen

- **SemVer (z. B. via MinVer, tag-getrieben):** üblicheres Schema mit API-Semantik, aber verworfen
  zugunsten der ausdrücklich gewünschten Datumsversionierung.
- **Ein Paket mit eingebettetem Core (ILRepack/DLL-Bündelung):** ein einziges Consumer-Paket, aber
  verworfen — Core wird auch von Server/Suite genutzt und ist eine eigenständige öffentliche
  Bibliothek; Einbetten birgt Typidentitäts-Konflikte. Zwei Pakete sind der .NET-Standardweg.
- **Consumer-only Sample (nur Connector, externer Server):** realistischere Consumer-Sicht, aber nicht
  „out-of-the-box" lauffähig; verworfen zugunsten des self-contained Quickstarts.
- **Pack + Publish sofort in der CI:** verworfen — der authentifizierte Push (Feed/Secrets/Permissions)
  gehört zur Publish-Pipeline M9 / #62; #50 liefert nur build-only Pack.

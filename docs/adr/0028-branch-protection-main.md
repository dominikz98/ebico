# 0028 — Branch-Protection für `main`: CI als durchgesetztes Merge-Gate

- Status: accepted
- Datum: 2026-07-21

## Kontext

Die projektweite Definition of Done verlangt „CI grün" (`dotnet build` + `dotnet test`,
keine neuen Warnungen). Die CI-Pipeline (`ci.yml`, #7) läuft auch längst auf jedem PR —
aber ihr Ergebnis war folgenlos: `main` war ungeschützt
(`gh api repos/:owner/:repo/branches/main/protection` → *„Branch not protected"*). Ein PR
mit roter CI war mergebar, und ein direkter Push auf `main` umging den PR-Prozess komplett.
Die DoD war damit eine reine Selbstverpflichtung.

Erschwerend: EBICO ist faktisch ein **Solo-Repo** (ein Maintainer). Genau dort sind die
üblichen Standardeinstellungen für Branch-Protection kontraproduktiv — „Required approving
reviews" blockiert jeden Merge, weil GitHub das Approven des eigenen PRs verbietet, und
ein Admin-Bypass macht die Regel für den einzigen Committer wirkungslos.

## Entscheidung

Klassische **Branch-Protection-Regel** auf `main` mit diesem Zuschnitt:

- **Required status checks** = genau die vier `ci.yml`-Jobs (`Build & Test`,
  `Docs Link Check`, `Container Build (Server)`, `Pack (NuGet, build-only)`), mit
  `strict: true` (Branch muss vor dem Merge aktuell sein).
- **Keine** required approving reviews — die Review-Pflicht bleibt als Checklistenpunkt
  in `.github/PULL_REQUEST_TEMPLATE.md`, nicht als technisches Gate.
- **`enforce_admins: true`** — die Regel gilt auch für den Repo-Owner.
- Direkte Pushes, Force-Pushes und das Löschen von `main` sind blockiert.

Der Job `Publish (NuGet + Container)` aus `release.yml` wird bewusst **nicht** als Required
Check geführt: er feuert nur auf `v*.*.*`-Tags.

## Konsequenzen

- Die DoD „CI grün" ist erstmals technisch durchgesetzt statt nur dokumentiert; jede
  Änderung an `main` läuft zwingend über einen PR.
- **Die Regel liegt in den Repo-Settings, nicht im Repo.** Sie ist nicht versioniert, taucht
  in keinem Diff auf und kann still verändert werden. Gegenmaßnahme: `docs/development/ci.md`
  beschreibt den Soll-Zustand, und `BranchProtectionDocTests` hält wenigstens die Liste der
  Required Checks mit den Job-Namen in `ci.yml` synchron — ein umbenannter Job bricht sonst
  lautlos entweder das Gate (Check existiert nicht mehr → hängt) oder die Doku.
- **Selbst-Aussperrung ist möglich:** Bei `enforce_admins: true` blockiert ein dauerhaft
  roter oder nie startender Required Check jeden Merge. Der vorgesehene Ausweg ist das
  temporäre Deaktivieren der Regel in den Settings, nicht der Force-Push.
- Jeder Job, der neu in `ci.yml` aufgenommen wird, ist zunächst **kein** Required Check —
  die Repo-Einstellung muss aktiv nachgezogen werden.

## Alternativen

- **Repository Rulesets** (die neuere GitHub-Mechanik): mächtiger (mehrere Regeln pro Branch,
  Bypass-Listen, org-weite Vererbung), aber für ein Solo-Repo mit genau einer geschützten
  Branch reiner Mehraufwand. Klassische Protection ist per `gh api` in einem Aufruf gesetzt
  und in jeder GitHub-Doku beschrieben — verworfen zugunsten der einfacheren Variante, ein
  späterer Umstieg bleibt jederzeit möglich.
- **`enforce_admins: false`** (Admin darf bypassen): schützt vor Selbst-Aussperrung, macht
  die Regel aber bei einem einzigen Committer zur Attrappe — verworfen.
- **Required approving reviews (1)**: würde bei einem Maintainer jeden Merge blockieren —
  verworfen; ersatzweise der DoD-Punkt „Code-Review durchgeführt" in der PR-Vorlage.
- **Mindest-Coverage-Gate als zusätzlicher Required Check** (ursprünglich in #3 gefordert):
  verworfen. Ein nachträglich auf eine fertige Codebasis gesetzter Schwellwert erzeugt vor
  allem CI-Rauschen; Coverage bleibt als Sichtbarkeit über das CI-Artefakt erhalten.

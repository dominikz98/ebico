# 0027 — NuGet publish & release pipeline (nuget.org, tag-driven)

- Status: accepted
- Date: 2026-07-21

## Context

M9 (#62) requires a **publish pipeline**: "pack + push in CI (nuget.org / GitHub
Packages)", "versioning/tags" and "release-notes automation". Starting point after
#50/#61: the two libraries `EBICO.Core` + `EBICO.Connector` have complete package metadata
and CalVer versioning ([ADR-0024](0024-nuget-packaging-and-versioning.md)), and CI has a
**build-only** `pack` job as well as a build-only `container-build` job — both deliberately
**without** a registry push, which was explicitly deferred to this pipeline
([ADR-0022](0022-container-image-and-configuration.md)).

Open and to be decided here: the **publish target** (nuget.org vs. GitHub Packages), the
**release trigger** and the **version origin** at release (ADR-0024 derives BUILD from
`github.run_number` — for a controlled release that is not reproducible/predictable), as
well as the **release-notes** strategy. The container push per ADR-0022 also has its place
here.

## Decision

1. **Publish target: nuget.org.** The packages are pushed to
   `https://api.nuget.org/v3/index.json` (goal: broad, auth-free consumability — the
   connector is intended as a public client, as *Azurite* is the server counterpart). The
   API key lives as the repo secret **`NUGET_API_KEY`**. The `.snupkg` symbol packages are
   published automatically by `dotnet nuget push`; `--skip-duplicate` makes re-runs
   idempotent.
2. **Tag-driven with version-from-tag.** A dedicated workflow
   **`.github/workflows/release.yml`** fires on `push` of tags `v*.*.*`. The version is
   derived from the tag (`v2026.7.42` → `2026.7.42`) and set via `-p:Version=` over the
   date-based CalVer computation from `Directory.Build.props`. The tag must match the CalVer
   pattern `{YEAR}.{MONTH}.{BUILD}` (guard in the workflow). This **refines** ADR-0024 (BUILD
   from `run_number`) for **release** builds: the tag is the source of truth; the
   run-number-based version stays for the build-only `pack` regression check in `ci.yml`.
3. **Release notes automatic.** The workflow creates a GitHub release
   (`gh release create --generate-notes`) with notes generated from the PRs/commits since the
   last tag and attaches the `.nupkg`/`.snupkg`. **No** hand-maintained `CHANGELOG.md` — the
   GitHub releases are the changelog (fulfils the "release notes/changelog" channel mentioned
   in ADR-0024).
4. **Container push to GHCR.** The same workflow builds the server image
   (`--build-arg PROJECT=EBICO.Server`, like the `container-build` job) and pushes
   `ghcr.io/dominikz98/ebico-server:{VERSION}` **and** `:latest` to GHCR — authenticated via
   the automatic `GITHUB_TOKEN` (`permissions: packages: write`), without an external secret.
   This fulfils the push deferred in ADR-0022 to "the publish pipeline #62".
5. **Separate workflow.** `release.yml` is separate from `ci.yml`, because the tag trigger
   differs from the CI trigger (`main`/PR); `ci.yml` stays responsible for build/test/pack
   regression.

## Consequences

- A release comes about by **setting and pushing a tag** `vYEAR.MONTH.N`; everything else
  (build/test, pack, nuget.org push, GHCR image, GitHub release) runs automatically. Runbook:
  [../development/release.md](../development/release.md).
- **Inert until configured:** without `NUGET_API_KEY` the NuGet push fails; merging the
  workflow itself publishes nothing. The tag gate protects against accidental publication —
  relevant, because nuget.org pushes are practically irreversible (only "unlist").
- Package and assembly version are identical (the same `-p:Version` at build and pack); the
  existing `PackageMetadataTests` (CalVer pattern) stay valid.
- **Trade-off GitHub-only release notes:** whoever wants the changelog offline/in the repo
  will not find it as a file; in exchange the maintenance effort and duplication are avoided.

## Alternatives

- **GitHub Packages** (instead of/in addition to nuget.org): uses `GITHUB_TOKEN` without an
  external account, but installation only with GitHub authentication — rejected in favour of
  the broad, auth-free nuget.org consumability (project goal).
- **GitHub release event** (`on: release: [published]`) as the trigger: requires manually
  creating a release in the UI; rejected in favour of the purely tag-driven, fully automatic
  flow (the release is created by the workflow).
- **Publish on every main push** (CalVer from `run_number`): continuous without tags, but
  without a controlled release moment and against the #62 requirement "versioning/tags" —
  rejected.
- **Hand-maintained `CHANGELOG.md`:** full control over the notes, but maintenance effort and
  redundancy with the GitHub releases — rejected in favour of automation.

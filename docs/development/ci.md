# CI pipeline (GitHub Actions)

Describes the continuous integration workflow (`.github/workflows/ci.yml`).
Belongs to issue **#7 — CI pipeline (GitHub Actions)** (Milestone M0).

## Trigger

- `pull_request` — every PR is built and tested (gate before the merge).
- `push` to `main` — validation of the main branch after the merge.

A `concurrency` group cancels older, still-running runs of the same
branch/PR as soon as a new commit arrives.

## Jobs

### `build-test`

1. **Checkout** (`actions/checkout`).
2. **Setup .NET** (`actions/setup-dotnet`) — installs the SDK from
   [`global.json`](../../global.json) (SDK version pinning).
3. **NuGet cache** (`actions/cache`) — caches `~/.nuget/packages`, key via
   `hashFiles('**/*.csproj', 'Directory.Packages.props')`. If dependencies
   change, the key changes.
4. **Restore** (`dotnet restore`).
5. **Build** (`-c Release --no-restore`) — `TreatWarningsAsErrors=true` from
   `Directory.Build.props` turns every new warning into an error (DoD).
6. **Test** (`--no-build`) — coverage via `--collect:"XPlat Code Coverage"`
   (coverlet) + TRX logger.
7. **Artifacts**: `coverage.cobertura.xml` (coverage) and `*.trx` (test report)
   are uploaded — even on a red run (`if: always()`).

### `docs-link-check`

Uses [lychee](https://github.com/lycheeverse/lychee-action) to check **relative**
documentation links (docs-as-code). It runs deliberately **offline** (`--offline`): external URLs
(e.g. the many `ebics.org` links in `docs/protocol/schema-sources.md`) are
not checked, to avoid flaky network requests. Dead relative links (for instance
after moving a documentation page) fail the job.

### `pack`

Packs (after `build-test`) the published libraries **`EBICO.Core`** and
**`EBICO.Connector`** in Release into `./artifacts` and uploads `*.nupkg` + `*.snupkg`
as the artifact `nuget` (issue #50). The job validates the real **packability**
(README, XML doc, symbols, SourceLink, license expression) — a missing
package README would break it, for example, with `NU5039`. The CalVer BUILD component comes from
`github.run_number` (`-p:EbicoBuildNumber=…`), see
[packaging.md](../connector/packaging.md) and
[ADR-0024](../adr/0024-nuget-packaging-and-versioning.md). **Build-only:** no
registry push — that belongs to the publish pipeline (M9 / #62), analogous to the
`container-build` job.

## Release workflow (`release.yml`)

The push/publish runs **separately** from CI in `.github/workflows/release.yml` (M9 / #62,
[ADR-0027](../adr/0027-nuget-publish-and-release-pipeline.md)). Its trigger is **not** `main`/PR, but
pushing a **tag `v*.*.*`** — the CI jobs above stay unaffected by it. The `release` job:

1. **Derive the version from the tag** and check it against the CalVer pattern (`v2026.7.42` → `2026.7.42`).
2. **Build + Test** in Release with `-p:Version=<version>` (overrides the date-based CalVer number;
   re-verifies the DoD).
3. **Pack** `EBICO.Core` + `EBICO.Connector` with the same version → `./artifacts`.
4. **Push to nuget.org** (`dotnet nuget push`, secret `NUGET_API_KEY`, `--skip-duplicate`; `.snupkg`
   automatically included).
5. **GHCR container push** `ghcr.io/dominikz98/ebico-server:{VERSION}` + `:latest` (via `GITHUB_TOKEN`).
6. **GitHub release** with auto-generated notes and the NuGet artifacts (`gh release create --generate-notes`).

The workflow is **inert** until maintainers set the secret `NUGET_API_KEY` and push a tag — a
mere merge publishes nothing. Step by step: [Release runbook](release.md).

## Branch protection for `main`

The CI jobs above only become a real **gate** once GitHub blocks the merge
while they are not green. Without a protection rule a red PR is mergeable and a direct
push to `main` is possible — the Definition of Done ("CI green") would be secured
by discipline alone. That is why `main` is protected by a **branch protection rule** (issue #3,
[ADR-0028](../adr/0028-branch-protection-main.md)).

The rule lives in the **repo settings**, not in the repo content — by definition it is
not versionable. This section is therefore the authoritative description of the
target state; a guard test (`BranchProtectionDocTests`) at least keeps the list of
required checks in sync with `ci.yml`.

### Required status checks

Exactly the jobs from `ci.yml` — they run on every `pull_request`:

<!-- required-checks:start -->
- `Build & Test`
- `Docs Link Check`
- `Container Build (Server)`
- `Pack (NuGet, build-only)`
<!-- required-checks:end -->

What counts as the check context is the job's **display name** (`name:`), not the YAML key.
If a job is renamed, added or removed, the list here **and** the
repo setting must be updated.

> **Not** included: the job `Publish (NuGet + Container)` from
> [`release.yml`](#release-workflow-releaseyml). It fires exclusively on `v*.*.*` tags
> and would, as a required check on every PR, hang forever as "Expected — Waiting for status"
> and permanently block the merge.

### Further settings

| Setting | Value | Why |
| --- | --- | --- |
| `strict` (branch must be up to date) | **on** | Prevents the semantic merge hole: two PRs, each green on its own, can break each other. |
| Direct pushes to `main` | **blocked** | Changes go through a PR without exception (see the workflow convention "one issue → one branch → one PR"). |
| `enforce_admins` | **on** | EBICO is effectively a solo repo; without admin binding the rule would be ineffective for the sole committer. |
| Required approving reviews | **off** | A solo repo cannot approve its own PR — the rule would block every merge. The review obligation remains as a DoD item in the PR checklist. |
| Force-push / delete branch | **blocked** | The history of `main` stays linear and traceable. |

The state can be set or checked via the API:

```bash
gh api repos/:owner/:repo/branches/main/protection            # Ist-Zustand
gh api repos/:owner/:repo/branches/main/protection --method PUT --input protection.json
```

**On a red gate:** If a broken or hanging check no longer allows a merge, the
way forward is *not* the force-push, but to briefly disable the rule in the settings, merge the
fix and re-enable it immediately.

## Reproducibility without lock files

**No** `packages.lock.json` are used. Reproducible restores
come from central package management: all versions are pinned exactly in
`Directory.Packages.props`, including transitive packages
(`CentralPackageTransitivePinningEnabled=true`). Background: the implicit
Blazor asset package `Microsoft.AspNetCore.App.Internal.Assets` depends on the
SDK runtime patch, which makes checked-in lock files break `--locked-mode` between
machines with a different SDK patch (NU1004). Details:
[solution-layout.md](solution-layout.md).

## Later

- **External link check** as a non-blocking `schedule` job (nightly).

> **Done (M9 / #62):** The authenticated **publish/push** has been implemented since #62 in the
> [release workflow](#release-workflow-releaseyml) (nuget.org + GHCR, tag-driven,
> [ADR-0027](../adr/0027-nuget-publish-and-release-pipeline.md)). The build-only `pack` job in `ci.yml`
> remains in place as regression protection.

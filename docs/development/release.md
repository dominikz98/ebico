# Release runbook

How a release of EBICO is cut, and published to nuget.org & GHCR. Implementation of **issue #62** (Milestone M9 — Packaging &
Docs). Foundational decision: [ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md). The
workflow is [`.github/workflows/release.yml`](../../.github/workflows/release.yml).

## In brief

A release comes about by **setting and pushing a tag** `vYEAR.MONTH.N`. Everything else runs
automatically:

```bash
git tag v2026.7.1      # CalVer: {YEAR}.{MONTH}.{BUILD}
git push origin v2026.7.1
```

The tag push triggers `release.yml`; a normal `main` push or PR triggers **no** release
(that still runs via [`ci.yml`](ci.md)).

## Prerequisites (once, by maintainers)

- **Secret `NUGET_API_KEY`** — an API key from nuget.org with push rights for `EBICO.*`, stored under
  *Repo → Settings → Secrets and variables → Actions*. **Without** this secret the NuGet push
  fails; nothing is published.
- **GHCR** needs **no** additional secret — the container push uses the automatic
  `GITHUB_TOKEN` (`permissions: packages: write` in the workflow).

## Version scheme

The version follows **CalVer `{YEAR}.{MONTH}.{BUILD}`** ([ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md)).
At release the **tag is the source of truth**: `v2026.7.42` → package/image version `2026.7.42`
(the `v` prefix is removed). The workflow aborts if the tag does not match the pattern
`v<number>.<number>.<number>`. NuGet normalizes leading zeros (`v2026.07.1` → `2026.7.1`).

> Distinction from the `pack` job in `ci.yml`: there the BUILD component comes from `github.run_number` (a pure
> regression pack, no push). For **releases** the tag version overrides that via `-p:Version=`.

## What the workflow does (`release.yml`)

1. **Derive the version from the tag** and check it against the CalVer pattern.
2. **Restore → Build → Test** in Release (with the tag version; `TreatWarningsAsErrors` still applies).
3. **Pack** `EBICO.Core` + `EBICO.Connector` (`*.nupkg` + `*.snupkg`) with the tag version → `./artifacts`.
4. **Push to nuget.org** (`dotnet nuget push`, `--skip-duplicate`; `.snupkg` symbols are automatically
   published along with them).
5. **GHCR container push** `ghcr.io/dominikz98/ebico-server:{VERSION}` **and** `:latest`.
6. **GitHub release** with auto-generated release notes (from the PRs/commits since the last tag) and
   the NuGet artifacts as attachments.

## Check after the release

- **nuget.org:** `EBICO.Core` and `EBICO.Connector` listed in the target version (indexing can take a few
  minutes). nuget.org pushes are practically **irreversible** (only "unlist").
- **GHCR:** `docker pull ghcr.io/dominikz98/ebico-server:<version>` works.
- **GitHub release:** under *Releases* with generated notes and attached `*.nupkg`/`*.snupkg`.

## Troubleshooting

| Symptom | Cause / remedy |
| --- | --- |
| Workflow aborts at "derive version from tag" | Tag does not match `vYEAR.MONTH.BUILD` (only digits, three components). |
| NuGet push fails (401/403) | `NUGET_API_KEY` missing/expired or without push rights for `EBICO.*`. |
| Package "already exists" | Version was already pushed; `--skip-duplicate` skips it (no error). |
| GHCR push "denied" | `packages: write` permission missing, or check package visibility/linkage. |

## Related documentation

- [CI pipeline](ci.md) — build/test/pack (build-only) per push/PR
- [Packaging & examples (NuGet)](../connector/packaging.md) — package metadata, symbols, CalVer
- [Container image](../deployment/container.md) — image build & GHCR push
- [ADR-0027 — NuGet publish & release pipeline](../adr/0027-nuget-publish-und-release-pipeline.md)
- [ADR-0024 — NuGet packaging & versioning](../adr/0024-nuget-packaging-und-versionierung.md)

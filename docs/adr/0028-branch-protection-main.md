# 0028 — Branch protection for `main`: CI as an enforced merge gate

- Status: accepted
- Date: 2026-07-21

## Context

The project-wide Definition of Done requires "CI green" (`dotnet build` + `dotnet test`,
no new warnings). The CI pipeline (`ci.yml`, #7) has long been running on every PR too —
but its result had no consequence: `main` was unprotected
(`gh api repos/:owner/:repo/branches/main/protection` → *"Branch not protected"*). A PR
with red CI was mergeable, and a direct push to `main` bypassed the PR process entirely.
The DoD was thereby a pure self-commitment.

Complicating matters: EBICO is effectively a **solo repo** (one maintainer). It is
precisely there that the usual default settings for branch protection are
counterproductive — "required approving reviews" blocks every merge, because GitHub forbids
approving one's own PR, and an admin bypass makes the rule ineffective for the only
committer.

## Decision

A classic **branch-protection rule** on `main` with this cut:

- **Required status checks** = exactly the four `ci.yml` jobs (`Build & Test`,
  `Docs Link Check`, `Container Build (Server)`, `Pack (NuGet, build-only)`), with
  `strict: true` (the branch must be up to date before the merge).
- **No** required approving reviews — the review obligation stays as a checklist item in
  `.github/PULL_REQUEST_TEMPLATE.md`, not as a technical gate.
- **`enforce_admins: true`** — the rule applies to the repo owner too.
- Direct pushes, force pushes and deleting `main` are blocked.

The job `Publish (NuGet + Container)` from `release.yml` is deliberately **not** run as a
required check: it fires only on `v*.*.*` tags.

## Consequences

- The DoD "CI green" is enforced technically for the first time rather than only
  documented; every change to `main` necessarily goes through a PR.
- **The rule lives in the repo settings, not in the repo.** It is not versioned, appears in
  no diff and can be changed silently. Countermeasure: `docs/development/ci.md` describes the
  target state, and `BranchProtectionDocTests` keeps at least the list of required checks in
  sync with the job names in `ci.yml` — a renamed job would otherwise silently break either
  the gate (the check no longer exists → hangs) or the docs.
- **Self-lockout is possible:** with `enforce_admins: true`, a permanently red or
  never-starting required check blocks every merge. The intended way out is temporarily
  disabling the rule in the settings, not a force push.
- Every job newly added to `ci.yml` is initially **not** a required check — the repo setting
  must be actively followed up.

## Alternatives

- **Repository rulesets** (the newer GitHub mechanism): more powerful (multiple rules per
  branch, bypass lists, org-wide inheritance), but for a solo repo with exactly one protected
  branch pure extra effort. Classic protection is set via `gh api` in one call and described
  in every GitHub doc — rejected in favour of the simpler variant, a later switch stays
  possible at any time.
- **`enforce_admins: false`** (admin may bypass): protects against self-lockout, but makes
  the rule a sham with a single committer — rejected.
- **Required approving reviews (1)**: would block every merge with a single maintainer —
  rejected; instead the DoD item "code review performed" in the PR template.
- **A minimum-coverage gate as an additional required check** (originally required in #3):
  rejected. A threshold set retroactively on a finished codebase mainly produces CI noise;
  coverage stays visible via the CI artefact.

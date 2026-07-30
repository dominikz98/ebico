<!--
  Thanks for your contribution! Please fill in the checklist.
  The "Definition of Done" is binding project-wide (see CLAUDE.md / docs/).
-->

## Description

<!-- What does this PR change, and why? Relation to the EBICS version (H003/H004/H005), if relevant. -->

## Issue

<!--
  Mandatory: every PR references exactly one issue — including pure tooling/docs changes.
  Enter the issue number (e.g. "Closes #42").
-->
Closes #

## Definition of Done

- [ ] **Issue linked:** `Closes #<no>` filled in above (every PR references exactly one issue)
- [ ] **Docs:** feature described in Markdown under `docs/` (purpose, flow,
      sample XML/code, EBICS version relation) and linked in the doc index (`docs/index.md`)
- [ ] **Docs/skills updated:** affected `docs/`, `CLAUDE.md` and `.claude/skills/`
      brought along in the same PR (see CLAUDE.md → "Maintaining context, docs & skills")
- [ ] **Tests:** unit tests for the core logic (happy path + relevant
      negative/edge cases); for protocol/crypto topics with test vectors/sample XML
- [ ] **CI green:** `dotnet build` + `dotnet test` successful, **no new warnings**
- [ ] **XML-doc comments** on public APIs
- [ ] **Code review** carried out

## License check (if schemas/specs/samples are touched)

- [ ] No proprietary EBICS schemas/specs/sample XML committed to the repo
      (see `docs/protocol/schema-sources.md`)

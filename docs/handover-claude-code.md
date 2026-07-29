# Handover prompt for Claude Code

Copy the following block as the first message into Claude Code (in the root
directory of the `ebico` repo). It provides the necessary context so that you do not have to
explain everything anew.

---

```
You are working on the EBICO project: an EBICS implementation in C# (.NET 10),
conceptually like Azurite, but for EBICS instead of Azure Storage. The goal is a
server emulator plus a client package.

## Project structure (5 projects, still to be created)
- EBICO.Core      — shared primitives (schemas/serialisation, crypto, BTF/order models)
- EBICO.Connector — NuGet client for accessing an EBICS server
- EBICO.Server    — the emulator (hostable, ASP.NET Core)
- EBICO.Suite     — Blazor UI for the server
- EBICO.Tests     — unit/integration/conformance tests

Supported EBICS versions: H003, H004, H005. Order coverage: the most
complete BTF/order palette possible.

## What already exists (in the repo)
- GitHub issues & milestones are already created (10 milestones M0–M9,
  64 issues, 12 epics). Overview: docs/ticket-overview.md
- scripts/fetch-schemas.sh — obtains/sorts the EBICS XSDs reproducibly
- docs/protocol/schema-sources.md — source URLs, file list, licensing situation
- docs/connector/architecture.md — architecture of the connector (mediator pattern)
- .gitignore — excludes schemas/**/*.xsd (license!)

## Project-wide, binding rules (Definition of Done per feature)
1. DOCS: Every feature is documented in Markdown under docs/ and linked in
   the doc index. Docs-as-Code: documentation belongs in the same PR as the code.
2. TESTS: Every feature is covered by unit tests (happy path +
   negative/edge cases). Protocol/crypto logic against test vectors and
   sample XML, not just self-consistency. No feature counts as done without tests.
3. CI must be green (dotnet build + dotnet test, no new warnings).
4. XML-doc comments on public APIs.

## Important constraints
- LICENSE: The EBICS schemas/specs are proprietary (EBICS SC). Modification /
  derivative uses without permission are not allowed. Do NOT commit XSDs unchecked
  — clarify the licensing question before M1 (issue "License/terms-of-use clarification"). Obtain
  the schemas locally via scripts/fetch-schemas.sh.
- The architecture in docs/connector/architecture.md is a reasoned
  proposal, NOT a design verified against the spec. Once the real schemas
  are available, verify the details (e.g. order of E002/A00x/X002, segment loop per
  version) against the official XSDs/annexes and update the docs.

## Connector architecture in brief (details: docs/connector/architecture.md)
Mediator pattern: the caller only knows IEbicsClient.Send(request) and receives
a typed EbicsResult<T>. Pipeline per Send: validation → serialisation →
compress/E002/A00x → X002 → transport (HttpClient behind ITransport) →
verify/decrypt → return code → segments if needed → deserialise. Own
dispatch instead of MediatR. Key store as an abstraction (IKeyStore).

## What I want to start with now
Begin with milestone M0. Concretely, first:
1. "Create solution & project skeleton": EBICO.sln with the 5 projects (net10.0),
   Directory.Build.props (Nullable enable, TreatWarningsAsErrors), central
   package management (Directory.Packages.props), .editorconfig, docs/ base structure,
   .github/PULL_REQUEST_TEMPLATE.md with docs/test checklist, solution folders src/ tests/ docs/.
2. Then "Test harness & fixtures" (xUnit + FluentAssertions) and
   "CI pipeline (GitHub Actions)".

First read docs/ticket-overview.md and the mentioned doc files, look at
the open GitHub issues (gh issue list), and then propose a
concrete plan for the solution skeleton before you create files. Work
issue-driven: one branch + PR per issue, docs and tests included.
```

---

## Usage tips

- **Before starting**, make sure that `gh` is authenticated and Claude Code
  is running in the repo root directory — then it can use `gh issue list` itself.
- If you prefer to work issue by issue, replace the last paragraph with a
  concrete issue number: *"Now work on issue #12."*
- You can also place the block as `CLAUDE.md` into the repo — then Claude Code
  has the context automatically in every session, without you pasting it in.

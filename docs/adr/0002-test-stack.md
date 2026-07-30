# 0002 — Test stack: xUnit v3 + AwesomeAssertions

- Status: accepted
- Date: 2026-06-21

## Context

The project-wide Definition of Done requires unit tests per feature (happy path +
negative/edge cases). A test framework and an assertion library for .NET 10 are
needed. Issue #8 originally mentioned "xUnit + FluentAssertions".

## Decision

- **xUnit v3** (`xunit.v3` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)
  as the test framework. The test project is executable (`OutputType=Exe`, required
  by xUnit v3).
- **AwesomeAssertions** instead of FluentAssertions as the assertion library.

## Consequences

- xUnit v3 is the current line, designed for .NET 10.
- AwesomeAssertions is an **MIT-licensed fork** of the FluentAssertions v7 API
  (same `Should()` syntax) — important because **FluentAssertions has been
  commercially licensed since v8 (Jan 2025)** (Xceed) and is therefore unsuitable
  for a public OSS repo.
- **Note:** the root namespace is `AwesomeAssertions` (not `FluentAssertions`).

Details: [../development/testing.md](../development/testing.md).

## Alternatives

- **FluentAssertions v8:** functionally excellent, but commercially licensed — rejected.
- **FluentAssertions v7 (last free one):** free, but frozen — rejected in favour of
  the actively maintained fork.
- **Shouldly:** free, but a different API than the one requested in the issue — rejected.

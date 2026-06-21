# 0002 — Test-Stack: xUnit v3 + AwesomeAssertions

- Status: accepted
- Datum: 2026-06-21

## Kontext

Die projektweite Definition of Done verlangt Unit-Tests pro Feature (Happy Path +
Negativ-/Grenzfälle). Es wird ein Testframework und eine Assertion-Bibliothek für
.NET 10 benötigt. Issue #8 nannte ursprünglich „xUnit + FluentAssertions".

## Entscheidung

- **xUnit v3** (`xunit.v3` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)
  als Testframework. Das Testprojekt ist ausführbar (`OutputType=Exe`, von
  xUnit v3 verlangt).
- **AwesomeAssertions** statt FluentAssertions als Assertion-Bibliothek.

## Konsequenzen

- xUnit v3 ist die aktuelle, für .NET 10 ausgelegte Linie.
- AwesomeAssertions ist ein **MIT-lizenzierter Fork** der FluentAssertions-v7-API
  (gleiche `Should()`-Syntax) — wichtig, weil **FluentAssertions seit v8 (Jan 2025)
  kommerziell** lizenziert ist (Xceed) und damit für ein öffentliches OSS-Repo
  nicht geeignet ist.
- **Achtung:** Der Root-Namespace ist `AwesomeAssertions` (nicht `FluentAssertions`).

Details: [../development/testing.md](../development/testing.md).

## Alternativen

- **FluentAssertions v8:** funktional top, aber kommerzielle Lizenz — verworfen.
- **FluentAssertions v7 (letzte freie):** frei, aber eingefroren — verworfen
  zugunsten des aktiv gepflegten Forks.
- **Shouldly:** frei, aber andere API als die im Issue gewünschte — verworfen.

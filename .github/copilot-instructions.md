# Copilot instructions for blazor-untranslated

All agent guidance lives in [`AGENTS.md`](../AGENTS.md) at the repo root. Read it before
changing code. It isn't duplicated here.

The rules that matter most, for tools that only read this file (such as code review):

- `UNT001` relies on Razor's generated code. Every behavior change adds a fixture case in
  `tests/Fixtures/Components/`, with a `@*UNT001*@` marker on each line the error points at.
- Roslyn stays at 4.8 (`Directory.Packages.props`): no APIs newer than 4.8, or the analyzer
  stops loading in older compilers.
- MSBuild properties are comma-separated: the analyzer config treats `;` and `#` as comments.
- New or changed rules update `AnalyzerReleases.Unshipped.md`; every change gets a line under
  *Unreleased* in `CHANGELOG.md`.
- C# style is `.editorconfig`, enforced by `dotnet format` in CI; warnings are errors. Run
  `scripts/verify.sh` before finishing.

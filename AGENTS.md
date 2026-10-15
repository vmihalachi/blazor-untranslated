# AGENTS.md — Blazor.Untranslated

Guide for coding agents (Claude Code reads it through `CLAUDE.md`). Humans start at `README.md`.

**What it is:** an MIT Roslyn analyzer, shipped as the NuGet package `Blazor.Untranslated`, with one rule:
`UNT001` fails the build when a `.razor` component renders literal text instead of a localized resource. It was
extracted from Adjudeca (its ADR-021), which uses the published package. Owned by Vlad Mihalachi and changed
whenever Adjudeca needs; there is no support for outside users.

## Layout

| Path | Contents |
|---|---|
| `src/Blazor.Untranslated/` | The analyzer (`LiteralTextAnalyzer.cs`, netstandard2.0), `build/Blazor.Untranslated.props` (declares the two `CompilerVisibleProperty` items) and the release-tracking files `AnalyzerReleases.{Shipped,Unshipped}.md`. |
| `tests/Blazor.Untranslated.Tests/` | xUnit. `FixtureBuild` packs the analyzer, copies the fixture project to a temp folder and runs `dotnet build` on it; `FixtureTests` compares each reported `UNT001` with the markers. |
| `tests/Fixtures/Components/` | A Razor class library with one `.razor` file per case. Every expected diagnostic is a `@*UNT001*@` marker on the line the error points at; a file without markers must build clean. Never part of the solution. |
| `site/` | The GitHub Pages site (static HTML, no build), published by `pages.yml`. |
| `scripts/verify.sh` | Version agreement, formatting and the tests; `--fast` skips the tests. |
| `.github/workflows/` | `ci.yml` (fixtures on the .NET 8, 9 and 10 SDKs), `release.yml` (tag → NuGet + GitHub release), `pages.yml`. |

## Commands

```sh
dotnet build
dotnet test                                    # fixtures with the current SDK
UNTRANSLATED_FIXTURE_SDK=8.0 dotnet test       # fixtures with the newest installed 8.0 SDK, targeting net8.0
dotnet format --verify-no-changes
scripts/verify.sh
```

A Stop hook runs `scripts/verify.sh --fast` and `dotnet format --verify-no-changes` on the files you edited
(`.claude/hooks/validate.json`); fix what it reports before ending.

## Conventions

- **The rule relies on Razor's generated code**: the `#pragma checksum`, literals at `RenderTreeBuilder` call sites,
  `RuntimeHelpers.TypeCheck`, the `TypeInference` helpers of generic components, and `.razor` files as additional
  files. A change in any of them must show up as a failing fixture; add a fixture case with every behavior change.
- **Roslyn stays at 4.8** (`Directory.Packages.props`), the compiler of the .NET 8 SDK. A newer reference stops the
  analyzer from loading in older compilers. Don't use APIs newer than 4.8.
- **MSBuild properties are comma-separated**: the analyzer config treats `;` and `#` as comments.
- **New or changed rules** update `AnalyzerReleases.Unshipped.md`; a release moves the entries to `Shipped.md`
  under `## Release X.Y.Z`.
- **Changelog:** every change gets a line under *Unreleased* in `CHANGELOG.md`. SemVer, `0.x`: a change in what the
  rule reports is a minor version.
- **Releasing X.Y.Z:** set the `UntranslatedVersion` default in `Directory.Build.props` and, in `site/index.html`,
  the `vX.Y.Z` label and both `Version="X.Y.Z"` snippets; move *Unreleased* to `## X.Y.Z - YYYY-MM-DD`; run
  `scripts/verify.sh`; commit as `X.Y.Z: <headline change>`. Ask the owner before tagging: `git tag -a vX.Y.Z -m
  "Blazor.Untranslated X.Y.Z" && git push origin vX.Y.Z`, or dispatch `release.yml` with the version. Tags never move.
- C# style is `.editorconfig`; `dotnet format` enforces it in CI. Warnings are errors.

## Sessions

One task per session: start a new one for unrelated work and hand off follow-ups instead of continuing here. Before searching for code you don't know the location of, use the `context-explorer` subagent (or `jbcontext search`) so the reads stay out of this context. Read big files by outline (`grep -n '^\(func\|class\|struct\|extension\|public\|##\)'`) and then a range, never whole. `/compact` at a milestone in a long session.

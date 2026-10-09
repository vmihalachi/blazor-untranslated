# Changelog

All notable changes to Blazor.Untranslated. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [Semantic Versioning](https://semver.org/). Every change gets a line under *Unreleased*.

## Unreleased

## 0.1.0 - 2026-10-09

The first release: Adjudeca's `ADJ1001` analyzer, extracted and made generic.

### Added
- Rule `UNT001`: fails the build when a `.razor` component renders literal text with letters in markup, text
  content, the `title`, `alt`, `placeholder`, `label`, `description`, `text` and `aria-*` text attributes, or the
  component parameters with those names, generic components included.
- `UntranslatedAllowedTerms`: comma-separated terms allowed in literal text (was `LocalizationAllowedTerms`).
- `UntranslatedTextAttributes`: comma-separated attribute and parameter names checked besides the defaults.
- The package: the analyzer in `analyzers/dotnet/cs`, `build/Blazor.Untranslated.props` declaring both properties,
  a development dependency built against Roslyn 4.8 so it loads in the .NET 8, 9 and 10 SDKs.
- Tests that build fixture components with the package on each of those SDKs.

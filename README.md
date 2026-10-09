# Blazor.Untranslated

A Roslyn analyzer that fails the build when a Blazor component renders literal text instead of a localized
resource. Rule `UNT001`, for `.razor` components in Blazor Server, WebAssembly, static SSR and MAUI Hybrid. MIT.

Blazor.Untranslated grew out of Adjudeca, a Blazor site in three languages, where it keeps every page
translatable. It is `0.x`, so any minor version may change what the rule reports.

**No support.** Built for my own projects and shared as is: issues and pull requests aren't monitored, and any
release may change or break things.

## What it catches

```razor
<h1>Coming soon</h1>
<img src="logo.svg" alt="Company logo" />
<Card Title="Latest news" />
```

```text
Pages/Home.razor(1,5): error UNT001: Literal text "Coming soon" must come from a resource
Pages/Home.razor(2,26): error UNT001: Literal text "Company logo" must come from a resource
Pages/Home.razor(3,14): error UNT001: Literal text "Latest news" must come from a resource
```

The rule reports literal text with letters in:

- markup text and text content (`@("Hello")`, `@(ok ? "Yes" : "No")`, `@($"Hi {name}")`, `@("a" + b)`);
- the attributes people see or hear: `title`, `alt`, `placeholder`, `label`, `aria-label`, `aria-description`,
  `aria-roledescription`, `aria-valuetext`, `aria-placeholder`, `description` and `text`;
- the component parameters with those names, such as `Title`, `Description`, `Label`, `Placeholder` and `Text`,
  generic components included.

It ignores text without letters (numbers, symbols, punctuation), `<script>` and `<style>` content, HTML and Razor
comments, and every other attribute (`class`, `href`, `id`…). Text from a resource, such as `@L["Title"]`, passes.

The rule is an error, so it fails every build: locally, in the IDE and in CI, with no extra step.

## Install

```sh
dotnet add package Blazor.Untranslated
```

The package is a development dependency: it adds nothing to your output and does not flow to projects that
reference yours. It works with the .NET 8, 9 and 10 SDKs.

## Configuration

Both settings are MSBuild properties in the project that renders the components, with items separated by commas:

```xml
<PropertyGroup>
  <!-- Text that is the same in every language, such as brand and plan names. -->
  <UntranslatedAllowedTerms>Contoso,Pro</UntranslatedAllowedTerms>
  <!-- Attributes and component parameters to check besides the defaults. -->
  <UntranslatedTextAttributes>data-tooltip,Caption</UntranslatedTextAttributes>
</PropertyGroup>
```

An allowed term is removed from the text before the check, so `Contoso` passes and `Welcome to Contoso` does not.
Neither property can contain `;` or `#`: the compiler receives them through an analyzer config file, where those
start a comment.

`UNT001` is reported in `.razor` files, where `#pragma warning disable` cannot reach it. To turn it off for a
project, add `<NoWarn>$(NoWarn);UNT001</NoWarn>` or set `dotnet_diagnostic.UNT001.severity = none` in
`.editorconfig`.

## Limits

- Text built in `@code` blocks or C# files and rendered through a variable is not detected. Keep such text in
  resources too, and check it in reviews.
- Missing translations of existing keys are not checked: the rule only sees that a resource is used.
- `.cshtml` views and Razor Pages are not checked: only `.razor` components.

## How it works

The Razor compiler has already separated markup from C# when the analyzer runs, so the rule sees exactly what
reaches the page instead of guessing through `@if` and `@foreach` blocks. For each `.razor` file it reads the
generated C# and inspects the `RenderTreeBuilder` calls: `AddMarkupContent` (static HTML, parsed for text nodes and
visible attributes), `AddContent`, `AddAttribute` and `AddComponentParameter`.

It relies on the shape of that generated code:

- the `#pragma checksum` at the top of each generated file, which names the `.razor` source;
- literal values at the `RenderTreeBuilder` call sites, wrapped at most in casts, conditionals, concatenation,
  interpolation and `RuntimeHelpers.TypeCheck`;
- the `TypeInference` helpers that render generic components, whose literals sit at the call site;
- the `.razor` files passed to the compiler as additional files, which the analyzer uses to point at the line.

A Razor compiler that changes these can silence the rule. The test suite builds fixture components on the .NET 8,
9 and 10 SDKs and compares every reported line with markers in the fixtures, so such a change fails CI here first.

## Develop

```sh
dotnet build                 # the analyzer and the tests
dotnet test                  # packs the analyzer and builds the fixtures in tests/Fixtures with it
dotnet format --verify-no-changes
```

`UNTRANSLATED_FIXTURE_SDK=8.0 dotnet test` builds the fixtures with the newest installed 8.0 SDK instead of the
current one. Coding agents: read `AGENTS.md`.

## Releases

`git tag v0.1.0 && git push --tags` (or Actions → Release → Run workflow with the version, which creates the tag on
main) runs `release.yml`: it tests, packs, pushes the package to NuGet.org through
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (no stored key: a policy on
nuget.org for repository `vmihalachi/blazor-untranslated` and workflow `release.yml`, plus the `NUGET_USER`
repository variable holding the nuget.org username; without them it says so and attaches the package to the
release instead) and creates a GitHub release from `CHANGELOG.md`.

## License

[MIT](LICENSE). © 2026 Vlad Mihalachi.

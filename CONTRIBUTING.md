# Contributing to TenantLint

Thank you for your interest in contributing to TenantLint!

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (for building and testing)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for the sample app only)

## Getting started

1. Fork and clone the repository.
2. Run `dotnet build` to verify everything compiles.
3. Run `dotnet test` to verify all tests pass.

## Development

```bash
# Build (treats warnings as errors in src/)
dotnet build --configuration Release

# Run tests
dotnet test

# Pack the NuGet package
dotnet pack src/TenantLint.Package

# Build the sample app (shows all diagnostics)
dotnet build samples/SampleSaaS/SampleSaaS.csproj
```

## Adding a new rule

1. **Descriptor** — Add a `DiagnosticDescriptor` to `src/TenantLint.Analyzers/Descriptors.cs`. Use the `HelpLinkBase` constant for the help link URI.

2. **Detection logic** — Add the analysis in `src/TenantLint.Analyzers/TenantIsolationAnalyzer.cs` (or a new `DiagnosticAnalyzer` if the rule is unrelated to tenant model discovery). Use the `IOperation` API over raw syntax. Detect methods by symbol, not by name alone.

3. **Tests** — Create `tests/TenantLint.Analyzers.Tests/TLxxxTests.cs` with:
   - At least **3 positive** test cases (the rule fires correctly)
   - At least **5 negative** test cases (the rule does NOT fire): non-tenant entity, `[CrossTenant]` method, user-defined method with same name, look-alike scenarios
   - Tests for both EF Core 8 and EF Core 10 reference assemblies

4. **Documentation** — Create `docs/rules/TLxxx.md` with: what the rule detects, why it's a security issue, bad/good code examples, how to suppress, false-positive notes.

5. **CHANGELOG** — Add an entry under `[Unreleased]` in `CHANGELOG.md`.

6. **AnalyzerReleases** — Add the rule to `src/TenantLint.Analyzers/AnalyzerReleases.Unshipped.md`.

## Commit conventions

We use [Conventional Commits](https://www.conventionalcommits.org/):

- `feat(TL001): add IgnoreQueryFilters detection`
- `fix(TL002): handle interpolated SQL strings`
- `test: add negative cases for CrossTenant attribute`
- `docs: update TL001 rule page`

One rule per PR.

## Code style

- Follow the `.editorconfig` settings.
- Analyzers target `netstandard2.0`.
- Use the `IOperation` API over raw syntax where possible.
- Every rule needs at least 3 positive and 5 negative test cases.
- No false positives over coverage — when unsure, report at a lower severity or not at all.

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

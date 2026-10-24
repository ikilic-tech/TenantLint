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

Follow the path below for one rule per PR. TL001 is a useful example of a rule that inspects an operation and needs the tenant model; it is already implemented, so use it as a reference rather than adding it again.

### Walkthrough: follow TL001

TL001 reports a call to EF Core's parameterless `IgnoreQueryFilters()` when its entity type is tenant-scoped. A call on a global entity such as `Country`, a similarly named user-defined extension method, or a call inside a `[CrossTenant]` method is intentionally ignored.

1. **Define the diagnostic.** In `src/TenantLint.Analyzers/Descriptors.cs`, TL001 defines the ID, message, severity, and help link. For a new rule, add a descriptor there, use the next available ID, and build its help link from `HelpLinkBase`.

2. **Register the analysis.** `TenantIsolationAnalyzer.Initialize` registers TL001 for `OperationKind.Invocation` after resolving EF Core's `EntityFrameworkQueryableExtensions` symbol. Add a new descriptor to `SupportedDiagnostics` as well as registering its analysis action. Choose an operation or symbol action that matches what the rule examines; use `IOperation` when possible.

3. **Match the real API, then check tenant scope.** In `AnalyzeInvocation`, TL001 compares the called method's containing type with the resolved EF Core symbol, checks the method name and parameter count, and gets the entity type from the method's type argument. It then calls `model.IsTenantScoped(entityType)`. `TenantModel` is built once per compilation and passed to the analysis; reuse it instead of rediscovering tenant entities. Keep the `[CrossTenant]` exemption and report on the `IgnoreQueryFilters` name token so the diagnostic points to the call that matters.

4. **Cover both reports and look-alikes.** Add `tests/TenantLint.Analyzers.Tests/TLxxxTests.cs`. Include at least three positive cases and five negative cases. For a tenant-aware rule, useful negatives include a global/non-tenant entity, a `[CrossTenant]` method, a user-defined method with the same name, and other near matches that should not trigger. Where the API differs by EF version, cover both EF Core 8 and 10 using the existing verifier helpers. TL001's `TL001Tests.cs` shows these patterns, including `ExecuteUpdate` and `ExecuteDelete` chains.

5. **Document the behavior and release entry.** Add `docs/rules/TLxxx.md` with what the rule detects, why it matters, bad and good examples, suppression guidance, and false-positive notes. Link it from the README rule table, add the change under `[Unreleased]` in `CHANGELOG.md`, and list the ID in `src/TenantLint.Analyzers/AnalyzerReleases.Unshipped.md`.

6. **Review the whole path.** Confirm the descriptor is supported, the analysis is registered, tenant scope and exemptions are handled, and the examples agree with the tests and rule page. Keep one rule per PR and summarize its intended behavior and any known limitations.

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

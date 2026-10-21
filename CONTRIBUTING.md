# Contributing to TenantLint

Thank you for your interest in contributing to TenantLint!

## Getting started

1. Fork and clone the repository.
2. Run `dotnet build` to verify everything compiles.
3. Run `dotnet test` to verify all tests pass.

## Development

- **SDK:** .NET 8 SDK (see `global.json`).
- **Build:** `dotnet build` from the repository root.
- **Test:** `dotnet test` from the repository root.
- **Pack:** `dotnet pack src/TenantLint.Package` to produce the NuGet package.

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

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

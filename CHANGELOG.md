# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Rules

- **TL011** (Warning) / **TL111** (Info, disabled by default): detects `HasQueryFilter` lambdas whose tenant value is rooted in a field only assigned in the constructor. TL111 fires on the DbContext type itself (opt-in via `.editorconfig`); TL011 fires at the `AddDbContextPool` / `AddPooledDbContextFactory` registration site. Pooled instances reuse the first tenant's value. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL011.md)
- **TL012** (Warning): detects `HasQueryFilter` lambdas whose tenant value is not rooted in the DbContext instance (locals, static fields/properties, static method calls). EF Core caches the model — the first value is baked in and reused for every tenant. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL012.md)
- **TL013** (Warning) / **TL113** (Info): detects raw ADO.NET SQL (`DbCommand.CommandText` assignment, DbCommand-derived constructor) that references a tenant table. Same severity logic as TL002/TL003. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL013.md)

### Fixed

- TL005 code fix: use the actual `OnModelCreating` parameter name instead of hardcoding `modelBuilder`.
- TL005 code fix: generate named `HasQueryFilter("Tenant", ...)` when the DbContext uses EF Core 10 named filters or `tenantlint.tenant_filter_name` is configured.
- TL005 code fix: avoid lambda parameter name collision with identifiers already in scope.

## [0.1.0-preview.2] - 2026-10-23

### Fixed

- Repository and help links pointed to the wrong GitHub owner (`ibrahimkilic` → `ikilic-tech`).

## [0.1.0-preview.1] - 2026-10-23

### Rules

- **TL001** (Warning): detects parameterless `IgnoreQueryFilters()` on tenant-scoped entities, including `ExecuteUpdate`/`ExecuteDelete` chains. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL001.md)
- **TL002** (Warning) / **TL102** (Info): detects raw SQL via `ExecuteSqlRaw`, `ExecuteSql`, `ExecuteSqlInterpolated`, `SqlQuery` (+ async) that references a tenant table without apparent tenant filtering. Downgrades to Info when a tenant column appears in a predicate clause with a parameter. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL002.md)
- **TL003** (Warning) / **TL103** (Info): detects Dapper `SqlMapper` calls (`Query*`, `Execute*`, etc.) whose SQL references a tenant table. Same severity logic as TL002. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL003.md)
- **TL005** (Warning) / **TL105** (Info): detects tenant-scoped entities missing a `HasQueryFilter` for the tenant property. Info severity when dynamic filters are detected. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL005.md)
- **TL900** (Info): reports when a dynamic query filter loop is detected in `OnModelCreating`.
- **TL901** (Info): `[CrossTenant]` attribute missing or has a placeholder reason.
- **TL902** (Warning): class-level `[CrossTenant]` suppresses all tenant checks — prefer method-level.

### Code Fixes

- **TL001**: rewrite to selective `IgnoreQueryFilters(["SoftDelete", ...])` (EF Core 10+); add `[CrossTenant("")]` attribute.
- **TL005**: insert `HasQueryFilter` stub with `TODO_SET_TENANT_ACCESSOR` (intentional CS0103).

### Configuration

- `.editorconfig` keys: `tenantlint.tenant_property_names`, `tenantlint.tenant_column_names`, `tenantlint.global_entities`, `tenantlint.tenant_filter_name`, `tenantlint.cross_tenant_attribute`.
- Per-rule severity override via standard `dotnet_diagnostic.TLxxx.severity`.

### Packaging

- NuGet package [`TenantLint.Analyzers`](https://www.nuget.org/packages/TenantLint.Analyzers) — analyzer-only (`analyzers/dotnet/cs`), `DevelopmentDependency`, MIT license.
- Supports EF Core 8+ and EF Core 10 named filters.

[Unreleased]: https://github.com/ikilic-tech/TenantLint/compare/v0.1.0-preview.2...HEAD
[0.1.0-preview.2]: https://github.com/ikilic-tech/TenantLint/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/ikilic-tech/TenantLint/releases/tag/v0.1.0-preview.1

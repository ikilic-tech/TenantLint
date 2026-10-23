# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Rules

- **TL004** (Warning) / **TL104** (Info): detects parameters whose name matches tenant property names and that are bound from client input (`[FromHeader]`, `[FromQuery]`, `[FromForm]` → TL004 Warning; `[FromRoute]` → TL104 Info), and direct `Request.Headers["X-Tenant-Id"]` / `Request.Headers.TryGetValue(...)` / `Request.Query["tenantId"]` reads where the key matches a tenant property name. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL004.md)
- **TL006** (Warning): detects `IMemoryCache` / `IDistributedCache` / `HybridCache` set/get calls whose key does not include a tenant identifier, inside a method that also touches tenant-scoped data. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL006.md)
- **TL007** (Warning): detects tenant context stored in a `static` field or property — tenant values in static fields are shared across all requests and tenants. Excludes `AsyncLocal<T>`, `IHttpContextAccessor`, and other known async-local-backed types. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL007.md)
- **TL008** (Info): detects `BackgroundService` / `IHostedService` that uses a DbContext with tenant-scoped entities without creating a scope or setting the tenant context. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL008.md)
- **TL009** (Warning): detects PostgreSQL RLS session variables set with `SET app.x` / `set_config(..., false)` instead of `SET LOCAL` / `set_config(..., true)` — values persist across pooled connections. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL009.md)
- **TL010** (Warning): detects tenant-scoped entity types bound directly from the request body via `[FromBody]`, `[FromForm]`, implicit body binding on `[ApiController]`, or minimal API `MapPost`/`MapPut`/`MapPatch` handlers (lambda and method-group). A client can set the tenant property to any value. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL010.md)
- **TL011** (Warning) / **TL111** (Info, disabled by default): detects `HasQueryFilter` lambdas whose tenant value is rooted in a field only assigned in the constructor. TL111 fires on the DbContext type itself (opt-in via `.editorconfig`); TL011 fires at the `AddDbContextPool` / `AddPooledDbContextFactory` registration site. Pooled instances reuse the first tenant's value. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL011.md)
- **TL012** (Warning): detects `HasQueryFilter` lambdas whose tenant value is not rooted in the DbContext instance (locals, static fields/properties, static method calls). EF Core caches the model — the first value is baked in and reused for every tenant. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL012.md)
- **TL013** (Warning) / **TL113** (Info): detects raw ADO.NET SQL (`DbCommand.CommandText` assignment, DbCommand-derived constructor) that references a tenant table. Same severity logic as TL002/TL003. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL013.md)
- **TL014** (Warning): detects ABP tenant-filter bypass — `IDataFilter.Disable<IMultiTenant>()`, `IDataFilter<IMultiTenant>.Disable()`, and `ICurrentTenant.Change()`. `[CrossTenant("reason")]` exempts. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL014.md)
- **TL015** (Warning): detects Finbuckle `TenantMismatchMode.Ignore` assignment — allows entities to be saved with a mismatched TenantId. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL015.md)
- **TL201** (Warning): detects `#pragma warning disable TLxxx` without a justification comment on the same or previous line. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL201.md)
- **TL202** (Warning): detects `[SuppressMessage]` targeting TL rules without a `Justification` property or with a placeholder value. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL202.md)
- **TL203** (Warning): detects TL rules globally disabled via `<NoWarn>` in project file or `.editorconfig` `severity = none`. [Docs](https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/TL203.md)

### Fixed

- TL005 code fix: use the actual `OnModelCreating` parameter name instead of hardcoding `modelBuilder`.
- TL005 code fix: generate named `HasQueryFilter("Tenant", ...)` when the DbContext uses EF Core 10 named filters or `tenantlint.tenant_filter_name` is configured.
- TL005 code fix: avoid lambda parameter name collision with identifiers already in scope.

### Configuration

- New `.editorconfig` keys: `tenantlint.filtered_base_types` (DbContext bases known to apply tenant filters), `tenantlint.tenant_marker_interfaces` (interfaces marking entities as tenant-scoped).

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

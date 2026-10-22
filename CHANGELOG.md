# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Project scaffold: solution structure, build props, CI workflow.
- `TenantModel`: per-compilation discovery of tenant-scoped entities from DbContext, `HasQueryFilter`, `IEntityTypeConfiguration`, and convention (DbSet + tenant property).
- `.editorconfig` configuration: `tenantlint.tenant_property_names`, `tenantlint.tenant_column_names`, `tenantlint.global_entities`, `tenantlint.tenant_filter_name`, `tenantlint.cross_tenant_attribute`.
- Dynamic query filter detection (TL900): informs when `foreach` over `GetEntityTypes()` with `HasQueryFilter`/`SetQueryFilter` is detected, reducing TL005 to Info severity for that context.
- Table name collection: entity name, DbSet property name, `[Table]` attribute, `ToTable()`, plus snake_case and lowercase variants.
- TL901 (Info): `[CrossTenant]` attribute should provide a reason string.
- TL902 (Warning): class-level `[CrossTenant]` suppresses tenant checks for all methods — prefer method-level suppression.
- Tri-state `FilterState` (`Filtered`, `Unfiltered`, `Unknown`) replacing boolean `HasExplicitFilter`. Dynamic filter contexts now mark entities as `Unknown` instead of silently treating them as filtered.
- Inheritance filter propagation: `HasQueryFilter` on a base entity type is propagated to derived entity types exposed as `DbSet<T>`.
- Lambda parameter verification: `HasQueryFilter` lambda analysis now verifies the member access is on the entity parameter, preventing false tenant-filter classification from unrelated objects.
- EF Core 10 named-filter API verified: `HasQueryFilter(string, Expression<Func<T, bool>>)` and `IgnoreQueryFilters(IQueryable<T>, IReadOnlyCollection<string>)` confirmed against EF Core 10.0.0.
- **TL001** (Warning): detects parameterless `IgnoreQueryFilters()` on tenant-scoped entities, including `ExecuteUpdate`/`ExecuteDelete` chains. Provides two code fixes: selective `IgnoreQueryFilters(["..."])` (EF Core 10+) and `[CrossTenant("")]` attribute.
- TL001 code fix: rewrite to selective `IgnoreQueryFilters(["SoftDelete", ...])` when EF Core 10 named filters are available; add `[CrossTenant("")]` when `CrossTenantAttribute` exists in compilation.
- `TenantEntityInfo.TenantFilterNames`: tracks which named filters reference the tenant property, enabling TL001 to compute non-tenant filter names for selective suggestions.
- TL901 now also flags placeholder/empty reasons: `""`, `"TODO"`, `"reason"`, `"n/a"`, `"TODO: reason"`.
- TL901/TL902 detection implemented via symbol actions on methods and named types.
- EF Core 10 test infrastructure: `ReferenceAssemblies.Net.Net100` with EF Core 10.0.0 packages for analyzer test compilations.
- **TL002** (Warning/Info): detects `ExecuteSqlRaw`, `ExecuteSql`, `ExecuteSqlInterpolated`, `SqlQueryRaw`, `SqlQuery` (and `Async` variants) whose constant or interpolated SQL text references a known tenant table name. Downgrades to Info when a tenant column appears in a `WHERE`/`ON`/`AND`/`OR` clause with a parameter placeholder. No code fix in v0.1.
- `SqlAnalyzer`: reusable helper for word-boundary table name matching and predicate-clause tenant column detection, shared by TL002 and (future) TL003.
- FromSql verification: confirmed EF Core composes global query filters over composable `FromSqlRaw`/`FromSqlInterpolated` queries; `FromSql*` excluded from TL002 scope.

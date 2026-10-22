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

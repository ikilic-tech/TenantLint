# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Project scaffold: solution structure, build props, CI workflow.
- `TenantModel`: per-compilation discovery of tenant-scoped entities from DbContext, `HasQueryFilter`, `IEntityTypeConfiguration`, and convention (DbSet + tenant property).
- `.editorconfig` configuration: `tenantlint.tenant_property_names`, `tenantlint.tenant_column_names`, `tenantlint.global_entities`, `tenantlint.tenant_filter_name`, `tenantlint.cross_tenant_attribute`.
- Dynamic query filter detection (TL900): informs when `foreach` over `GetEntityTypes()` with `HasQueryFilter`/`SetQueryFilter` is detected, disabling TL005 for that context.
- Table name collection: entity name, DbSet property name, `[Table]` attribute, `ToTable()`, plus snake_case and lowercase variants.

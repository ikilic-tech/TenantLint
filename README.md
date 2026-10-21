# TenantLint

Roslyn analyzers that catch cross-tenant data leaks in multi-tenant .NET applications at build time.

AI coding agents write a growing share of code. In multi-tenant SaaS, the most dangerous mistake they make is silent: a query without the tenant filter, an `IgnoreQueryFilters()` that strips tenant isolation, raw SQL that bypasses global filters. TenantLint plugs into the build so every `dotnet build` flags code that can leak one tenant's data to another.

## Install

```
dotnet add package TenantLint.Analyzers
```

No configuration required. Works with plain EF Core `HasQueryFilter`, Finbuckle, ABP, or any hand-rolled multi-tenant setup.

## Rules

| ID | Severity | Description |
|----|----------|-------------|
| TL001 | Warning | `IgnoreQueryFilters()` removes tenant isolation |
| TL002 | Warning | Raw SQL via EF Core mentions tenant table without tenant filter |
| TL003 | Warning | Dapper query mentions tenant table without tenant filter |
| TL004 | Warning | Tenant identity read from client input |
| TL005 | Warning | Tenant-scoped entity has no query filter |

## Configuration

Configure via `.editorconfig`:

```ini
[*.cs]
tenantlint.tenant_property_names = TenantId, OrganizationId
tenantlint.tenant_column_names   = tenant_id, organization_id
tenantlint.global_entities       = Tenant, AuditLog, Country
```

## Using TenantLint with AI coding agents

Agents see warnings in `dotnet build` output. Add this to your project's agent instructions:

> Treat TLxxx diagnostics as errors; never suppress them without a `[CrossTenant(reason)]` attribute.

## License

MIT - Ibrahim Kilic

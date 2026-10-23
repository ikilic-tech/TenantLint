# TenantLint

Roslyn analyzers that catch **cross-tenant data leaks** in multi-tenant .NET applications at build time.

[![NuGet](https://img.shields.io/nuget/v/TenantLint.Analyzers.svg)](https://www.nuget.org/packages/TenantLint.Analyzers)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TenantLint.Analyzers.svg)](https://www.nuget.org/packages/TenantLint.Analyzers)
[![CI](https://github.com/ikilic-tech/TenantLint/actions/workflows/ci.yml/badge.svg)](https://github.com/ikilic-tech/TenantLint/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## The problem

In multi-tenant SaaS, the most dangerous bugs are silent: a LINQ query
without a tenant filter returns data from every tenant, an
`IgnoreQueryFilters()` call strips tenant isolation, and raw SQL bypasses
global filters entirely. These bugs ship to production without a single
test failure.

```csharp
// This returns ALL customers across ALL tenants:
var customers = db.Customers.IgnoreQueryFilters().ToList();
//                          ^^^^^^^^^^^^^^^^^^^^ TL001
```

AI coding agents (Claude Code, Cursor, Copilot) write a growing share of
production code. They run `dotnet build` constantly but have no awareness
of multi-tenant invariants unless the build output tells them. TenantLint
plugs into every build — in the IDE, in CI, or inside an agent's loop —
and flags code that can leak one tenant's data to another.

## Install

```
dotnet add package TenantLint.Analyzers
```

No configuration required. Works with plain EF Core `HasQueryFilter`,
Finbuckle, ABP, or any hand-rolled multi-tenant setup. No base classes,
no interfaces, no runtime dependency.

> After updating, if your IDE still shows old diagnostics, reload the
> project or restart the IDE.

## Quick example

Build output when TL001 fires:

```
SessionRepository.cs(69,28): warning TL001: 'Session' query removes the tenant
  filter via IgnoreQueryFilters(). Use IgnoreQueryFilters(["SoftDelete"]) to lift
  only the filter you need, or mark the method [CrossTenant("reason")].
```

The fix — lift only the non-tenant filter (EF Core 10+):

```csharp
// Before (removes ALL filters, including the tenant filter):
var sessions = db.Sessions.IgnoreQueryFilters().Where(...);

// After (removes only the SoftDelete filter, tenant filter stays):
var sessions = db.Sessions.IgnoreQueryFilters(["SoftDelete"]).Where(...);
```

On EF Core 8/9 (no selective overload), mark the method instead:

```csharp
[CrossTenant("Pre-auth: validates security stamp before tenant is known")]
public async Task<string?> GetStampAsync(Guid userId) { ... }
```

## Rules

### Query filters

| ID | Severity | What it detects | Fix | Docs |
|----|----------|----------------|-----|------|
| TL001 | Warning | `IgnoreQueryFilters()` on tenant-scoped entity | Selective `IgnoreQueryFilters(["..."])` (EF 10+) or `[CrossTenant]` | [TL001](docs/rules/TL001.md) |
| TL005 | Warning | Tenant-scoped entity has no `HasQueryFilter` | Inserts `HasQueryFilter` stub | [TL005](docs/rules/TL005.md) |
| TL105 | Info | Same as TL005, but a dynamic filter loop was detected — verify manually | -- | [TL005](docs/rules/TL005.md) |
| TL011 | Warning | Pooled DbContext has tenant filter rooted in constructor-only field | -- | [TL011](docs/rules/TL011.md) |
| TL111 | Info | Same as TL011, but fires regardless of pooling (opt-in, **disabled by default**) | -- | [TL011](docs/rules/TL011.md) |
| TL012 | Warning | `HasQueryFilter` lambda references value not rooted in DbContext instance | -- | [TL012](docs/rules/TL012.md) |

### Raw SQL

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL002 | Warning | EF Core raw SQL (`ExecuteSqlRaw`, `SqlQuery`, etc.) references tenant table without tenant WHERE | [TL002](docs/rules/TL002.md) |
| TL102 | Info | Same as TL002, but tenant column appears in a predicate — verify manually | [TL002](docs/rules/TL002.md) |
| TL003 | Warning | Dapper `SqlMapper` call references tenant table without tenant WHERE | [TL003](docs/rules/TL003.md) |
| TL103 | Info | Same as TL003, for Dapper | [TL003](docs/rules/TL003.md) |
| TL013 | Warning | Raw ADO.NET SQL (`DbCommand.CommandText`) references tenant table without tenant WHERE | [TL013](docs/rules/TL013.md) |
| TL113 | Info | Same as TL013, for ADO.NET | [TL013](docs/rules/TL013.md) |
| TL009 | Warning | PostgreSQL RLS session variable (`SET app.x` / `set_config(..., false)`) uses session scope — leaks across pooled connections | [TL009](docs/rules/TL009.md) |

### Request input

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL004 | Warning | Tenant identity from client input (`[FromHeader]`, `[FromQuery]`, `[FromForm]`, direct header/query/form reads) | [TL004](docs/rules/TL004.md) |
| TL104 | Info | Tenant identity from `[FromRoute]` — legitimate when authorized against claims | [TL004](docs/rules/TL004.md) |
| TL010 | Warning | Tenant-scoped entity bound directly from request body (over-posting) | [TL010](docs/rules/TL010.md) |

### Lifetime and state

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL006 | Warning | Cache key without tenant identifier (`IMemoryCache`, `IDistributedCache`, `HybridCache`) | [TL006](docs/rules/TL006.md) |
| TL007 | Warning | Tenant context in `static` field/property, or singleton capturing scoped tenant service | [TL007](docs/rules/TL007.md) |
| TL008 | Info | Background service uses tenant-scoped DbContext without creating a scope or setting tenant | [TL008](docs/rules/TL008.md) |

### Framework-specific

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL014 | Warning | ABP tenant-filter bypass (`IDataFilter.Disable<IMultiTenant>()`, `ICurrentTenant.Change()`) | [TL014](docs/rules/TL014.md) |
| TL015 | Warning | Finbuckle `TenantMismatchMode.Ignore` allows silent cross-tenant writes | [TL015](docs/rules/TL015.md) |

### Anti-tamper

These rules detect attempts to silently disable tenant isolation checks.

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL201 | Warning | `#pragma warning disable TLxxx` without a justification comment | [TL201](docs/rules/TL201.md) |
| TL202 | Warning | `[SuppressMessage]` targeting a TL rule without a `Justification` | [TL202](docs/rules/TL202.md) |
| TL203 | Warning | TL rules globally disabled via `<NoWarn>` or `.editorconfig` `severity = none` | [TL203](docs/rules/TL203.md) |

### Helpers

| ID | Severity | What it detects | Docs |
|----|----------|----------------|------|
| TL900 | Info | Dynamic query filter loop detected in DbContext (informational) | [TL900](docs/rules/TL900.md) |
| TL901 | Info | `[CrossTenant]` attribute has no reason or a placeholder reason | [TL901](docs/rules/TL901.md) |
| TL902 | Warning | Class-level `[CrossTenant]` suppresses tenant checks for all methods — prefer method-level | [TL902](docs/rules/TL902.md) |

## Supported

| | Supported |
|---|-----------|
| EF Core | 8.0+ (named filters on 10.0+) |
| .NET | 8.0+ |
| Frameworks | Finbuckle.MultiTenant, ABP Framework |
| Raw SQL | EF Core `ExecuteSqlRaw` / `SqlQuery`, Dapper `SqlMapper`, ADO.NET `DbCommand` |
| ASP.NET Core | MVC controllers, minimal APIs (`MapPost`/`MapPut`/`MapPatch`) |

### Out of scope

- **EF6 and .NET Framework** — Roslyn analyzer infrastructure requires the .NET SDK.
- **Database-per-tenant and schema-per-tenant** — TenantLint targets shared-database (row-level) multi-tenancy where isolation comes from query filters or WHERE clauses.
- **Runtime behavior** — TenantLint is static analysis. It catches patterns that are likely to leak, but it cannot prove the absence of leaks. Runtime testing and code review remain necessary.

## Configuration

Zero configuration required by default. Customize via `.editorconfig`:

```ini
[*.cs]
# Property names that identify the tenant (default: TenantId)
tenantlint.tenant_property_names = TenantId, OrganizationId, WorkspaceId

# Column names for raw SQL analysis (default: derived from property names)
tenantlint.tenant_column_names   = tenant_id, organization_id

# Entities that are never tenant-scoped (shared/global data)
tenantlint.global_entities       = Tenant, AuditLog, Country

# EF Core 10 named filter that carries tenant isolation
tenantlint.tenant_filter_name    = Tenant

# Attribute that marks intentional cross-tenant access
tenantlint.cross_tenant_attribute = CrossTenant

# DbContext bases (external) known to apply tenant filters
tenantlint.filtered_base_types   = MyCompany.Data.TenantDbContextBase

# Interfaces marking entities as tenant-scoped
tenantlint.tenant_marker_interfaces = ITenantEntity, IMustHaveTenant
```

Change severity per rule:

```ini
[*.cs]
dotnet_diagnostic.TL001.severity = error    # treat as build error
dotnet_diagnostic.TL005.severity = none     # disable
dotnet_diagnostic.TL111.severity = warning  # opt in to pooling warning
```

### Intentional cross-tenant access

Mark methods that intentionally bypass tenant isolation with a
`[CrossTenant("reason")]` attribute. TenantLint recognizes any attribute
with the simple name `CrossTenant` in any namespace — no runtime dependency
needed. Copy this into your codebase:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal sealed class CrossTenantAttribute(string reason = "") : Attribute
{
    public string Reason { get; } = reason;
}
```

## Using TenantLint with AI coding agents

AI coding agents run `dotnet build` on every iteration. TenantLint
warnings appear in build output, giving the agent immediate feedback when
it writes code that can leak tenant data. No MCP server setup is required.

Add this to your project's agent instructions file (`CLAUDE.md`,
`AGENTS.md`, `.cursorrules`, `.github/copilot-instructions.md`):

```
Treat TLxxx diagnostics as errors; never suppress them without a
[CrossTenant("reason")] attribute. When TenantLint reports a warning,
fix the root cause — do not disable the rule.
```

The anti-tamper rules (TL201, TL202, TL203) catch attempts to silently
suppress tenant checks — whether by a human or by an agent that decides
`#pragma warning disable` is easier than fixing the issue.

## Code fixes

| Rule | Code fix |
|------|----------|
| TL001 | Rewrite `IgnoreQueryFilters()` to selective `IgnoreQueryFilters(["SoftDelete"])` (EF Core 10+). Always offers `[CrossTenant("TODO: reason")]`. |
| TL005 | Insert `HasQueryFilter(e => e.TenantId == TODO_SET_TENANT_ACCESSOR)` stub into `OnModelCreating`. |

> **Caution — Fix All:** "Fix All in Project/Solution" changes runtime
> behavior. Review each site first. Mark intentional cross-tenant methods
> with `[CrossTenant("reason")]` before applying the fix to the rest.

## Validation

Tested against 6 open-source multi-tenant .NET projects across three
rounds of validation.

**Round 1** (3 projects, initial implementation): 18 diagnostics, 8 TP,
10 FP — **56% false positive rate.** All FPs were from framework-managed
filters invisible to static analysis (ABP, Finbuckle).

**Round 2** (same 3 projects, after adding Finbuckle/ABP recognition and
expanded entity discovery): 11 diagnostics, 11 TP, 0 FP — **0% false
positive rate.**

**Round 3** (all 6 projects, all rules): 15 diagnostics, 14 TP, 1 FP —
**6.7% false positive rate.**

| Project | Approach | Diagnostics | TP | FP |
|---------|----------|:-----------:|:--:|:--:|
| [PlatformPlatform](https://github.com/nicdev/PlatformPlatform) | Dynamic named filters | 3 | 3 | 0 |
| [fullstackhero starter-kit](https://github.com/nicdev/fullstackhero-dotnet-microservices-boilerplate) | Finbuckle + explicit TenantId | 9 | 8 | 1 |
| [ABP BookStore](https://github.com/nicdev/abp-samples) | ABP `IMultiTenant` | 0 | 0 | 0 |
| [EntityFrameworkCoreMultiTenancy](https://github.com/khalidabuhakmeh/EntityFrameworkCoreMultiTenancy) | Hand-rolled HasQueryFilter | 1 | 1 | 0 |
| [saas-starter-dotnet](https://github.com/arslanahmadpk/saas-starter-dotnet) | Dynamic reflection loop | 2 | 2 | 0 |
| [MultiTenant](https://github.com/bibekpandey0521/MultiTenant) | Finbuckle + Identity | 0 | 0 | 0 |
| **Total** | | **15** | **14** | **1** |

The single false positive is a TL007 heuristic match on an immutable
static DTO default field in a class named `TenantThemeDto`.

Three of the six projects were used to guide development (Rounds 1-2).
The other three were held out until all rules were implemented, then
re-run after fixing two detection gaps they revealed (dynamic filter
recognition and `Request.Query.TryGetValue()` coverage). Two of the
three held-out projects are small single-DbContext demos.

Full results: [`docs/validation/`](docs/validation/2026-10-oss-validation.md).

## How is this different from...

| Tool | What it does | Gap TenantLint fills |
|------|-------------|---------------------|
| **Finbuckle.MultiTenant** | Runtime multi-tenant framework (tenant resolution, per-tenant config) | No compile-time checks. TenantLint works alongside Finbuckle and recognizes its `IsMultiTenant()` configuration. |
| **Knara.MultiTenant.IsolationEnforcer** | Roslyn analyzer, but requires its own base classes and repository pattern | TenantLint is framework-agnostic — works with any EF Core setup, no base classes required. |
| **mcp-tenant-isolation** | TypeScript CLI with 57 rules for Node.js apps | No .NET support. TenantLint covers the .NET ecosystem. |

## Vision

TenantLint is the start of a compile-time safety layer for multi-tenant
.NET applications. As AI agents write more production code, analyzers
that run on every build are the most natural feedback loop — they require
no setup from the agent and integrate with every .NET tool from Visual
Studio to CI pipelines. Tenant isolation is the starting point because a
single missing filter can expose every customer's data.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

See [SECURITY.md](SECURITY.md). No third-party vulnerabilities were found
during validation.

## Sample app

The [`samples/SampleSaaS`](samples/SampleSaaS) project demonstrates one
violation per rule. Build it to see TenantLint in action:

```
dotnet build samples/SampleSaaS/SampleSaaS.csproj
```

## License

[MIT](LICENSE) — Ibrahim Kılıç

# TenantLint

Roslyn analyzers that catch **cross-tenant data leaks** in multi-tenant .NET applications at build time.

In multi-tenant SaaS, the most dangerous bugs are silent: a LINQ query without a tenant filter returns data from every tenant. An `IgnoreQueryFilters()` call strips tenant isolation. Raw SQL bypasses global filters entirely. These bugs ship to production without a single test failure.

```csharp
// This returns ALL customers across ALL tenants:
var customers = db.Customers.IgnoreQueryFilters().ToList();
//                          ^^^^^^^^^^^^^^^^^^^^ TL001
```

TenantLint plugs into `dotnet build` so every build — in the IDE, in CI, or inside an AI coding agent's loop — flags code that can leak one tenant's data to another.

## Install

```
dotnet add package TenantLint.Analyzers
```

No configuration required. Works with plain EF Core `HasQueryFilter`, Finbuckle, ABP, or any hand-rolled multi-tenant setup. No base classes, no interfaces, no runtime dependency.

## Supported versions

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| .NET SDK  | 8.0     | 10.0        |
| EF Core   | 8.0     | 10.0 (named filters) |
| C#        | 12      | latest      |

EF6 and .NET Framework projects are not supported.

## Demo

![TenantLint in action](docs/assets/demo.gif)

## Rules

| ID | Severity | What it detects | Code fix | Docs |
|----|----------|----------------|----------|------|
| [TL001](docs/rules/TL001.md) | Warning | `IgnoreQueryFilters()` on tenant-scoped entity removes tenant filter | Rewrite to selective `IgnoreQueryFilters(["..."])` (EF 10+), or add `[CrossTenant]` | [TL001](docs/rules/TL001.md) |
| [TL002](docs/rules/TL002.md) | Warning | EF Core raw SQL (`ExecuteSqlRaw`, `SqlQuery`, etc.) references tenant table without tenant WHERE clause | -- | [TL002](docs/rules/TL002.md) |
| [TL003](docs/rules/TL003.md) | Warning | Dapper `SqlMapper` call references tenant table without tenant WHERE clause | -- | [TL003](docs/rules/TL003.md) |
| [TL005](docs/rules/TL005.md) | Warning | Tenant-scoped entity exposed as `DbSet<T>` has no `HasQueryFilter` | Inserts `HasQueryFilter` stub into `OnModelCreating` | [TL005](docs/rules/TL005.md) |

> **Caution — Fix All:** "Fix All in Project/Solution" changes runtime behavior. Mark intentional cross-tenant methods with `[CrossTenant("reason")]` first, then apply the fix to the rest.

### Info-level variants

These diagnostics share documentation with their parent rule but fire at **Info** severity for cases where the analyzer detects a possible (but unverified) mitigation:

| ID | Parent | When it fires |
|----|--------|--------------|
| TL102 | TL002 | Raw SQL references tenant table, but a tenant column appears in a predicate clause — verify manually |
| TL103 | TL003 | Same as TL102, for Dapper calls |
| TL105 | TL005 | Entity may be missing a tenant filter, but a dynamic filter loop was detected — verify manually |

### Supplementary diagnostics

| ID | Severity | What it detects |
|----|----------|----------------|
| TL900 | Info | Dynamic query filter loop detected in DbContext (informational) |
| TL901 | Info | `[CrossTenant]` attribute has no reason or a placeholder reason |
| TL902 | Warning | Class-level `[CrossTenant]` suppresses tenant checks for all methods — prefer method-level |

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
```

Change severity per rule with the standard EditorConfig syntax:

```ini
[*.cs]
dotnet_diagnostic.TL001.severity = error
dotnet_diagnostic.TL005.severity = none
```

### Intentional cross-tenant access

Mark methods that intentionally access data across tenants with a `[CrossTenant("reason")]` attribute. TenantLint recognizes any attribute with the simple name `CrossTenant` in any namespace — you do not need a runtime dependency. Copy this into your codebase:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal sealed class CrossTenantAttribute(string reason = "") : Attribute
{
    public string Reason { get; } = reason;
}
```

## Using TenantLint with AI coding agents

AI coding agents (Claude Code, Cursor, Copilot, etc.) run `dotnet build` constantly. TenantLint warnings appear in build output, giving the agent immediate feedback when it writes code that can leak tenant data.

Add this to your project's agent instructions file (e.g., `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`):

```
Treat TLxxx diagnostics as errors; never suppress them without a [CrossTenant("reason")] attribute.
When TenantLint reports a warning, fix the root cause — do not disable the rule.
```

No MCP server setup is required. The analyzer works through standard build output.

## How is this different from...

| Tool | What it does | Gap TenantLint fills |
|------|-------------|---------------------|
| **Finbuckle.MultiTenant** | Runtime multi-tenant framework (tenant resolution, per-tenant config) | No compile-time checks. TenantLint works alongside Finbuckle. |
| **Knara.MultiTenant.IsolationEnforcer** | Roslyn analyzer, but requires its own base classes and repository pattern | TenantLint is framework-agnostic — works with any EF Core setup. |
| **mcp-tenant-isolation** | TypeScript CLI with 57 rules for Node.js apps | No .NET support. TenantLint covers the .NET ecosystem. |

## Vision

TenantLint is the first piece of a broader compile-time safety layer for AI-written code in multi-tenant applications. As AI agents write more production code, the gap between "code that compiles" and "code that is correct" grows. Analyzers that run on every build are the most natural checkpoint — they require no setup from the agent, produce structured feedback, and integrate with every .NET tool from Visual Studio to CI pipelines. TenantLint starts with tenant isolation because it is the highest-stakes correctness property in multi-tenant SaaS: a single missing filter can expose every customer's data.

## Sample app

The [`samples/SampleSaaS`](samples/SampleSaaS) project contains one deliberate violation per rule. Build it to see TenantLint in action:

```
dotnet build samples/SampleSaaS/SampleSaaS.csproj
```

## License

MIT — Ibrahim Kilic

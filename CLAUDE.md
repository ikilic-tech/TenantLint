# TenantLint — Project Brief

> Read this whole file before writing any code. It is the single source of truth for scope, architecture and conventions. When something here conflicts with a later instruction from the maintainer, the maintainer wins — then update this file.

## 1. What we are building

**TenantLint** is a set of Roslyn analyzers (plus code fixes) that catch **cross-tenant data leaks** in multi-tenant .NET applications at build time.

One-line pitch: *Drop it into an existing multi-tenant EF Core app with `dotnet add package TenantLint.Analyzers` — no framework migration, no config — and every build (in the IDE, in CI, or inside an AI coding agent's loop) flags code that can leak one tenant's data to another.*

### Why it matters now
AI coding agents (Claude Code, Cursor, Copilot) write a growing share of code. In multi-tenant SaaS, the most dangerous mistake they make is silent: a query without the tenant filter, an `IgnoreQueryFilters()` that strips tenant isolation, raw SQL that bypasses global filters, a tenant id taken from a request header. Agents run `dotnet build` constantly — so analyzer warnings in build output are the cheapest feedback loop we can plug into. **No MCP setup is required for the core value.**

### Positioning vs. existing tools (do not copy them; differentiate)
| Tool | Gap we fill |
|---|---|
| `mcp-tenant-isolation` (TypeScript CLI, 57 rules) | No .NET support; intra-function analysis only |
| `Knara.MultiTenant.IsolationEnforcer.Analyzers` | Forces you onto its own repository/base classes |
| Themia / EricksonLopez analyzers | 2–3 rules each, tied to their own frameworks |
| Finbuckle, Tenantry, TenancyKit, SaasSuite | Runtime only, no compile-time checks |

**Our differentiators — protect them in every design decision:**
1. **Framework-agnostic.** Works with plain EF Core `HasQueryFilter`, Finbuckle, ABP, or a hand-rolled setup. We *discover* the user's tenant model; we never require base classes, interfaces or attributes from us.
2. **Zero config by default**, tunable via `.editorconfig`.
3. **Agent-friendly diagnostics.** Messages state *what* leaks, *why*, and *the exact fix*, so an AI agent reading build output can repair the code without extra context.
4. **Low false positives.** A noisy analyzer gets disabled. When unsure, report at a lower severity or not at all.

## 2. Repository layout

```
TenantLint/
├─ CLAUDE.md
├─ README.md
├─ LICENSE                      # MIT, copyright Ibrahim Kılıç
├─ CONTRIBUTING.md
├─ CHANGELOG.md
├─ Directory.Build.props        # shared props: LangVersion latest, Nullable enable, TreatWarningsAsErrors (src only), deterministic
├─ Directory.Packages.props     # Central Package Management
├─ .editorconfig
├─ global.json                  # pin SDK to .NET 8 (8.0.401, rollForward latestFeature); samples/ has its own global.json for .NET 10
├─ assets/
│  └─ icon.png                  # 128x128 package icon (blue shield with "TL")
├─ TenantLint.sln
├─ src/
│  ├─ TenantLint.Analyzers/     # netstandard2.0 — DiagnosticAnalyzers only
│  ├─ TenantLint.CodeFixes/     # netstandard2.0 — CodeFixProviders (references Workspaces)
│  └─ TenantLint.Package/       # packs Analyzers + CodeFixes into analyzers/dotnet/cs, PackageId = TenantLint.Analyzers
├─ tests/
│  └─ TenantLint.Analyzers.Tests/   # net8.0 + xUnit, Roslyn analyzer testing framework
├─ samples/
│  └─ SampleSaaS/               # net10.0 minimal API + EF Core 10; contains deliberate violations, used for demos and dogfooding
├─ docs/
│  └─ rules/TL001.md … TL009.md # one page per rule; diagnostics link here via helpLinkUri
└─ .github/workflows/ci.yml
```

### Project settings that must be right
- `TenantLint.Analyzers` and `TenantLint.CodeFixes`: `TargetFramework=netstandard2.0`, `EnforceExtendedAnalyzerRules=true`, `IsRoslynComponent=true`, `IncludeBuildOutput=false` where appropriate.
- Reference `Microsoft.CodeAnalysis.CSharp` (Analyzers) and `Microsoft.CodeAnalysis.CSharp.Workspaces` (CodeFixes) with `PrivateAssets=all`. Pin to a **4.x version old enough to load in the oldest IDE/SDK we support** (start with 4.8.0 for .NET 8 SDK compatibility); do not chase the newest Roslyn.
- Add `Microsoft.CodeAnalysis.Analyzers` and follow its rules (RS1xxx). Treat its warnings as errors.
- Tests: use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` with `DefaultVerifier` (the old `*.XUnit` packages are deprecated). Test compilations need EF Core and ASP.NET Core references: use `ReferenceAssemblies.Net.Net80.AddPackages(...)` with `Microsoft.EntityFrameworkCore` (and `Dapper` for TL003); add a second reference set with EF Core 10 for named-filter tests.
- Package: `PackageId=TenantLint.Analyzers`, `DevelopmentDependency=true`, `NoPackageAnalysis=true`, include README and an icon, `PackageLicenseExpression=MIT`, SourceLink, deterministic build. Publish via GitHub Actions with NuGet Trusted Publishing (OIDC) — no long-lived API keys.

## 3. Core concept: the Tenant Model

Every rule depends on knowing **which entity types are tenant-scoped** and **what the tenant property is called**. Build this once per compilation in a `TenantModel` (created in `RegisterCompilationStartAction`, cached, immutable, thread-safe).

### Discovery (in priority order)
1. **Explicit filters.** Find all types deriving from `Microsoft.EntityFrameworkCore.DbContext`. In `OnModelCreating` and in every `IEntityTypeConfiguration<T>.Configure`, find `HasQueryFilter(...)` calls (both the classic overload and the EF Core 10 named overload `HasQueryFilter("Name", expr)`). If the lambda body references a member whose name is in the tenant property list, mark the entity tenant-scoped **and** record the filter name (if any). **Follow one level of method calls from `OnModelCreating`**: if `OnModelCreating` calls a method within the same compilation that receives the `ModelBuilder` parameter, analyze that method's body for `HasQueryFilter`, `ToTable`, `Entity<T>()`, and framework-specific calls (e.g. `IsMultiTenant()`). Two-level calls are not followed (by design).
2. **Convention.** An entity exposed as `DbSet<T>` on a DbContext that has a property named in the tenant property list is tenant-scoped (even if unfiltered — that is exactly what TL005 reports).
3. **Extended entity discovery.** Discover tenant entities from additional sources beyond `DbSet<T>` properties: (a) `modelBuilder.Entity<T>()` calls in `OnModelCreating` and helper methods; (b) `context.Set<T>()` calls across the compilation (e.g. in repository classes); (c) `IEntityTypeConfiguration<T>` implementations (register the entity even if no `DbSet<T>` exists). Entities discovered this way must have a property matching the tenant property list (or implement a `tenant_marker_interfaces` interface) to be considered tenant-scoped.
4. **Framework recognition** (detect by symbol, not name strings):
   - **Finbuckle**: `EntityTypeBuilder.IsMultiTenant()` (on `Finbuckle.MultiTenant.EntityTypeBuilderExtensions`), `[Finbuckle.MultiTenant.MultiTenantAttribute]` on entity types, DbContexts deriving from `Finbuckle.MultiTenant.EntityFrameworkCore.MultiTenantDbContext` or implementing `IMultiTenantDbContext`. Entities become tenant-scoped with `FilterState.Filtered` and `TenantPropertyName = "TenantId"` (Finbuckle's shadow property). TL001 fires on `IgnoreQueryFilters()` for them.
   - **ABP**: Entities implementing `Volo.Abp.MultiTenancy.IMultiTenant` in a DbContext deriving from `Volo.Abp.EntityFrameworkCore.AbpDbContext<T>`. Mark as tenant-scoped with `FilterState.Filtered` and `TenantPropertyName = "TenantId"`.
5. **Dynamic filters.** If `OnModelCreating` (or a followed helper method) loops over `modelBuilder.Model.GetEntityTypes()` and calls `HasQueryFilter` / `SetQueryFilter` dynamically, we cannot determine which entities are covered or whether the filters are tenant-related. Mark convention-tenant entities in that context as `FilterState.Unknown` (not `Filtered`). TL005 still reports for `Unknown` entities, but at `Info` severity instead of `Warning`, with the message: "entity may be missing a tenant filter; dynamic filter detected in <Context> — verify manually." Log TL900 (Info) once per affected context: "dynamic query filter detected in <Context>; TL005 reduced to Info for this context."
6. **External base safety rule.** If a DbContext derives from a base class defined in another assembly (other than EF Core's `DbContext` or a recognized framework base like `MultiTenantDbContext` / `AbpDbContext<T>`), set `FilterState.Unknown` for its convention-tenant entities → TL105 (Info), never TL005 (Warning). Users can override this with `tenantlint.filtered_base_types`.
7. **Table names** for raw-SQL rules: entity name, DbSet property name, `ToTable("x")` / `[Table("x")]`, plus snake_case and lowercase variants. Store them case-insensitively.
8. **Inheritance.** After all discovery phases, propagate explicit filter status from base entity types to their derived types. In EF Core, `HasQueryFilter` can only be applied to the root entity of an inheritance hierarchy, and the filter automatically applies to all derived types. Therefore, if `BaseEntity` has an explicit tenant filter and `Customer : BaseEntity` is exposed as `DbSet<Customer>`, mark `Customer` as having an explicit filter.

### Configuration (`.editorconfig`, read via `AnalyzerConfigOptionsProvider`)
```ini
[*.cs]
tenantlint.tenant_property_names = TenantId, OrganizationId, WorkspaceId   # default: TenantId
tenantlint.tenant_column_names   = tenant_id, organization_id              # default: derived from property names
tenantlint.global_entities       = Tenant, AuditLog, Country               # never tenant-scoped
tenantlint.tenant_filter_name    = Tenant                                  # EF Core 10 named filter that carries isolation
tenantlint.cross_tenant_attribute = CrossTenant                            # attribute name that marks intentional cross-tenant code
tenantlint.filtered_base_types   = MyCompany.Data.TenantDbContextBase      # DbContext bases (external) known to apply tenant filters
tenantlint.tenant_marker_interfaces = ITenantEntity, IMustHaveTenant       # interfaces marking entities as tenant-scoped
```
Users can also change severity per rule with the standard `dotnet_diagnostic.TL001.severity = error`.

### Intentional cross-tenant access
Any **method** decorated with an attribute whose **simple name** is `CrossTenant` / `CrossTenantAttribute` (any namespace; we do not ship a runtime package) is exempt from TL001–TL004 and TL010–TL013 for diagnostics within that method. Class-level `[CrossTenant]` exempts all methods but emits TL902 (Warning): "Class-level [CrossTenant] suppresses tenant checks for all methods — prefer method-level suppression." The attribute should take a reason string; if it is missing or empty, report TL901 (Info): "[CrossTenant] should provide a reason." The README shows users a 3-line attribute to copy into their codebase.

## 4. Rules

Diagnostic ID prefix `TL`, category `TenantIsolation`, `helpLinkUri = https://github.com/<owner>/TenantLint/blob/main/docs/rules/TLxxx.md`. Message format (agent-friendly):

> `TL001: '<Customers>' query removes the tenant filter via IgnoreQueryFilters(). Use IgnoreQueryFilters(["<SoftDelete>"]) to lift only the filter you need, or mark the method [CrossTenant("reason")].`

### v0.1 (ship first)

| ID | Default severity | Detects | Code fix |
|---|---|---|---|
| **TL001** | Warning | Parameterless `IgnoreQueryFilters()` on `IQueryable<T>` (including `ExecuteUpdate`/`ExecuteDelete` chains) where `T` is tenant-scoped. If the project uses EF Core ≥ 10 and named filters exist, the fix lists the non-tenant filter names. On EF Core < 10 there is no partial alternative → message points to `[CrossTenant]`. | EF 10+: rewrite to `IgnoreQueryFilters(["<other filter names>"])`. Always: add `[CrossTenant("TODO: reason")]`. |
| **TL002** | Warning | `Database.SqlQuery<T>`, `SqlQueryRaw<T>`, `ExecuteSql*`, `ExecuteSqlRaw*`, `ExecuteSqlInterpolated*` (+ `Async`) whose SQL text (literal, interpolated string, or constant) mentions a known tenant table name. Non-constant SQL → do not report. | None in v0.1. |
| **TL102** | Info | Same detection as TL002, but a tenant column name appears inside a `WHERE`/`ON`/`AND`/`OR` clause adjacent to a parameter placeholder. Filter is likely present but should be verified. | None. |
| **TL003** | Warning | Dapper `SqlMapper` extension calls (`Query*`, `QueryFirst*`, `QuerySingle*`, `QueryMultiple*`, `Execute*`, `ExecuteScalar*`, `ExecuteReader*`) with the same SQL analysis as TL002. Detect by symbol (`Dapper.SqlMapper`), not by method name alone. Covers `CommandDefinition` overloads. | None in v0.1. |
| **TL103** | Info | Same detection as TL003, but tenant column appears in a predicate clause. | None. |
| **TL005** | Warning | A tenant-scoped (by convention) entity exposed as `DbSet<T>` whose DbContext defines no query filter for it (after inheritance propagation). Reported on the `DbSet<T>` property. Skipped for `global_entities`. | Insert `modelBuilder.Entity<T>().HasQueryFilter(e => e.<TenantProp> == TODO_SET_TENANT_ACCESSOR)` where `TODO_SET_TENANT_ACCESSOR` is an undefined identifier that forces a compilation error. |
| **TL105** | Info | Same as TL005, but a dynamic query filter loop was detected in the DbContext (`FilterState.Unknown`) — the entity may be filtered at runtime. Verify manually. | None. |

### v0.2

| ID | Severity | Detects |
|---|---|---|
| **TL004** | Warning | Tenant identity taken from client input in ASP.NET Core: (a) action/endpoint parameters whose name matches the tenant property list bound via `[FromHeader]`, `[FromQuery]`, `[FromForm]`; (b) direct `Request.Headers["X-Tenant-Id"]` / `Request.Headers.TryGetValue(...)` / `Request.Query["tenantId"]` reads where the key contains a tenant property name (case-insensitive, strip `X-` prefix, strip dashes). `[FromBody]` excluded (handled by TL010). Data-flow tracking deferred to v0.3. |
| **TL104** | Info | Same as TL004 but for `[FromRoute]` parameters. Route-scoped tenants (`/orgs/{orgId}`) are a legitimate pattern when the route value is authorized against the user's claims. Message: "verify that '{0}' is authorized against the authenticated principal." |
| **TL010** | Warning | Tenant-scoped entity bound directly from the request body in ASP.NET Core (over-posting). Detects `[FromBody]`, `[FromForm]`, implicit body-binding on `[ApiController]` actions, and body-bound parameters in minimal API `MapPost`/`MapPut`/`MapPatch` handlers (lambda and method-group). A client can set the tenant property to any value. |
| **TL011** | Warning | `HasQueryFilter` lambda whose tenant value is rooted in a field only assigned in the constructor, at `AddDbContextPool` / `AddPooledDbContextFactory` registration site. Pooled instances reuse the first tenant's value. |
| **TL111** | Info (disabled) | Same detection as TL011, but fires on the DbContext type itself — regardless of how it is registered — as a heads-up that the pattern is unsafe if pooling is ever introduced. Opt-in via `.editorconfig`. |
| **TL012** | Warning | `HasQueryFilter` lambda whose tenant value is not rooted in the DbContext instance (locals, static fields/properties, static method calls). EF Core caches the model — the first value is baked in and reused for every tenant. |
| **TL013** | Warning | Raw ADO.NET SQL (`DbCommand.CommandText`, DbCommand-derived constructor) references tenant table without apparent tenant filtering. Same SQL analysis as TL002/TL003. |
| **TL113** | Info | Same detection as TL013, but tenant column appears in a predicate clause — verify manually. |
| **TL006** | Warning | `IMemoryCache` / `IDistributedCache` / `HybridCache` set/get/`GetOrCreate*` calls whose key is a constant or an interpolation with no tenant-related value, inside a method that also touches tenant-scoped data. |
| **TL007** | Warning | Tenant context stored in a `static` field/property, or a type whose name contains `Tenant` (e.g. `ITenantContext`, `ICurrentTenant`) registered as `AddSingleton`. Also a singleton capturing a scoped tenant service in its constructor. |
| **TL008** | Info | `BackgroundService` / `IHostedService` / Hangfire job methods that resolve or inject a DbContext with tenant-scoped entities without creating a scope / setting the tenant first. Heuristic → Info by default. |
| **TL009** | Warning | PostgreSQL RLS session variable set with `SET app.<x>` / `set_config(..., false)` instead of `SET LOCAL` / `set_config(..., true)` → leaks across pooled connections. |

### Verification tasks (do not skip)
- **FromSql\*** *(Verified — 2026-10-22)*: EF Core composes global query filters over composable `FromSql*` queries on a `DbSet<T>`. Confirmed via EF Core documentation and community sources for EF Core 8 and 10. `FromSqlRaw`/`FromSqlInterpolated` produce composable subqueries; EF Core wraps them and applies `WHERE` clauses from global filters. Non-composable SQL (stored procedures) cannot have filters applied. **Result:** `FromSql*` is excluded from TL002 scope — tenant filters protect these queries automatically.
- **Named filters API** *(Verified against EF Core 10.0.0 — 2026-10-22)*:
  - `EntityTypeBuilder.HasQueryFilter(LambdaExpression filter)`
  - `EntityTypeBuilder.HasQueryFilter(String filterKey, LambdaExpression filter)`
  - `EntityTypeBuilder<T>.HasQueryFilter(Expression<Func<T, bool>> filter)`
  - `EntityTypeBuilder<T>.HasQueryFilter(String filterKey, Expression<Func<T, bool>> filter)`
  - `EntityFrameworkQueryableExtensions.IgnoreQueryFilters<T>(IQueryable<T> source)` — parameterless (removes all)
  - `EntityFrameworkQueryableExtensions.IgnoreQueryFilters<T>(IQueryable<T> source, IReadOnlyCollection<string> filterKeys)` — selective
- **Mixed unnamed + named filter behavior** *(Verified via EF Core 10 source and community demos — 2026-10-22)*:
  - **Unnamed filters replace each other:** a second unnamed `HasQueryFilter` silently overwrites the first (same as EF Core 8/9).
  - **Named filters coexist:** multiple `HasQueryFilter("Name", ...)` on the same entity all survive; both reach the SQL WHERE clause.
  - **Mixing unnamed + named throws `InvalidOperationException`:** EF Core 10 does **not** allow an entity to have both unnamed and named filters. This means TL001's selective fix is safe — entities that use named filters will only have named filters.
  - **Parameterless `IgnoreQueryFilters()`** removes all filters (named and unnamed).
  - **Selective `IgnoreQueryFilters(["SoftDelete"])`** removes only the specified named filter; other named filters remain active.
  - Sources: [codewithmukesh.com](https://codewithmukesh.com/blog/global-query-filters-efcore/), [gdhami-net/ef-named-query-filters-demo](https://github.com/gdhami-net/ef-named-query-filters-demo) (tested on EF Core 10.0.12).
- **Query filter parameterization** *(Verified via spike, EF Core 8.0.32 + 10.0.12, SQLite — 2026-10-22)*:
  - **DbContext instance field** (`_tenantId`): **Parameterized correctly.** EF Core detects that the closure captures `this.<member>` and generates a parameterized SQL query. Each new DbContext instance uses its own value. **This is the only safe pattern.**
  - **DbContext instance field → property on stored service** (`_tenantService.TenantId`): **Parameterized correctly.** EF Core walks the member-access chain through `this._tenantService.TenantId` and re-evaluates per query.
  - **DbContext instance field → method call on stored service** (`_resolver.GetTenantId()`): **Parameterized correctly.** The method is re-evaluated per query because the call is on a member of `this`.
  - **Local variable** captured in `OnModelCreating`: **BAKED IN.** The value from the first `OnModelCreating` call is frozen into the cached model. All subsequent DbContext instances of the same type see the first tenant's value. **Cross-tenant data leak confirmed.**
  - **Static field** (`StaticFieldContext.CurrentTenantId`): **BAKED IN.** Same as local — the value is read once during model building and cached forever. Changing the static field has no effect. **Cross-tenant data leak confirmed.**
  - **Key insight:** EF Core's model cache parameterizes expressions that traverse from `this` (the DbContext instance). Any value not reachable through `this` is treated as a constant and baked into the cached model.
  - Results identical on EF Core 8 and 10.
- **DbContext pooling + tenant in constructor** *(Verified via spike, EF Core 8.0.32 + 10.0.12, SQLite — 2026-10-22)*:
  - **Mutable instance field** set via `SetTenant()` after pool returns the instance: **Safe.** The query filter re-reads the mutable field per query. This is the recommended pattern for pooling.
  - **Readonly field set in constructor** with hardcoded default: Constructor is called only once per pool slot. On re-use, the constructor is NOT re-invoked. The field retains its original value. With a constant default ("CTOR-DEFAULT"), this causes "no data returned" (silent failure, not a leak).
  - **Readonly field set in constructor from ambient/static source** (`_tenantId = AmbientTenantId`): **CROSS-TENANT LEAK CONFIRMED.** First pool slot created for tenant-A captures "tenant-A". When the same slot is re-used for tenant-B, the readonly field still holds "tenant-A" and the query filter returns tenant-A's data to tenant-B. Same behavior with `AddPooledDbContextFactory`.
  - **Key insight:** `AddDbContextPool` / `AddPooledDbContextFactory` recycle instances. Constructors run once per pool slot. Tenant values set in the constructor become stale. The safe pattern is a mutable field set after retrieval from the pool.
  - Results identical on EF Core 8 and 10.
- **`Find`/`FindAsync` vs query filters** *(Verified via spike, EF Core 8.0.32 + 10.0.12, SQLite — 2026-10-22)*:
  - **Find for own tenant's entity (not tracked):** Returns the entity. Query filter applies. **Safe.**
  - **Find for other tenant's entity (not tracked):** Returns `null`. Query filter prevents loading. **Safe.**
  - **Find for other tenant's entity (already tracked via `Entry().State = Unchanged`):** **RETURNS THE CROSS-TENANT ENTITY.** Find checks the change tracker first; if the entity is tracked, it returns it without applying the query filter. **Cross-tenant data leak confirmed.**
  - **Find after `IgnoreQueryFilters()` loads cross-tenant entity:** Same result — Find returns the tracked cross-tenant entity. **Cross-tenant data leak confirmed.**
  - **Regular LINQ (`ToList()`) after tracking cross-tenant entity:** Query filter still applies to the database query. Tracked cross-tenant entities are NOT included in LINQ results. **Safe.**
  - **Key insight:** `Find`/`FindAsync` is a two-step operation: (1) check change tracker, (2) query database. Step 1 does NOT apply query filters. This is by design (documented behavior) but is a tenant isolation hazard when cross-tenant entities enter the change tracker via `IgnoreQueryFilters()`, raw SQL, or manual `Attach`/`Entry` calls.
  - Results identical on EF Core 8 and 10.
- **TL012/TL011/TL111 design** *(Implemented — 2026-10-22)*: TL012 detects values not rooted in `this`; TL011 detects constructor-only fields at pool registration sites; TL111 (Info, **disabled by default**) fires on the DbContext type itself. "Constructor-only" = `readonly` OR only assigned inside constructor bodies.
- **Find/FindAsync:** Documented as a pitfall in TL001.md; no separate rule. Find checks the change tracker first and does not apply query filters to tracked entities.
- **Scope:** TenantLint targets **shared-database (row-level) multi-tenancy**. Database-per-tenant and schema-per-tenant are out of scope.
- **TL004/TL104 severity split** *(Decision — 2026-10-22)*: `[FromRoute]` demoted to TL104 (Info) because route-scoped tenants (`/orgs/{orgId}`) are a legitimate pattern when the route value is authorized against the user's claims. Header / query / form sources remain TL004 (Warning).
- **TL010 minimal API support** *(Decision — 2026-10-22)*: Detect body-bound tenant entity parameters in `MapPost`/`MapPut`/`MapPatch` delegate handlers (lambda and method-group). `MapGet`/`MapDelete` excluded — GET/DELETE do not body-bind by default.
- **TL004 direct header/query reads** *(Decision — 2026-10-22)*: Detect `Request.Headers["X-Tenant-Id"]`, `Request.Headers.TryGetValue("X-Tenant-Id", ...)`, `IHeaderDictionary` indexer, and `Request.Query["tenantId"]`. Header key matching: case-insensitive, strip `X-` prefix, strip dashes, then compare to tenant property names.
- **OSS validation** *(Completed — 2026-10-22)*: See `docs/validation/2026-10-oss-validation.md`. Tested against PlatformPlatform, fullstackhero/Finbuckle, ABP BookStore. 8 TP, 10 FP, ~18 missed. Key gaps: `Set<T>()` entity discovery (P0), helper method following (P0), Finbuckle `IsMultiTenant()` (P1), ABP `IMultiTenant` (P1).
- **P0 — Entity discovery expansion** *(Decision — 2026-10-22)*: Discover tenant entities from `Set<T>()` calls, `modelBuilder.Entity<T>()` in OnModelCreating, and `IEntityTypeConfiguration<T>` implementations — not only from `DbSet<T>` properties. Follow one level of method calls from `OnModelCreating` (methods within the same compilation that receive the `ModelBuilder` parameter). This closes the PlatformPlatform gap (zero diagnostics because entities are only referenced via `Set<T>()`).
- **P1 — Framework recognition** *(Decision — 2026-10-22)*: Detect by symbol, not name strings.
  - **Finbuckle**: `EntityTypeBuilder.IsMultiTenant()` (on `Finbuckle.MultiTenant.EntityTypeBuilderExtensions`), `[MultiTenant]` attribute (on `Finbuckle.MultiTenant.MultiTenantAttribute`), DbContexts deriving from `MultiTenantDbContext` / implementing `IMultiTenantDbContext`. Entities become tenant-scoped + `FilterState.Filtered`, including shadow `TenantId` entities. TL001 fires on `IgnoreQueryFilters()` for them.
  - **ABP**: Entities implementing `Volo.Abp.MultiTenancy.IMultiTenant` in a DbContext deriving from `AbpDbContext<T>`. Mark as tenant-scoped + `FilterState.Filtered`. TenantPropertyName = "TenantId" from IMultiTenant interface.
- **External base safety rule** *(Decision — 2026-10-22)*: If a DbContext derives from a base class defined in another assembly (other than EF Core's `DbContext` or a recognized framework base), set `FilterState.Unknown` for its convention-tenant entities → TL105 (Info), never TL005 (Warning). Override with `tenantlint.filtered_base_types`.
- **New config options** *(Decision — 2026-10-22)*: `tenantlint.filtered_base_types` (CSV of DbContext base type names known to apply tenant filters; default empty) and `tenantlint.tenant_marker_interfaces` (CSV of interface names marking entities as tenant-scoped; default empty).

## 5. Engineering rules

- **Performance:** register syntax-node / operation actions narrowly (`OperationKind.Invocation`, `SymbolKind.Property`). Never walk the whole tree per node. Build the `TenantModel` once per compilation. Call `context.EnableConcurrentExecution()` and `ConfigureGeneratedCodeAnalysis(None)`.
- **Use the IOperation API** (`IInvocationOperation`) over raw syntax wherever possible; resolve methods by symbol (`Compilation.GetTypeByMetadataName`) — never by string-matching method names alone.
- **Filter lambda analysis:** When checking whether a `HasQueryFilter` lambda references a tenant property, verify that the member access is on the **lambda parameter** (the entity being filtered), not on an arbitrary receiver. This prevents false tenant-filter classification when unrelated objects happen to have a property matching the tenant property name.
- **No false positives over coverage.** Every rule ships with at least 3 positive and 5 negative test cases (including look-alikes: a user method named `IgnoreQueryFilters`, a non-tenant entity, a `[CrossTenant]` method, string SQL built at runtime).
- **IDE/agent experience:** diagnostic location is the narrowest meaningful span (the invocation name, the parameter, the DbSet property) so the squiggle and the build output line are precise.
- **No runtime dependency** shipped to users. The package contains analyzers only.
- **Every rule has** a `docs/rules/TLxxx.md` page with: what, why it leaks, bad/good code, how to suppress, false-positive notes.
- **Commits:** Conventional Commits (`feat(TL001): …`, `test: …`, `docs: …`). One rule per PR.
- Public code, comments, docs and messages are **in English**.

## 5a. Working agreement: CLAUDE.md is the source of truth

- Any new rule, rule change, design decision, or verified EF Core/Roslyn
  behavior that the maintainer gives in chat MUST be written into
  CLAUDE.md BEFORE implementation starts, in the same commit as the first
  related change or in a preceding "docs:" commit.
- If a prompt references a rule ID, setting, or decision that is not in
  CLAUDE.md, stop and ask for its definition — never guess.
- Verification results (spikes) are recorded in CLAUDE.md with the
  package versions and date.
- At the end of each milestone, check that CLAUDE.md matches the code
  (rules table, IDs, severities, milestones) and fix any drift.
- **Never write vulnerability details about third-party projects into
  this repository** (code, docs, commit messages). Keep them in
  `~/tenantlint-private/` and report via private channels.

## 6. Sample app (`samples/SampleSaaS`)

A .NET 10 console app + EF Core 10 (SQLite) multi-tenant sample — `Customer`, `Order`, `Invoice` (tenant-scoped), `Tenant`, `Country` (global). Uses EF Core 10 **named filters** (`"Tenant"` and `"SoftDelete"` on Customer) so TL001's selective fix suggestion (`IgnoreQueryFilters(["SoftDelete"])`) is visible in build output. Contains **one deliberate violation per rule**, each marked `// TL00x: deliberate`, so `dotnet build samples/SampleSaaS` shows every diagnostic. Has its own `samples/global.json` (pinned to .NET 10 SDK) and `samples/Directory.Build.props` (CPM disabled) to isolate from the main solution.

## 7. CI (`.github/workflows/ci.yml`)
- Matrix: ubuntu-latest, windows-latest; .NET 8 + 10 SDKs.
- `dotnet build -warnaserror` (src), `dotnet test`, `dotnet pack`.
- Build the sample (from `samples/SampleSaaS/` working directory so `samples/global.json` is used) and assert TL001/TL002/TL003/TL005 appear in build output.
- On tag `v*`: pack and publish to NuGet via **NuGet Trusted Publishing** (`NuGet/login@v1` with `secrets.NUGET_USER`, short-lived OIDC key). No long-lived `NUGET_API_KEY` secret. Create a GitHub Release with notes from CHANGELOG.

## 8. Milestones (work in this order; stop after each for maintainer review)

- **M0 — Scaffold.** *(Done.)* Solution, projects, props files, CPM, global.json, .editorconfig, empty analyzer + one dummy test passing, CI green, MIT license, README skeleton.
- **M1 — TenantModel.** *(Done.)* Discovery + `.editorconfig` options + unit tests. Post-M1 fixes applied: tri-state `FilterState` (F1), lambda parameter verification (F2), inheritance filter propagation (F3), `[CrossTenant]` scope tightening (F7).
- **M2 — TL001** *(Done.)* Code fix + docs page. Includes `ExecuteUpdate`/`ExecuteDelete` chains and selective `IgnoreQueryFilters(["..."])` for EF Core 10.
- **M3 — TL002** *(Done.)* FromSql verification confirmed filters apply; `FromSql*` excluded. Docs page with heuristic limitations.
- **M4 — TL003** *(Done.)* Dapper `SqlMapper` detection, same SQL analysis as TL002. Docs page.
- **M5 — TL005** *(Done.)* Code fix (non-compiling stub). Separate Info-severity IDs: TL102, TL103, TL105. Docs page.
- **M6 — Release v0.1.0.** *(In progress.)* Sample app (net10.0, named filters, 4 violations), README, pack verification (icon, 0.1.0-preview.1), CI with NuGet Trusted Publishing. Tag pending.

Definition of done for a milestone: builds with zero warnings in `src/`, all tests pass, docs updated, CHANGELOG entry added.

## 9. README must include
- The problem in 3 sentences with one leaking code example.
- Install: `dotnet add package TenantLint.Analyzers`.
- A table of rules with links.
- **"Using TenantLint with AI coding agents"** section: agents see warnings in `dotnet build` output; recommend adding to the project's agent instructions file: *"Treat TLxxx diagnostics as errors; never suppress them without a [CrossTenant(reason)] attribute."*
- Configuration via `.editorconfig`.
- "How is this different from…" section (Finbuckle, Knara, mcp-tenant-isolation) — respectful, factual.
- Maintainer: Ibrahim Kılıç.

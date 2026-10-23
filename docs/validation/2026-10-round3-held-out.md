# Round 3 — Held-out Validation (all rules)

**Date:** 2026-10-23
**Package:** TenantLint.Analyzers 0.1.0-preview.3-local (main @ f4b8d37)
**Rules active:** TL001–TL015, TL1xx variants, TL201–TL203, TL900–TL902
**Build flag:** `dotnet build --no-incremental` (required for non-cached analyzer runs)

## Projects

| # | Repository | Multi-tenancy approach | Framework | Why chosen |
|---|-----------|----------------------|-----------|------------|
| 1 | [khalidabuhakmeh/EntityFrameworkCoreMultiTenancy](https://github.com/khalidabuhakmeh/EntityFrameworkCoreMultiTenancy) | Hand-rolled `HasQueryFilter` + Minimal API | EF Core 8 (retargeted from 7), SQLite | Minimal API, JetBrains blog reference, simple hand-rolled filter |
| 2 | [arslanahmadpk/saas-starter-dotnet](https://github.com/arslanahmadpk/saas-starter-dotnet) | Dynamic query filter via reflection loop | EF Core 10, SQL Server, Hangfire | Production-grade, background jobs, caching, `IgnoreQueryFilters`, cross-tenant scopes |
| 3 | [bibekpandey0521/MultiTenant](https://github.com/bibekpandey0521/MultiTenant) | Finbuckle `.IsMultiTenant()` + Identity | EF Core 10, Finbuckle 10.1.3, SQL Server | Finbuckle (not fullstackhero), shadow `TenantId`, `MultiTenantIdentityDbContext` |

---

## Project 1 — EntityFrameworkCoreMultiTenancy

**Config:** `.editorconfig` with `tenantlint.tenant_property_names = Tenant`

### Architecture

- `Database : DbContext` with `HasQueryFilter(a => a.Tenant == tenant)` where
  `tenant` is a `private readonly string` instance field set from `ITenantGetter`
  in the constructor.
- One entity (`Animal`) with one `DbSet`.
- Tenant set via HTTP query parameter in `MultiTenantServiceMiddleware`.
- No pooling (`AddDbContext`), no raw SQL, no Dapper, no `IgnoreQueryFilters`.

### Results: 0 TL diagnostics

All true negatives. The filter lambda references `this.tenant` (an instance field),
which EF Core correctly parameterizes per query. No unsafe patterns.

### Findings table

| Rule | Severity | Count | TP | Intentional | FP |
|------|----------|-------|----|-------------|-----|
| — | — | 0 | 0 | 0 | 0 |

### Misses (not reported, should have been)

| Rule | Location | Description |
|------|----------|-------------|
| TL004 | `MultiTenantServiceMiddleware.cs:14` | `context.Request.Query.TryGetValue("tenant", ...)` reads tenant from query parameter. TL004 covers `Request.Headers.TryGetValue()` and `Request.Query["key"]` indexer but not `Request.Query.TryGetValue()`. |

**Root cause of TL004 miss:** The CLAUDE.md spec lists `Request.Headers.TryGetValue()`
but does not list `Request.Query.TryGetValue()`. The implementation follows the spec.
**Proposed fix:** Extend TL004 to detect `TryGetValue` on `IQueryCollection` (the
type of `Request.Query`), not only on `IHeaderDictionary`.

---

## Project 2 — saas-starter-dotnet

**Config:** `.editorconfig` in `src/` with `tenantlint.global_entities = Tenant, AuditLog`
and `tenantlint.tenant_marker_interfaces = ITenantScoped`.

### Architecture

- `AppDbContext` applies query filters dynamically: `OnModelCreating` loops over
  `modelBuilder.Model.GetEntityTypes()` and calls `ApplyFiltersFor<TEntity>()` via
  `MethodInfo.MakeGenericMethod().Invoke()`. The helper applies a `HasQueryFilter`
  expression referencing `CurrentTenantId` (a property on the DbContext that reads
  from scoped `ITenantContext` — re-evaluated per query, not baked in).
- Entities: `User`, `Role`, `RefreshToken` implement `ITenantScoped` (have `TenantId`).
  `Tenant` and `AuditLog` are global.
- `IgnoreQueryFilters()` used in:
  - `PreAuthenticationLookups.cs` — pre-auth lookups (before tenant is resolved)
  - `MaintenanceJobs.cs` — platform maintenance (token cleanup, audit log retention)
  - `Repository<T>.QueryAcrossTenants()` — generic helper for platform operations
- Tenant-prefixed caching via `DistributedCacheService` (keys include `t:{tenantId}:`).
- Tenant resolved from JWT claim > HTTP header > subdomain (middleware).

### Results: 5 unique TL diagnostics

| # | Rule | Severity | Location | Classification | Reason |
|---|------|----------|----------|---------------|--------|
| 1 | TL005 | Warning | `AppDbContext.cs:31` (`User`) | **FP** | Filtered via reflection-based dynamic loop; TenantLint can't see through `MakeGenericMethod().Invoke()` |
| 2 | TL005 | Warning | `AppDbContext.cs:33` (`Role`) | **FP** | Same as above |
| 3 | TL005 | Warning | `AppDbContext.cs:39` (`RefreshToken`) | **FP** | Same as above |
| 4 | TL001 | Warning | `MaintenanceJobs.cs:28` (`RefreshToken`) | **TP (intentional)** | Platform maintenance job deletes expired tokens globally; should use `[CrossTenant("reason")]` |
| 5 | TL001 | Warning | `PreAuthenticationLookups.cs:52` (`User`) | **TP (intentional)** | Pre-auth security stamp check runs before tenant is known; constrained by signed-token userId |

### Findings by rule

| Rule | Severity | Count | TP | Intentional | FP |
|------|----------|-------|----|-------------|-----|
| TL001 | Warning | 2 | 2 | 2 | 0 |
| TL005 | Warning | 3 | 0 | 0 | 3 |
| **Total** | | **5** | **2** | **2** | **3** |

### False positive analysis

**TL005 x3 (User, Role, RefreshToken):** The `OnModelCreating` method loops over
`modelBuilder.Model.GetEntityTypes()` and applies filters via reflection
(`MakeGenericMethod().Invoke(this, [modelBuilder])`). TenantLint follows one level
of direct method calls from `OnModelCreating`, but cannot follow reflection-based
invocations. These entities are genuinely filtered at runtime.

**Root cause:** Dynamic filter detection requires seeing both `GetEntityTypes()` AND
`HasQueryFilter`/`SetQueryFilter` in the followed method chain. Here, `HasQueryFilter`
is inside a method invoked via reflection — invisible to the analyzer.

**Proposed fix:** Detect `GetEntityTypes()` loop in `OnModelCreating` and set
`FilterState.Unknown` even when `HasQueryFilter` isn't visible in the followed chain.
The presence of `GetEntityTypes()` strongly implies dynamic filter application. This
would produce TL105 (Info) + TL900 (Info) instead of TL005 (Warning), matching the
existing spec in CLAUDE.md section 3.5.

### True positive analysis

**TL001 on MaintenanceJobs.cs:28:** `context.RefreshTokens.IgnoreQueryFilters()`
in `RefreshTokenCleanupJob.RunAsync()`. This is a Hangfire background job that
deletes expired tokens across all tenants. The code is safe (constrained by
`ExpiresAt < cutoff`, projects to delete count), but it should be annotated with
`[CrossTenant("Platform maintenance: cleanup expired tokens across all tenants")]`.

**TL001 on PreAuthenticationLookups.cs:52:** `context.Users.IgnoreQueryFilters()`
in `SecurityStampReader.GetCurrentStampAsync()`. This runs during JWT validation
before the tenant is resolved. The query is constrained by a signed-token userId
and projects to a minimal value (security stamp). Should be annotated with
`[CrossTenant("Pre-auth: validates security stamp before tenant is resolved")]`.

### Correct non-reports

- `context.Tenants.IgnoreQueryFilters()` (PreAuthenticationLookups.cs:37) — `Tenant`
  is in `global_entities`. Correct.
- `context.AuditLogs.IgnoreQueryFilters()` (MaintenanceJobs.cs:61) — `AuditLog`
  is in `global_entities`. Correct.
- `context.Request.Headers[_options.HeaderName]` (TenantResolutionMiddleware.cs:63) —
  header key is a runtime value from configuration, not a constant. TL004 correctly
  does not report (can't evaluate non-constant keys).

### Misses (not reported, should have been)

| Rule | Location | Description |
|------|----------|-------------|
| TL105/TL900 | `AppDbContext.cs:57` | `GetEntityTypes()` loop should trigger dynamic filter detection → TL105 (Info) instead of TL005 (Warning), plus TL900 (Info) once per context |
| TL001 | `Repository.cs:21` | `QueryAcrossTenants()` calls `.IgnoreQueryFilters()` on generic `TEntity` — analyzer can't determine if entity is tenant-scoped in a generic context |

**TL001 miss in Repository.cs:** The generic `Repository<TEntity>` calls
`.IgnoreQueryFilters()` on any `TEntity`. When instantiated with `User` or `Role`,
this removes tenant isolation. The analyzer can't resolve the generic type parameter
at the definition site. This is a known limitation of static analysis with generic
repositories. No fix planned — users should annotate the concrete call sites or the
`QueryAcrossTenants` method.

---

## Project 3 — MultiTenant (Finbuckle school management)

**Config:** No custom `.editorconfig` (default `TenantId` property name).

### Architecture

- `ApplicationDbContext : BaseDbContext : MultiTenantIdentityDbContext<...>` (Finbuckle).
- One domain entity (`School`) plus 7 ASP.NET Identity entities, all configured
  with `.IsMultiTenant()` in `IEntityTypeConfiguration<T>` implementations.
- Finbuckle's `MultiTenantIdentityDbContext` applies shadow `TenantId` and query
  filters automatically via `base.OnModelCreating()`.
- Tenant resolved via header strategy (`"tenant"` header) and claim strategy.
- No controllers yet (early development stage).
- No `IgnoreQueryFilters`, no raw SQL, no Dapper, no caching, no background services.

### Results: 0 TL diagnostics

All true negatives. Finbuckle framework recognition correctly identifies all
`.IsMultiTenant()` entities as `FilterState.Filtered` with `TenantPropertyName = "TenantId"`.
`BaseDbContext` derives from `MultiTenantIdentityDbContext` (recognized Finbuckle base).

### Findings table

| Rule | Severity | Count | TP | Intentional | FP |
|------|----------|-------|----|-------------|-----|
| — | — | 0 | 0 | 0 | 0 |

### Misses

None for current rules. The project is in early development with minimal code.

---

## Summary across all projects

### By classification

| | TP (real issue) | TP (intentional cross-tenant) | FP | Total |
|---|---|---|---|---|
| Project 1 | 0 | 0 | 0 | 0 |
| Project 2 | 0 | 2 | 3 | 5 |
| Project 3 | 0 | 0 | 0 | 0 |
| **Total** | **0** | **2** | **3** | **5** |

### By rule

| Rule | Severity | TP | Intentional | FP | Missed | Notes |
|------|----------|----|-------------|-----|--------|-------|
| TL001 | Warning | 0 | 2 | 0 | 1 | Generic repository `.IgnoreQueryFilters()` |
| TL005 | Warning | 0 | 0 | 3 | 0 | Should be TL105 (Info) via dynamic filter detection |
| TL004 | Warning | — | — | — | 1 | `Request.Query.TryGetValue()` not covered |
| TL105 | Info | — | — | — | 3 | Should replace TL005 for dynamic filter contexts |
| TL900 | Info | — | — | — | 1 | Should fire for `GetEntityTypes()` loop |

### FP rate

- **Warning-severity FP:** 3 out of 5 total warnings = **60%** (all from one pattern)
- **If TL105/dynamic filter detection is fixed:** 0 FP at Warning severity, 3 TL105 at Info

### Key findings

1. **Finbuckle recognition works well.** Project 3 got a clean bill of health with
   zero false positives — `.IsMultiTenant()` and `MultiTenantIdentityDbContext` are
   correctly recognized.

2. **Hand-rolled HasQueryFilter works well.** Project 1 correctly identified the
   filter as safe (instance field rooted in `this`).

3. **Reflection-based dynamic filters are the gap.** The only FP source is
   `MakeGenericMethod().Invoke()` for applying filters — a pattern used by
   production codebases that want DRY filter application. The fix is to relax
   dynamic filter detection: `GetEntityTypes()` loop alone (without visible
   `HasQueryFilter`) should set `FilterState.Unknown` → TL105 (Info).

4. **TL001 on intentional cross-tenant access is the right behavior.** Both TL001
   findings in Project 2 are exactly the kind of code that should have
   `[CrossTenant("reason")]` — the developer explicitly chose to bypass isolation
   for valid reasons, and the annotation documents that choice.

5. **Generic repositories hide `IgnoreQueryFilters` from the analyzer.** The
   `Repository<TEntity>.QueryAcrossTenants()` pattern calls `.IgnoreQueryFilters()`
   at the generic level. This is a known static analysis limitation with no
   practical fix (would require whole-program data flow analysis).

6. **`Request.Query.TryGetValue()` is not covered by TL004.** The spec covers
   `Request.Headers.TryGetValue()` and `Request.Query[key]` indexer, but not
   `Request.Query.TryGetValue()`.

## Proposed fixes (do not implement in this round)

| Priority | Issue | Fix |
|----------|-------|-----|
| P0 | TL005 fires as Warning for reflection-based dynamic filters | Detect `GetEntityTypes()` loop in OnModelCreating and set `FilterState.Unknown` even without visible `HasQueryFilter` in the followed chain → TL105 (Info) + TL900 (Info) |
| P1 | TL004 misses `Request.Query.TryGetValue()` | Extend TL004 to detect `TryGetValue` on `IQueryCollection` |

## Security disclosure

No third-party vulnerabilities were found during this validation. All TL001
findings represent intentional, well-documented cross-tenant operations with
appropriate access constraints. No disclosure action required.

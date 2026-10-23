# OSS Real-World Validation — October 2026

TenantLint 0.1.0-preview.3-local tested against three open-source multi-tenant .NET
projects representing different isolation strategies: hand-rolled dynamic filters,
Finbuckle.MultiTenant, and ABP Framework.

## Projects tested

| Project | Multi-tenancy approach | EF Core | Entities scanned |
|---------|----------------------|---------|-----------------|
| [PlatformPlatform](https://github.com/nicdev/PlatformPlatform) | Hand-rolled dynamic named filters via helper method | 10 | ~15 tenant-scoped |
| [fullstackhero/dotnet-microservices-boilerplate](https://github.com/nicdev/fullstackhero-dotnet-microservices-boilerplate) | Finbuckle `IsMultiTenant()` + explicit `TenantId` in some modules | 8 | ~30 tenant-scoped |
| [ABP BookStore samples](https://github.com/nicdev/abp-samples) | ABP `IMultiTenant` interface, dynamic filters in framework base class | 8 | ~20 tenant-scoped |

## Aggregate results

| Rule | PlatformPlatform | fullstackhero | ABP BookStore | Total |
|------|-----------------|---------------|---------------|-------|
| TL001 | 0 | 2 | 0 | **2** |
| TL002 | 0 | 0 | 0 | **0** |
| TL003 | 0 | 0 | 0 | **0** |
| TL005 | 0 | 9 | 7 | **16** |
| TL900 | 0 | 0 | 0 | **0** |
| **Total** | **0** | **11** | **7** | **18** |

## Classification

| Category | Count | Percentage |
|----------|-------|-----------|
| True positive | 8 | 44% |
| False positive | 10 | 56% |
| Missed (false negative) | ~18 | — |

---

## Per-project findings

### 1. PlatformPlatform (0 diagnostics — 3 detection gaps)

**Result: zero diagnostics reported.** This is incorrect — the project has at least 3
parameterless `IgnoreQueryFilters()` calls on tenant-scoped entities and ~15 entities
with tenant filters that should have been discoverable.

#### Gap 1: No `DbSet<T>` properties

TenantLint discovers tenant entities via `DbSet<T>` properties on DbContext subclasses.
PlatformPlatform uses a repository pattern with `context.Set<T>()` and never declares
`DbSet<T>` properties:

```csharp
// SubscriptionRepository.cs — uses Set<T>() instead of DbSet<T> property
.Where(s => !accountDbContext.Set<BillingEvent>()
    .IgnoreQueryFilters(tenantFilterName)
    .Any(e => e.SubscriptionId == s.Id))
```

**Impact:** All entity discovery fails. TL001, TL002, TL003, TL005 produce nothing.

#### Gap 2: Dynamic filters in helper method not followed

Filters are applied in a private helper method called from `OnModelCreating`, not
inline:

```csharp
// SharedKernelDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(...);
    ApplyNamedQueryFilters(modelBuilder);  // <-- not followed
}

private void ApplyNamedQueryFilters(ModelBuilder modelBuilder)
{
    var entityTypes = modelBuilder.Model.GetEntityTypes()
        .Where(t => !t.IsOwned()).ToList();

    foreach (var entityType in entityTypes)
    {
        // ... dynamically applies HasQueryFilter(QueryFilterNames.Tenant, lambda)
        // and HasQueryFilter(QueryFilterNames.SoftDelete, lambda)
    }
}
```

TenantLint only inspects the direct body of `OnModelCreating` for `HasQueryFilter`
calls. The helper method indirection means no filters are detected.

#### Gap 3: Cross-assembly DbContext inheritance

The `SharedKernelDbContext` (where filters are defined) lives in a shared-kernel
project. Derived DbContexts in other projects inherit it. TenantLint only scans
`DbContext` subtypes whose source is in the current compilation — referenced assembly
base classes are invisible.

#### Missed findings

| Location | What was missed | Expected rule |
|----------|----------------|---------------|
| `SessionRepository.cs:69` | `DbSet.IgnoreQueryFilters()` on `Session` (tenant-scoped) | TL001 |
| `SessionRepository.cs:113` | `DbSet.IgnoreQueryFilters()` + `ExecuteUpdateAsync` chain | TL001 |
| `SessionRepository.cs:136` | `DbSet.IgnoreQueryFilters()` on `Session` | TL001 |

The project also uses EF Core 10 selective `IgnoreQueryFilters([QueryFilterNames.Tenant])`
in 3 additional locations — these would correctly NOT be flagged by TL001 if entities
were discovered, demonstrating the named-filter path works as designed.

---

### 2. fullstackhero / Finbuckle (11 diagnostics: 2 TL001 + 9 TL005)

#### TL001 findings (2 — both true positive)

| # | File | Entity | Verdict |
|---|------|--------|---------|
| 1 | `GetAuditsQueryHandler.cs:164` | `AuditRecord` | **TP** — parameterless `IgnoreQueryFilters()` on entity with explicit `TenantId` property and tenant query filter |
| 2 | `GetAuditsQueryHandler.cs:175` | `AuditRecord` | **TP** — same pattern, different query |

Representative snippet:
```csharp
// GetAuditsQueryHandler.cs — cross-tenant audit query without [CrossTenant]
return _dbContext.AuditRecords
    .AsNoTracking()
    .IgnoreQueryFilters()   // ← TL001: removes tenant filter
    .Where(a => a.TenantId == requested);
```

#### TL005 findings (9)

| # | Entity | DbContext | Verdict | Reason |
|---|--------|-----------|---------|--------|
| 1 | `Subscription` | `BillingDbContext` | **TP** | Has `TenantId`, no query filter; filtering done ad-hoc in queries |
| 2 | `Invoice` | `BillingDbContext` | **TP** | Same — explicit `TenantId`, no model-level filter |
| 3 | `InvoiceLineItem` | `BillingDbContext` | **TP** | Same pattern |
| 4 | `BillingCustomer` | `BillingDbContext` | **TP** | Same pattern |
| 5 | `UsageRecord` | `BillingDbContext` | **TP** | Same pattern |
| 6 | `PaymentMethod` | `BillingDbContext` | **TP** | Same pattern |
| 7 | `AuditRecord` | `AuditingDbContext` | **FP** | Finbuckle `IsMultiTenant()` applies shadow `TenantId` filter — invisible to static analysis |
| 8 | `OutboxMessage` | `EventingDbContext` | **FP** | Same — Finbuckle shadow filter |
| 9 | `InboxMessage` | `EventingDbContext` | **FP** | Same — Finbuckle shadow filter |

**True positives (6):** The Billing module intentionally avoids Finbuckle and uses
explicit `TenantId` properties with ad-hoc WHERE clauses. These entities genuinely
lack model-level tenant filters — a real isolation risk:

```csharp
// Subscription.cs — has TenantId but BillingDbContext has no HasQueryFilter for it
public sealed class Subscription : BaseEntity<Guid>
{
    public string TenantId { get; private set; } = default!;
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    // ...
}
```

**False positives (3):** Finbuckle's `IsMultiTenant()` (called via
`builder.ApplyTenantIsolationByDefault()`) adds a shadow `TenantId` property and a
query filter at the EF Core model level, but this happens through Finbuckle's
`IModelCustomizer` at runtime — not through a `HasQueryFilter` call visible in source.

#### Missed findings — Finbuckle shadow `TenantId`

~15+ parameterless `IgnoreQueryFilters()` calls exist on entities whose tenant
isolation comes from Finbuckle's shadow `TenantId`. Because TenantLint does not
recognize these entities as tenant-scoped, TL001 does not fire.

Examples:
- `IdentityDbContext` entities (users, roles, claims) — all use `IsMultiTenant()`
- `TodoDbContext` entities — same pattern
- Various module entities using `ApplyTenantIsolationByDefault()`

The selective `IgnoreQueryFilters(["SoftDelete"])` calls in this project were
correctly NOT flagged — no false positives from the named-filter path.

---

### 3. ABP BookStore (7 TL005 — all false positive)

All 7 diagnostics are false positives. ABP applies tenant filters dynamically in
compiled library code that TenantLint cannot inspect.

| # | Entity | Source | Verdict |
|---|--------|--------|---------|
| 1–7 | Various ABP Identity/TenantManagement entities | `IMultiTenant` interface | **FP** — ABP `AbpDbContext.OnModelCreating` applies dynamic filters |

**Mechanism:** ABP entities implement `IMultiTenant`, which defines `Guid? TenantId`.
The `AbpDbContext` base class dynamically calls `HasQueryFilter` for all `IMultiTenant`
entities inside its compiled `OnModelCreating` override:

```csharp
// User code — looks unfiltered, but base class handles it
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);  // ← ABP applies IMultiTenant filters here
    builder.ConfigureBookStore();
}

// ABP entity — IMultiTenant means "ABP handles the filter"
public class Organization : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }
}
```

**TL900 did not fire.** Dynamic filter detection (`TL900`) only works on source code
visible in the current compilation. The ABP `base.OnModelCreating()` call delegates to
compiled IL in the `Volo.Abp.EntityFrameworkCore` NuGet package — TenantLint cannot
inspect this.

**Correctly NOT flagged:** `Book` and `Author` entities (no `TenantId` property) were
correctly excluded from TL005 — convention detection works when the entity genuinely
has no tenant property.

---

## Framework detection analysis

### What works today

| Pattern | Detection | Rules |
|---------|-----------|-------|
| Explicit `HasQueryFilter(e => e.TenantId == ...)` in OnModelCreating body | Yes | TL001, TL005 |
| `DbSet<T>` property with `TenantId` but no filter | Yes | TL005 |
| Named `HasQueryFilter("Name", ...)` in OnModelCreating body | Yes | TL001, TL005 |
| Selective `IgnoreQueryFilters(["SoftDelete"])` | Correctly ignored | TL001 |
| `[CrossTenant]` suppression | Yes | All |
| Entities without tenant property | Correctly ignored | All |
| `global_entities` config | Yes | TL005 |

### What does not work

| Pattern | Gap | Affected frameworks |
|---------|-----|-------------------|
| Entities accessed via `Set<T>()` without `DbSet<T>` property | Not discovered as tenant entities | Repository patterns (PlatformPlatform, Clean Architecture) |
| `HasQueryFilter` in private helper methods called from OnModelCreating | Filters not detected | Hand-rolled dynamic filter patterns |
| `HasQueryFilter` in base DbContext from referenced assembly | Base class invisible | ABP, Finbuckle, any framework with shared base DbContext |
| Finbuckle `IsMultiTenant()` shadow `TenantId` | Not recognized as tenant filter | Finbuckle.MultiTenant |
| ABP `IMultiTenant` interface dynamic filters | Not recognized as tenant filter | ABP Framework |
| `IEntityTypeConfiguration<T>` in referenced assemblies | Not scanned | Any framework shipping configurations as NuGet |

---

## Proposed fixes (prioritized)

### P0 — High impact, required for v0.1.0 credibility

#### 1. Discover entities from `modelBuilder.Entity<T>()` and `Set<T>()` calls

**Problem:** Entity discovery relies solely on `DbSet<T>` properties. Many projects use
`context.Set<T>()` (repository pattern) or only configure entities via
`modelBuilder.Entity<T>()` without exposing them as DbSet properties.

**Proposed fix:** In `TenantModel` discovery, additionally scan:
- `ModelBuilder.Entity<T>()` calls in `OnModelCreating` to discover entity types
- `DbContext.Set<T>()` calls across the compilation to discover entity types

Register a `SyntaxNodeAction` for `GenericNameSyntax` or use `IInvocationOperation`
to find `Set<T>()` calls, resolve `T`, check for tenant property.

**Expected impact:** PlatformPlatform goes from 0 to ~15 discovered entities.

#### 2. Follow one level of method calls from OnModelCreating

**Problem:** Projects that extract filter logic into helper methods
(`ApplyNamedQueryFilters`, `ConfigureTenantFilters`, etc.) are invisible.

**Proposed fix:** When scanning `OnModelCreating` / `IEntityTypeConfiguration.Configure`,
if a `modelBuilder` (or `builder`) argument is passed to a private/internal method in
the same type, follow into that method body and continue scanning for `HasQueryFilter`
calls. Limit to one level of indirection to avoid performance issues.

**Expected impact:** PlatformPlatform dynamic filters detected, `FilterState.Unknown`
replaced with `FilterState.Filtered` for those entities.

### P1 — Framework-specific detection (v0.2 stretch)

#### 3. Recognize Finbuckle `IsMultiTenant()` as tenant filter

**Problem:** Finbuckle's `IsMultiTenant()` fluent call on `EntityTypeBuilder` adds a
shadow `TenantId` property and a query filter at the model customizer level. TenantLint
does not recognize this.

**Proposed fix:** In `TenantModel` discovery, detect
`EntityTypeBuilder.IsMultiTenant()` calls (symbol:
`Finbuckle.MultiTenant.EntityTypeBuilderExtensions.IsMultiTenant`). Mark the entity as
tenant-scoped with `FilterState.Filtered`. Also detect the convention method
`ApplyTenantIsolationByDefault()` as a blanket "all non-global entities are filtered"
signal.

**Expected impact:** 3 FP eliminated in fullstackhero; ~15 missed `IgnoreQueryFilters()`
calls now detected as TL001.

#### 4. Recognize ABP `IMultiTenant` as tenant scope indicator

**Problem:** ABP entities implement `IMultiTenant` and receive dynamic filters from
`AbpDbContext`. TenantLint sees `TenantId` (convention match) but not the filter.

**Proposed fix:** Check if an entity type implements an interface named `IMultiTenant`
(any namespace — ABP, custom). If so, and `base.OnModelCreating(builder)` is called,
assume the framework applies tenant filters → mark as `FilterState.Filtered`.

**Expected impact:** All 7 ABP FP eliminated.

### P2 — Nice to have (v0.2+)

#### 5. Cross-assembly base DbContext scanning

**Problem:** Base DbContext classes in referenced assemblies (NuGet or project
references) are invisible to Roslyn syntax analysis.

**Proposed fix:** Use `INamedTypeSymbol.BaseType` chain to find the immediate DbContext
subclass in the current compilation. If `OnModelCreating` is not overridden in source
but the base type is from a known framework (Finbuckle, ABP), apply framework-specific
heuristics (see P1 fixes above). Full IL scanning is not feasible for a Roslyn
analyzer.

#### 6. Scan `IEntityTypeConfiguration<T>` from referenced assemblies

Not feasible in a Roslyn analyzer (no IL access). Document as a known limitation. Users
should configure `tenantlint.global_entities` for framework entities they know are
filtered externally.

---

## Round 1 conclusions

1. **TenantLint catches real bugs.** The 6 true-positive TL005 findings in
   fullstackhero's Billing module are genuine: entities with `TenantId` and no
   model-level filter, relying on ad-hoc WHERE clauses. The 2 TL001 findings are
   also real cross-tenant queries without `[CrossTenant]` annotation.

2. **False positive rate is too high for framework users.** 10/18 findings (56%) are
   false positives, all from framework-managed filters invisible to static analysis.
   ABP users would see 100% FP; Finbuckle users ~27% FP. This needs addressing
   before v0.1.0.

3. **Entity discovery via `DbSet<T>` only is too narrow.** The PlatformPlatform
   result (0 diagnostics on a project with real tenant entities) shows this is the
   highest-priority gap. `Set<T>()` and `modelBuilder.Entity<T>()` discovery would
   fix it.

4. **Following helper methods from OnModelCreating is critical.** Real-world projects
   extract filter logic into helper methods. One level of method-call following would
   cover the most common pattern.

5. **Framework heuristics are feasible and bounded.** Detecting `IsMultiTenant()` and
   `IMultiTenant` interface does not require IL scanning — it only requires symbol
   resolution, which Roslyn provides for referenced assemblies. These two heuristics
   alone would eliminate all 10 false positives in this validation.

---

## Round 2 — After P0/P1 fixes

Re-validated the same three projects after implementing:

- **P0a:** Entity discovery from `Set<T>()`, `modelBuilder.Entity<T>()`, and
  `IEntityTypeConfiguration<T>` (commit `4ddb1c2`)
- **P0b:** Follow one level of method calls from `OnModelCreating` (commit `2435c01`)
- **P1a:** Finbuckle `IsMultiTenant()`, `[MultiTenant]`, `MultiTenantDbContext` base
  recognition (commit `743dcf6`)
- **P1b:** ABP `IMultiTenant` + `AbpDbContext<T>` recognition (commit `9d5c49e`)
- **Safety rule:** Unknown external base class → `FilterState.Unknown` / TL105 Info
  (commit `4007568`)

### Round 2 aggregate results

| Rule | PlatformPlatform | fullstackhero | ABP BookStore | Total |
|------|-----------------|---------------|---------------|-------|
| TL001 | 3 | 2 | 0 | **5** |
| TL002 | 0 | 0 | 0 | **0** |
| TL003 | 0 | 0 | 0 | **0** |
| TL005 | 0 | 6 | 0 | **6** |
| TL900 | 0 | 0 | 0 | **0** |
| **Total** | **3** | **8** | **0** | **11** |

### Round 2 classification

| Category | Count | Percentage |
|----------|-------|-----------|
| True positive | 11 | **100%** |
| False positive | 0 | **0%** |
| Missed (false negative) | ~12 | — |

### Round 1 → Round 2 delta

| Project | Round 1 | Round 2 | TP change | FP change |
|---------|---------|---------|-----------|-----------|
| PlatformPlatform | 0 (0 TP, 0 FP) | 3 (3 TP, 0 FP) | +3 | — |
| fullstackhero | 11 (8 TP, 3 FP) | 8 (8 TP, 0 FP) | — | **-3** |
| ABP BookStore | 7 (0 TP, 7 FP) | 0 (0 TP, 0 FP) | — | **-7** |
| **Total** | **18 (8 TP, 10 FP)** | **11 (11 TP, 0 FP)** | **+3** | **-10** |

### Per-project Round 2 findings

#### 1. PlatformPlatform (3 TL001 — all true positive)

All three `IgnoreQueryFilters()` calls on `Session` are now detected. Entity
discovery via `Set<T>()` (P0a) and method-call following (P0b) resolved the two
gaps that caused zero diagnostics in Round 1.

| # | File | Entity | Verdict |
|---|------|--------|---------|
| 1 | `SessionRepository.cs:69` | `Session` | **TP** — parameterless `IgnoreQueryFilters()` removes tenant filter |
| 2 | `SessionRepository.cs:113` | `Session` | **TP** — `IgnoreQueryFilters()` + `ExecuteUpdateAsync` chain |
| 3 | `SessionRepository.cs:136` | `Session` | **TP** — parameterless `IgnoreQueryFilters()` on `Session` |

The dynamic filter in `ApplyNamedQueryFilters` is correctly detected as a dynamic
filter (method following finds the `GetEntityTypes()` + `HasQueryFilter` loop),
so convention-tenant entities in this context receive `FilterState.Unknown` and
TL005 is suppressed to Info. The `IgnoreQueryFilters()` calls, however, are on
entities touched via `Set<T>()` which are now discovered — TL001 fires correctly.

**Remaining misses:** The selective `IgnoreQueryFilters([QueryFilterNames.Tenant])`
calls (3 locations) are correctly NOT flagged — the named-filter path works as
designed.

#### 2. fullstackhero / Finbuckle (8 diagnostics — all true positive)

**Round 1 → Round 2 changes:**

- 3 **false positives eliminated:** `AuditRecord` (AuditingDbContext), `OutboxMessage`
  and `InboxMessage` (EventingDbContext) were previously TL005 false positives because
  Finbuckle's `IsMultiTenant()` shadow filter was invisible. P1a Finbuckle recognition
  now marks these entities as `FilterState.Filtered` — TL005 no longer fires.

- TL001 count unchanged at 2 (both on `AuditRecord` in cross-tenant audit handlers).

- TL005 count reduced from 9 to 6: the 3 Finbuckle shadow-filter entities removed,
  leaving only the 6 Billing entities that genuinely lack model-level filters.

| # | Rule | File | Entity | Verdict |
|---|------|------|--------|---------|
| 1 | TL001 | `GetAuditSummaryQueryHandler.cs:112` | `AuditRecord` | **TP** — parameterless `IgnoreQueryFilters()` |
| 2 | TL001 | `GetAuditsQueryHandler.cs:165` | `AuditRecord` | **TP** — parameterless `IgnoreQueryFilters()` |
| 3 | TL005 | `BillingDbContext.cs:19` | `Subscription` | **TP** — has `TenantId`, no query filter |
| 4 | TL005 | `BillingDbContext.cs:20` | `Invoice` | **TP** — has `TenantId`, no query filter |
| 5 | TL005 | `BillingDbContext.cs:22` | `UsageSnapshot` | **TP** — has `TenantId`, no query filter |
| 6 | TL005 | `BillingDbContext.cs:23` | `Wallet` | **TP** — has `TenantId`, no query filter |
| 7 | TL005 | `BillingDbContext.cs:24` | `WalletTransaction` | **TP** — has `TenantId`, no query filter |
| 8 | TL005 | `BillingDbContext.cs:25` | `TopupRequest` | **TP** — has `TenantId`, no query filter |

**Note:** Round 1 reported 9 TL005 including `InvoiceLineItem`, `BillingCustomer`, and
`PaymentMethod`. Round 2 reports 6 TL005 on `Subscription`, `Invoice`, `UsageSnapshot`,
`Wallet`, `WalletTransaction`, and `TopupRequest`. The difference is because the
fullstackhero codebase evolved between rounds (entities were renamed/added/removed in the
starter-kit repo); the 3 eliminated entries are the Finbuckle FPs.

#### 3. ABP BookStore (0 diagnostics — correct)

All 7 false positives from Round 1 are eliminated. P1b ABP recognition detects that the
DbContext derives from `AbpDbContext<T>` and marks it as a filtered base context. Entities
with `TenantId` from `IMultiTenant` in an ABP context are correctly marked
`FilterState.Filtered`.

`Book` and `Author` (no `TenantId`) remain correctly excluded — no false negatives introduced.

### Round 2 conclusions

1. **Zero false positives.** All 11 diagnostics across three projects are true positives.
   The 56% false-positive rate from Round 1 is eliminated.

2. **PlatformPlatform now works.** Entity discovery via `Set<T>()` and method-call
   following resolved the complete detection failure. 3 real `IgnoreQueryFilters()` calls
   are now caught.

3. **Finbuckle recognition works.** Shadow `TenantId` entities managed by
   `IsMultiTenant()` are correctly classified as `FilterState.Filtered`. TL005 no longer
   fires for them, while TL001 correctly fires when `IgnoreQueryFilters()` is used on
   entities with explicit `TenantId` properties.

4. **ABP recognition works.** `AbpDbContext<T>` base detection + `IMultiTenant` interface
   recognition eliminates all ABP false positives without introducing false negatives.

5. **Remaining misses (~12)** are mostly Finbuckle shadow-property entities where
   `IgnoreQueryFilters()` is used in modules we don't build (identity, todo, etc.) — these
   would require building each module project individually with the analyzer. This is
   expected behavior, not a bug in TenantLint.

---

## Billing entity query analysis (fullstackhero)

Round 2 also surfaced potential issues in a third-party project. They are being
reported privately to the maintainers and will not be published here until fixed.

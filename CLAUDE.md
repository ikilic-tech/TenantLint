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
├─ global.json                  # pin SDK (latest installed .NET 10 SDK, rollForward latestFeature)
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
1. **Explicit filters.** Find all types deriving from `Microsoft.EntityFrameworkCore.DbContext`. In `OnModelCreating` and in every `IEntityTypeConfiguration<T>.Configure`, find `HasQueryFilter(...)` calls (both the classic overload and the EF Core 10 named overload `HasQueryFilter("Name", expr)`). If the lambda body references a member whose name is in the tenant property list, mark the entity tenant-scoped **and** record the filter name (if any).
2. **Convention.** An entity exposed as `DbSet<T>` on a DbContext that has a property named in the tenant property list is tenant-scoped (even if unfiltered — that is exactly what TL005 reports).
3. **Dynamic filters.** If `OnModelCreating` loops over `modelBuilder.Model.GetEntityTypes()` and calls `HasQueryFilter` / `SetQueryFilter` dynamically, we cannot see which entities are covered. Treat every convention-tenant entity as filtered and suppress TL005 for that context (log an Info diagnostic TL900 once: "dynamic query filter detected, TL005 disabled for <Context>").
4. **Table names** for raw-SQL rules: entity name, DbSet property name, `ToTable("x")` / `[Table("x")]`, plus snake_case and lowercase variants. Store them case-insensitively.

### Configuration (`.editorconfig`, read via `AnalyzerConfigOptionsProvider`)
```ini
[*.cs]
tenantlint.tenant_property_names = TenantId, OrganizationId, WorkspaceId   # default: TenantId
tenantlint.tenant_column_names   = tenant_id, organization_id              # default: derived from property names
tenantlint.global_entities       = Tenant, AuditLog, Country               # never tenant-scoped
tenantlint.tenant_filter_name    = Tenant                                  # EF Core 10 named filter that carries isolation
tenantlint.cross_tenant_attribute = CrossTenant                            # attribute name that marks intentional cross-tenant code
```
Users can also change severity per rule with the standard `dotnet_diagnostic.TL001.severity = error`.

### Intentional cross-tenant access
Any method or class decorated with an attribute whose **simple name** is `CrossTenant` / `CrossTenantAttribute` (any namespace; we do not ship a runtime package) is exempt from TL001–TL003. The attribute should take a reason string; if it is missing, report TL901 (Info): "CrossTenant attribute should state a reason". The README shows users a 3-line attribute to copy into their codebase.

## 4. Rules

Diagnostic ID prefix `TL`, category `TenantIsolation`, `helpLinkUri = https://github.com/<owner>/TenantLint/blob/main/docs/rules/TLxxx.md`. Message format (agent-friendly):

> `TL001: '<Customers>' query removes the tenant filter via IgnoreQueryFilters(). Use IgnoreQueryFilters(["<SoftDelete>"]) to lift only the filter you need, or mark the method [CrossTenant("reason")].`

### v0.1 (ship first)

| ID | Default severity | Detects | Code fix |
|---|---|---|---|
| **TL001** | Warning | Parameterless `IgnoreQueryFilters()` on `IQueryable<T>` where `T` is tenant-scoped. If the project uses EF Core ≥ 10 and named filters exist, the fix lists the non-tenant filter names. On EF Core < 10 there is no partial alternative → message points to `[CrossTenant]`. | EF 10+: rewrite to `IgnoreQueryFilters(["<other filter names>"])`. Always: add `[CrossTenant("TODO: reason")]`. |
| **TL002** | Warning | `Database.SqlQuery<T>`, `SqlQueryRaw<T>`, `ExecuteSql*`, `ExecuteSqlRaw*`, `ExecuteSqlInterpolated*` (+ `Async`) whose SQL text (literal, interpolated string, or constant) mentions a known tenant table name but **not** a tenant column name. Global query filters do not apply to these APIs. Non-constant SQL → do not report (avoid false positives). | None in v0.1 (message shows the expected `WHERE tenant_id = @tenant`). |
| **TL003** | Warning | Dapper `SqlMapper` extension calls (`Query*`, `QueryFirst*`, `QuerySingle*`, `QueryMultiple*`, `Execute*`, `ExecuteScalar*`) with the same tenant-table / tenant-column check as TL002. Detect by symbol (`Dapper.SqlMapper`), not by method name alone. | None in v0.1. |
| **TL004** | Warning | Tenant identity taken from client input in ASP.NET Core: action/endpoint parameters whose name matches the tenant property list and that are bound `[FromHeader]`, `[FromQuery]`, `[FromRoute]`, `[FromForm]`, or implicitly from query/route; properties of `[FromBody]` DTOs with a tenant-property name **when that value flows into a query, `Where`, or entity assignment in the same method**; `Request.Headers["X-Tenant-Id"]`-style reads. | None in v0.1 (message: "read the tenant from the authenticated principal, e.g. User.FindFirstValue("tenant")"). |
| **TL005** | Warning | A tenant-scoped (by convention) entity exposed as `DbSet<T>` whose DbContext defines no query filter for it. Reported on the `DbSet<T>` property. Skipped for `global_entities` and when dynamic filters are detected (TL900). | Insert `modelBuilder.Entity<T>().HasQueryFilter(e => e.<TenantProp> == <tenant accessor>)` stub with a `// TODO` for the accessor. |

### v0.2

| ID | Severity | Detects |
|---|---|---|
| **TL006** | Warning | `IMemoryCache` / `IDistributedCache` / `HybridCache` set/get/`GetOrCreate*` calls whose key is a constant or an interpolation with no tenant-related value, inside a method that also touches tenant-scoped data. |
| **TL007** | Warning | Tenant context stored in a `static` field/property, or a type whose name contains `Tenant` (e.g. `ITenantContext`, `ICurrentTenant`) registered as `AddSingleton`. Also a singleton capturing a scoped tenant service in its constructor. |
| **TL008** | Info | `BackgroundService` / `IHostedService` / Hangfire job methods that resolve or inject a DbContext with tenant-scoped entities without creating a scope / setting the tenant first. Heuristic → Info by default. |
| **TL009** | Warning | PostgreSQL RLS session variable set with `SET app.<x>` / `set_config(..., false)` instead of `SET LOCAL` / `set_config(..., true)` → leaks across pooled connections. |

### Verification tasks (do not skip)
- **FromSql\***: EF Core composes global query filters over composable `FromSql*` queries on a `DbSet<T>`. Before finalizing TL002, write an integration test (SQLite in-memory, two tenants) that confirms this for EF Core 8 and 10. If filters turn out **not** to apply, extend TL002 to cover `FromSql*` and update `docs/rules/TL002.md`.
- **Named filters API**: confirm the exact EF Core 10 signatures (`HasQueryFilter(string, LambdaExpression)`, `IgnoreQueryFilters(IReadOnlyCollection<string>)`) against the real package before coding TL001's fix.

## 5. Engineering rules

- **Performance:** register syntax-node / operation actions narrowly (`OperationKind.Invocation`, `SymbolKind.Property`). Never walk the whole tree per node. Build the `TenantModel` once per compilation. Call `context.EnableConcurrentExecution()` and `ConfigureGeneratedCodeAnalysis(None)`.
- **Use the IOperation API** (`IInvocationOperation`) over raw syntax wherever possible; resolve methods by symbol (`Compilation.GetTypeByMetadataName`) — never by string-matching method names alone.
- **No false positives over coverage.** Every rule ships with at least 3 positive and 5 negative test cases (including look-alikes: a user method named `IgnoreQueryFilters`, a non-tenant entity, a `[CrossTenant]` method, string SQL built at runtime).
- **IDE/agent experience:** diagnostic location is the narrowest meaningful span (the invocation name, the parameter, the DbSet property) so the squiggle and the build output line are precise.
- **No runtime dependency** shipped to users. The package contains analyzers only.
- **Every rule has** a `docs/rules/TLxxx.md` page with: what, why it leaks, bad/good code, how to suppress, false-positive notes.
- **Commits:** Conventional Commits (`feat(TL001): …`, `test: …`, `docs: …`). One rule per PR.
- Public code, comments, docs and messages are **in English**.

## 6. Sample app (`samples/SampleSaaS`)

A small .NET 10 minimal API + EF Core 10 (SQLite) multi-tenant app — `Customer`, `Order`, `Invoice` (tenant-scoped), `Tenant`, `Country` (global). Tenant comes from a JWT claim. It contains **one deliberate violation per rule**, each marked `// TL00x: deliberate`, so `dotnet build samples/SampleSaaS` shows every diagnostic. This is the demo for the README GIF and the launch blog post.

## 7. CI (`.github/workflows/ci.yml`)
- Matrix: ubuntu-latest, windows-latest; .NET 8 + 10 SDKs.
- `dotnet build -warnaserror` (src), `dotnet test`, `dotnet pack`.
- Build the sample and assert the expected diagnostic IDs appear (simple script comparing build output to an expected list).
- On tag `v*`: pack and publish to NuGet via Trusted Publishing, create a GitHub Release with notes from CHANGELOG.

## 8. Milestones (work in this order; stop after each for maintainer review)

- **M0 — Scaffold.** Solution, projects, props files, CPM, global.json, .editorconfig, empty analyzer + one dummy test passing, CI green, MIT license, README skeleton.
- **M1 — TenantModel.** Discovery + `.editorconfig` options + unit tests for discovery (explicit filter, named filter, IEntityTypeConfiguration, convention, dynamic loop, global entities).
- **M2 — TL001** + code fix + docs page.
- **M3 — TL004** + docs page.
- **M4 — TL002** (after the FromSql verification test) + docs page.
- **M5 — TL003** + docs page.
- **M6 — TL005** + code fix + docs page.
- **M7 — Release v0.1.0.** Sample app with deliberate violations, README (problem → 30-second install → what it catches → AI-agent section → comparison table → config → contributing), CHANGELOG, pack verification, tag.

Definition of done for a milestone: builds with zero warnings in `src/`, all tests pass, docs updated, CHANGELOG entry added.

## 9. README must include
- The problem in 3 sentences with one leaking code example.
- Install: `dotnet add package TenantLint.Analyzers`.
- A table of rules with links.
- **"Using TenantLint with AI coding agents"** section: agents see warnings in `dotnet build` output; recommend adding to the project's agent instructions file: *"Treat TLxxx diagnostics as errors; never suppress them without a [CrossTenant(reason)] attribute."*
- Configuration via `.editorconfig`.
- "How is this different from…" section (Finbuckle, Knara, mcp-tenant-isolation) — respectful, factual.
- Maintainer: Ibrahim Kılıç.

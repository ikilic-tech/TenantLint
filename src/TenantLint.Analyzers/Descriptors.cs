using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

internal static class Descriptors
{
    private const string Category = "TenantIsolation";
    private const string HelpLinkBase = "https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor TL001 = new(
        id: "TL001",
        title: "IgnoreQueryFilters() removes tenant filter",
        messageFormat: "'{0}' query removes the tenant filter via IgnoreQueryFilters(). Use IgnoreQueryFilters([{1}]) to lift only the filter you need, or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL001.md");

    public static readonly DiagnosticDescriptor TL001NoSelective = new(
        id: "TL001",
        title: "IgnoreQueryFilters() removes tenant filter",
        messageFormat: "'{0}' query removes the tenant filter via IgnoreQueryFilters(). Mark the method [CrossTenant(\"reason\")] to document intentional cross-tenant access.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL001.md");

    public static readonly DiagnosticDescriptor TL002 = new(
        id: "TL002",
        title: "Raw SQL references tenant table without tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL without apparent tenant filtering. Ensure the query includes WHERE {2} = @param or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL002.md");

    public static readonly DiagnosticDescriptor TL102 = new(
        id: "TL102",
        title: "Raw SQL references tenant table with possible tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL. Tenant column '{2}' appears in a predicate clause, but verify the filter is correct.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL002.md");

    public static readonly DiagnosticDescriptor TL003 = new(
        id: "TL003",
        title: "Dapper raw SQL references tenant table without tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL without apparent tenant filtering. Ensure the query includes WHERE {2} = @param or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL003.md");

    public static readonly DiagnosticDescriptor TL103 = new(
        id: "TL103",
        title: "Dapper raw SQL references tenant table with possible tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL. Tenant column '{2}' appears in a predicate clause, but verify the filter is correct.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL003.md");

    public static readonly DiagnosticDescriptor TL004 = new(
        id: "TL004",
        title: "Tenant identity received from client input",
        messageFormat: "'{0}' receives the tenant identity from client input ({1}). Derive the tenant from a trusted source (JWT claim, authenticated session) instead, or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL004.md");

    public static readonly DiagnosticDescriptor TL104 = new(
        id: "TL104",
        title: "Tenant identity received from route parameter",
        messageFormat: "'{0}' receives the tenant identity from a route parameter. Verify that '{0}' is authorized against the authenticated principal, or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL004.md");

    public static readonly DiagnosticDescriptor TL005 = new(
        id: "TL005",
        title: "Tenant-scoped entity has no query filter",
        messageFormat: "'{0}' is tenant-scoped (has property '{1}') but DbContext '{2}' defines no query filter for it. Add HasQueryFilter or mark it in tenantlint.global_entities.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL005.md");

    public static readonly DiagnosticDescriptor TL105 = new(
        id: "TL105",
        title: "Tenant-scoped entity may be missing a query filter (dynamic filter detected)",
        messageFormat: "'{0}' may be missing a tenant filter. Dynamic filter detected in '{2}'; verify manually that '{1}' is filtered.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL005.md");

    public static readonly DiagnosticDescriptor TL010 = new(
        id: "TL010",
        title: "Tenant-scoped entity bound from request body",
        messageFormat: "'{0}' is a tenant-scoped entity bound from the request body; a client can set {1}. Bind a DTO and set {1} from the authenticated tenant.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL010.md");

    public static readonly DiagnosticDescriptor TL011 = new(
        id: "TL011",
        title: "Pooled DbContext has tenant filter on constructor-only field",
        messageFormat: "'{0}' is registered with {1} but its tenant filter uses field '{2}' which is only assigned in the constructor. Pooled instances reuse the first tenant's value. Assign the field after the instance is taken from the pool.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL011.md");

    public static readonly DiagnosticDescriptor TL111 = new(
        id: "TL111",
        title: "Tenant filter references constructor-only field (unsafe if pooled)",
        messageFormat: "'{0}' has a tenant filter referencing field '{1}' which is only assigned in the constructor. This is unsafe if the context is pooled — the constructor is not re-invoked on pool reuse.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        helpLinkUri: HelpLinkBase + "TL011.md");

    public static readonly DiagnosticDescriptor TL012 = new(
        id: "TL012",
        title: "HasQueryFilter references a value not rooted in the DbContext instance",
        messageFormat: "HasQueryFilter on '{0}' in '{1}' references a value not rooted in the DbContext instance. EF Core caches the model — the first value is baked in and reused for every tenant. Use a DbContext instance field instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL012.md");

    public static readonly DiagnosticDescriptor TL013 = new(
        id: "TL013",
        title: "Raw ADO.NET SQL references tenant table without tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL without apparent tenant filtering. Ensure the query includes WHERE {2} = @param or mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL013.md");

    public static readonly DiagnosticDescriptor TL113 = new(
        id: "TL113",
        title: "Raw ADO.NET SQL references tenant table with possible tenant filtering",
        messageFormat: "'{0}' references tenant table '{1}' in raw SQL. Tenant column '{2}' appears in a predicate clause, but verify the filter is correct.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL013.md");

    public static readonly DiagnosticDescriptor TL900 = new(
        id: "TL900",
        title: "Dynamic query filter detected",
        messageFormat: "Dynamic query filter detected in '{0}'; TL005 reduced to Info for this context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL900.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public static readonly DiagnosticDescriptor TL901 = new(
        id: "TL901",
        title: "CrossTenant attribute should state a reason",
        messageFormat: "[CrossTenant] on '{0}' should provide a reason: [CrossTenant(\"reason\")]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL901.md");

    public static readonly DiagnosticDescriptor TL902 = new(
        id: "TL902",
        title: "Class-level CrossTenant suppresses all methods",
        messageFormat: "Class-level [CrossTenant] on '{0}' suppresses tenant checks for all methods; prefer method-level [CrossTenant(\"reason\")]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL902.md");

    public static readonly DiagnosticDescriptor TL006 = new(
        id: "TL006",
        title: "Cache key does not include tenant identifier",
        messageFormat: "Cache key '{0}' does not include a tenant identifier; cached data may leak between tenants. Include the tenant ID in the cache key.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL006.md");

    public static readonly DiagnosticDescriptor TL007 = new(
        id: "TL007",
        title: "Tenant context stored in static field",
        messageFormat: "Static field '{0}' holds tenant state; tenant values in static fields are shared across all requests and tenants",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL007.md");

    public static readonly DiagnosticDescriptor TL007Captive = new(
        id: "TL007",
        title: "Singleton captures scoped tenant service",
        messageFormat: "Singleton '{0}' captures '{1}' which appears to be a scoped tenant service; the first request's tenant will be used for all requests",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL007.md");

    public static readonly DiagnosticDescriptor TL008 = new(
        id: "TL008",
        title: "Background service uses tenant-scoped DbContext without explicit scope",
        messageFormat: "Background service '{0}' uses a DbContext with tenant-scoped entities without creating a scope or setting the tenant context; tenant identity must be set explicitly in background services",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL008.md");

    public static readonly DiagnosticDescriptor TL009 = new(
        id: "TL009",
        title: "RLS session variable uses session scope instead of transaction-local",
        messageFormat: "'{0}' uses session scope; the value persists across pooled connections and may leak to other tenants. Use 'SET LOCAL' or 'set_config(..., true)' for transaction-local scope.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL009.md");

    public static readonly DiagnosticDescriptor TL014 = new(
        id: "TL014",
        title: "ABP tenant-filter bypass",
        messageFormat: "Disabling the ABP multi-tenant filter via {0}() removes tenant isolation. Mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL014.md");

    public static readonly DiagnosticDescriptor TL014Change = new(
        id: "TL014",
        title: "ABP tenant context switch",
        messageFormat: "Switching tenant context via ICurrentTenant.Change() affects tenant isolation. Mark the method [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL014.md");

    public static readonly DiagnosticDescriptor TL015 = new(
        id: "TL015",
        title: "Finbuckle TenantMismatchMode.Ignore allows cross-tenant writes",
        messageFormat: "TenantMismatchMode.Ignore allows entities to be saved with a mismatched TenantId; cross-tenant writes will silently succeed. Use TenantMismatchMode.Throw (default) or [CrossTenant(\"reason\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL015.md");

    public static readonly DiagnosticDescriptor TL201 = new(
        id: "TL201",
        title: "#pragma warning disable TL rule without justification",
        messageFormat: "#pragma warning disable {0} should have a justification comment explaining why the tenant check is suppressed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL201.md");

    public static readonly DiagnosticDescriptor TL202 = new(
        id: "TL202",
        title: "[SuppressMessage] targeting TL rule without justification",
        messageFormat: "[SuppressMessage] targeting {0} should include a Justification explaining why the tenant check is suppressed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL202.md");

    public static readonly DiagnosticDescriptor TL203 = new(
        id: "TL203",
        title: "Tenant isolation rules globally disabled",
        messageFormat: "Tenant isolation rules [{0}] are globally disabled via NoWarn or .editorconfig; suppressing tenant checks hides potential cross-tenant data leaks",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL203.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });
}

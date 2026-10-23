using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

internal static class Descriptors
{
    private const string Category = "TenantIsolation";
    private const string HelpLinkBase = "https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor TL000 = new(
        id: "TL000",
        title: "TenantLint placeholder",
        messageFormat: "TenantLint is loaded",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false,
        helpLinkUri: HelpLinkBase + "TL000.md");

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
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

/// <summary>
/// Represents the filter coverage state of a tenant-scoped entity.
/// </summary>
internal enum FilterState
{
    /// <summary>Entity has no explicit query filter referencing the tenant property.</summary>
    Unfiltered,

    /// <summary>Entity has an explicit HasQueryFilter that references the tenant property.</summary>
    Filtered,

    /// <summary>
    /// Static analysis cannot determine filter coverage (e.g., dynamic filter loop detected).
    /// TL005 reports at Info severity instead of Warning.
    /// </summary>
    Unknown
}

internal sealed class TenantEntityInfo
{
    public INamedTypeSymbol EntityType { get; }
    public string TenantPropertyName { get; }
    public FilterState FilterState { get; }
    public ImmutableArray<string> FilterNames { get; }
    public ImmutableHashSet<string> TableNames { get; }
    public INamedTypeSymbol DbContextType { get; }

    public TenantEntityInfo(
        INamedTypeSymbol entityType,
        string tenantPropertyName,
        FilterState filterState,
        ImmutableArray<string> filterNames,
        ImmutableHashSet<string> tableNames,
        INamedTypeSymbol dbContextType)
    {
        EntityType = entityType;
        TenantPropertyName = tenantPropertyName;
        FilterState = filterState;
        FilterNames = filterNames;
        TableNames = tableNames;
        DbContextType = dbContextType;
    }
}
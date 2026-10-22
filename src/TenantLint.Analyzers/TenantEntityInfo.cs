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
    public ImmutableArray<string> TenantFilterNames { get; }
    public ImmutableHashSet<string> TableNames { get; }
    public INamedTypeSymbol DbContextType { get; }
    public IPropertySymbol? DbSetProperty { get; }
    public bool HasEntityTypeConfiguration { get; }

    public TenantEntityInfo(
        INamedTypeSymbol entityType,
        string tenantPropertyName,
        FilterState filterState,
        ImmutableArray<string> filterNames,
        ImmutableArray<string> tenantFilterNames,
        ImmutableHashSet<string> tableNames,
        INamedTypeSymbol dbContextType,
        IPropertySymbol? dbSetProperty = null,
        bool hasEntityTypeConfiguration = false)
    {
        EntityType = entityType;
        TenantPropertyName = tenantPropertyName;
        FilterState = filterState;
        FilterNames = filterNames;
        TenantFilterNames = tenantFilterNames;
        TableNames = tableNames;
        DbContextType = dbContextType;
        DbSetProperty = dbSetProperty;
        HasEntityTypeConfiguration = hasEntityTypeConfiguration;
    }
}
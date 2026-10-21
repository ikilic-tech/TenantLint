using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

internal sealed class TenantEntityInfo
{
    public INamedTypeSymbol EntityType { get; }
    public string TenantPropertyName { get; }
    public bool HasExplicitFilter { get; }
    public ImmutableArray<string> FilterNames { get; }
    public ImmutableHashSet<string> TableNames { get; }
    public INamedTypeSymbol DbContextType { get; }

    public TenantEntityInfo(
        INamedTypeSymbol entityType,
        string tenantPropertyName,
        bool hasExplicitFilter,
        ImmutableArray<string> filterNames,
        ImmutableHashSet<string> tableNames,
        INamedTypeSymbol dbContextType)
    {
        EntityType = entityType;
        TenantPropertyName = tenantPropertyName;
        HasExplicitFilter = hasExplicitFilter;
        FilterNames = filterNames;
        TableNames = tableNames;
        DbContextType = dbContextType;
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

internal sealed class TenantModel
{
    public static readonly TenantModel Empty = new(
        ImmutableDictionary<INamedTypeSymbol, TenantEntityInfo>.Empty,
        ImmutableHashSet<INamedTypeSymbol>.Empty,
        hasEfCore: false);

    public ImmutableDictionary<INamedTypeSymbol, TenantEntityInfo> TenantEntities { get; }
    public ImmutableHashSet<INamedTypeSymbol> DynamicFilterContexts { get; }
    public ImmutableDictionary<string, TenantEntityInfo> TableNameToEntity { get; }
    public bool HasEfCore { get; }

    public TenantModel(
        ImmutableDictionary<INamedTypeSymbol, TenantEntityInfo> tenantEntities,
        ImmutableHashSet<INamedTypeSymbol> dynamicFilterContexts,
        bool hasEfCore)
    {
        TenantEntities = tenantEntities;
        DynamicFilterContexts = dynamicFilterContexts;
        HasEfCore = hasEfCore;

        var tableBuilder = ImmutableDictionary.CreateBuilder<string, TenantEntityInfo>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in tenantEntities)
        {
            foreach (var tableName in kvp.Value.TableNames)
            {
                // First entity wins if table names collide
                if (!tableBuilder.ContainsKey(tableName))
                    tableBuilder[tableName] = kvp.Value;
            }
        }
        TableNameToEntity = tableBuilder.ToImmutable();
    }

    public bool IsTenantScoped(INamedTypeSymbol type) =>
        TenantEntities.ContainsKey(type);

    public FilterState GetFilterState(INamedTypeSymbol type) =>
        TenantEntities.TryGetValue(type, out var info) ? info.FilterState : FilterState.Unfiltered;

    public TenantEntityInfo? GetEntityByTableName(string tableName) =>
        TableNameToEntity.TryGetValue(tableName, out var info) ? info : null;
}

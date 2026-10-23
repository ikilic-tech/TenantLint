using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TenantLint.Analyzers;

internal sealed class TenantModelBuilder
{
    private readonly Compilation _compilation;
    private readonly ConfigOptions _options;

    private readonly INamedTypeSymbol? _dbContextType;
    private readonly INamedTypeSymbol? _dbSetOpenType;
    private readonly INamedTypeSymbol? _entityTypeConfigOpenType;

    private readonly Dictionary<INamedTypeSymbol, MutableEntityInfo> _entities =
        new(SymbolEqualityComparer.Default);

    private readonly HashSet<INamedTypeSymbol> _dynamicFilterContexts =
        new(SymbolEqualityComparer.Default);

    // Track ToTable mappings discovered during OnModelCreating analysis
    private readonly Dictionary<INamedTypeSymbol, HashSet<string>> _toTableNames =
        new(SymbolEqualityComparer.Default);

    // Deferred diagnostics collected during model building (reported later by the analyzer)
    private readonly List<DeferredDiagnostic> _deferredDiagnostics = new();

    // Per-context tracking of readonly fields used in tenant filters (for TL011/TL111)
    private readonly Dictionary<INamedTypeSymbol, string> _poolingUnsafeFields =
        new(SymbolEqualityComparer.Default);

    public TenantModelBuilder(Compilation compilation, ConfigOptions options)
    {
        _compilation = compilation;
        _options = options;

        _dbContextType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        _dbSetOpenType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        _entityTypeConfigOpenType = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
    }

    public TenantModel Build()
    {
        if (_dbContextType is null)
            return TenantModel.Empty;

        var contextTypes = FindDbContextTypes();
        if (contextTypes.Length == 0)
            return TenantModel.Empty;

        foreach (var contextType in contextTypes)
        {
            DiscoverDbSetEntities(contextType);
            AnalyzeOnModelCreating(contextType);
        }

        AnalyzeEntityTypeConfigurations();

        // Propagate explicit filters from base types to derived types (F3).
        // EF Core only allows HasQueryFilter on root entities; the filter applies to all derived types.
        PropagateInheritedFilters();

        // Build immutable result
        var entitiesBuilder = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, TenantEntityInfo>(
            SymbolEqualityComparer.Default);

        foreach (var kvp in _entities)
        {
            var mutable = kvp.Value;

            // Collect table names
            var tableNames = CollectTableNames(mutable.EntityType, mutable.DbSetPropertyName);
            if (_toTableNames.TryGetValue(mutable.EntityType, out var extraNames))
            {
                tableNames = tableNames.Union(extraNames).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
            }

            // Determine filter state
            FilterState filterState;
            if (mutable.HasExplicitFilter)
            {
                filterState = FilterState.Filtered;
            }
            else if (_dynamicFilterContexts.Contains(mutable.DbContextType))
            {
                // Dynamic filters detected but we can't confirm coverage — mark Unknown
                filterState = FilterState.Unknown;
            }
            else
            {
                filterState = FilterState.Unfiltered;
            }

            entitiesBuilder[kvp.Key] = new TenantEntityInfo(
                mutable.EntityType,
                mutable.TenantPropertyName,
                filterState,
                mutable.FilterNames.ToImmutableArray(),
                mutable.TenantFilterNames.ToImmutableArray(),
                tableNames.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                mutable.DbContextType,
                mutable.DbSetProperty,
                mutable.HasEntityTypeConfiguration);
        }

        return new TenantModel(
            entitiesBuilder.ToImmutable(),
            _dynamicFilterContexts.ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            _deferredDiagnostics.ToImmutableArray(),
            _poolingUnsafeFields.ToImmutableDictionary(SymbolEqualityComparer.Default),
            hasEfCore: true);
    }

    private ImmutableArray<INamedTypeSymbol> FindDbContextTypes()
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        CollectDbContextTypes(_compilation.Assembly.GlobalNamespace, builder);
        return builder.ToImmutable();
    }

    private void CollectDbContextTypes(
        INamespaceSymbol ns,
        ImmutableArray<INamedTypeSymbol>.Builder builder)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (DerivesFrom(type, _dbContextType!))
                builder.Add(type);

            CollectNestedDbContextTypes(type, builder);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
            CollectDbContextTypes(childNs, builder);
    }

    private void CollectNestedDbContextTypes(
        INamedTypeSymbol type,
        ImmutableArray<INamedTypeSymbol>.Builder builder)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            if (DerivesFrom(nested, _dbContextType!))
                builder.Add(nested);

            CollectNestedDbContextTypes(nested, builder);
        }
    }

    private void DiscoverDbSetEntities(INamedTypeSymbol contextType)
    {
        foreach (var member in contextType.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            if (property.Type is not INamedTypeSymbol propertyType)
                continue;

            if (!IsDbSetType(propertyType))
                continue;

            var entityType = propertyType.TypeArguments[0] as INamedTypeSymbol;
            if (entityType is null)
                continue;

            if (IsGlobalEntity(entityType))
                continue;

            var tenantProperty = FindTenantProperty(entityType);
            if (tenantProperty is null)
                continue;

            if (!_entities.ContainsKey(entityType))
            {
                _entities[entityType] = new MutableEntityInfo
                {
                    EntityType = entityType,
                    TenantPropertyName = tenantProperty,
                    HasExplicitFilter = false,
                    DbContextType = contextType,
                    DbSetPropertyName = property.Name,
                    DbSetProperty = property
                };
            }
        }
    }

    private void AnalyzeOnModelCreating(INamedTypeSymbol contextType)
    {
        var onModelCreating = contextType.GetMembers("OnModelCreating")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsOverride && m.Parameters.Length == 1);

        if (onModelCreating is null)
            return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is not MethodDeclarationSyntax methodSyntax)
                continue;

            var semanticModel = _compilation.GetSemanticModel(methodSyntax.SyntaxTree);

            // Check for dynamic filter pattern first
            if (DetectDynamicFilters(methodSyntax, semanticModel))
                _dynamicFilterContexts.Add(contextType);

            // Walk all invocations in the method body
            var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                    continue;

                var reducedFrom = methodSymbol.ReducedFrom ?? methodSymbol;
                var methodName = reducedFrom.Name;

                if (methodName == "HasQueryFilter")
                {
                    AnalyzeHasQueryFilterCall(invocation, methodSymbol, semanticModel, contextType);
                }
                else if (methodName == "ToTable")
                {
                    AnalyzeToTableCall(invocation, methodSymbol, semanticModel);
                }
            }
        }
    }

    private void AnalyzeEntityTypeConfigurations()
    {
        if (_entityTypeConfigOpenType is null)
            return;

        var configTypes = new List<(INamedTypeSymbol ConfigType, INamedTypeSymbol EntityType)>();
        CollectEntityTypeConfigurations(_compilation.Assembly.GlobalNamespace, configTypes);

        foreach (var (configType, entityType) in configTypes)
        {
            // Mark that this entity has an IEntityTypeConfiguration
            if (_entities.TryGetValue(entityType, out var entityInfo))
                entityInfo.HasEntityTypeConfiguration = true;

            var configureMethod = configType.GetMembers("Configure")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Parameters.Length == 1);

            if (configureMethod is null)
                continue;

            foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax();
                if (syntax is not MethodDeclarationSyntax methodSyntax)
                    continue;

                var semanticModel = _compilation.GetSemanticModel(methodSyntax.SyntaxTree);

                var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var reducedFrom = methodSymbol.ReducedFrom ?? methodSymbol;
                    if (reducedFrom.Name == "HasQueryFilter")
                    {
                        AnalyzeHasQueryFilterCallForConfig(
                            invocation, methodSymbol, semanticModel, entityType);
                    }
                    else if (reducedFrom.Name == "ToTable")
                    {
                        AnalyzeToTableCall(invocation, methodSymbol, semanticModel);
                    }
                }
            }
        }
    }

    private void CollectEntityTypeConfigurations(
        INamespaceSymbol ns,
        List<(INamedTypeSymbol, INamedTypeSymbol)> results)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            CheckForEntityTypeConfiguration(type, results);
            CollectNestedEntityTypeConfigurations(type, results);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
            CollectEntityTypeConfigurations(childNs, results);
    }

    private void CollectNestedEntityTypeConfigurations(
        INamedTypeSymbol type,
        List<(INamedTypeSymbol, INamedTypeSymbol)> results)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            CheckForEntityTypeConfiguration(nested, results);
            CollectNestedEntityTypeConfigurations(nested, results);
        }
    }

    private void CheckForEntityTypeConfiguration(
        INamedTypeSymbol type,
        List<(INamedTypeSymbol, INamedTypeSymbol)> results)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.OriginalDefinition.Equals(_entityTypeConfigOpenType, SymbolEqualityComparer.Default)
                && iface.TypeArguments.Length == 1
                && iface.TypeArguments[0] is INamedTypeSymbol entityType)
            {
                results.Add((type, entityType));
            }
        }
    }

    private bool DetectDynamicFilters(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        // Look for foreach loops over GetEntityTypes()
        foreach (var foreachStatement in method.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (foreachStatement.Expression is not InvocationExpressionSyntax foreachInvocation)
                continue;

            var foreachSymbol = semanticModel.GetSymbolInfo(foreachInvocation).Symbol as IMethodSymbol;
            if (foreachSymbol is null)
                continue;

            if (foreachSymbol.Name != "GetEntityTypes")
                continue;

            // Check if the loop body contains HasQueryFilter or SetQueryFilter calls
            var loopBody = foreachStatement.Statement;
            foreach (var innerInvocation in loopBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var innerSymbol = semanticModel.GetSymbolInfo(innerInvocation).Symbol as IMethodSymbol;
                if (innerSymbol is null)
                    continue;

                var innerName = (innerSymbol.ReducedFrom ?? innerSymbol).Name;
                if (innerName == "HasQueryFilter" || innerName == "SetQueryFilter")
                    return true;
            }
        }

        return false;
    }

    private void AnalyzeHasQueryFilterCall(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol contextType)
    {
        var entityType = ResolveEntityFromFluentChain(invocation, semanticModel);
        if (entityType is null)
            return;

        AnalyzeFilterLambda(invocation, methodSymbol, semanticModel, entityType, contextType);
    }

    private void AnalyzeHasQueryFilterCallForConfig(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol entityType)
    {
        // For IEntityTypeConfiguration, we need the DbContext. We may not have one yet
        // if the entity was not exposed as a DbSet. Find the associated context or use a placeholder.
        var contextType = FindContextForEntity(entityType);

        AnalyzeFilterLambda(invocation, methodSymbol, semanticModel, entityType, contextType);
    }

    private void AnalyzeFilterLambda(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol entityType,
        INamedTypeSymbol? contextType)
    {
        if (IsGlobalEntity(entityType))
            return;

        // Determine if this is a named overload (2+ params: name, lambda)
        string? filterName = null;
        ExpressionSyntax? lambdaArg = null;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 2)
        {
            // Named overload: HasQueryFilter("name", lambda)
            var nameArg = semanticModel.GetConstantValue(args[0].Expression);
            if (nameArg.HasValue && nameArg.Value is string name)
                filterName = name;

            lambdaArg = args[1].Expression;
        }
        else if (args.Count == 1)
        {
            lambdaArg = args[0].Expression;
        }

        if (lambdaArg is null)
            return;

        var referencesTenantProp = LambdaReferencesTenantProperty(lambdaArg, semanticModel);

        if (!referencesTenantProp)
        {
            // Filter exists but doesn't reference a tenant property.
            // If entity already tracked by convention, record the non-tenant filter name.
            if (filterName is not null && _entities.TryGetValue(entityType, out var existing))
                existing.FilterNames.Add(filterName);
            return;
        }

        // This is a tenant filter — check if the value side is rooted in `this` (TL012)
        if (contextType is not null)
        {
            CheckFilterValueRooting(invocation, lambdaArg, semanticModel, entityType, contextType);
        }

        // Record the tenant filter
        var tenantProperty = FindTenantProperty(entityType);
        if (tenantProperty is null)
            return;

        if (_entities.TryGetValue(entityType, out var info))
        {
            info.HasExplicitFilter = true;
            if (filterName is not null)
            {
                info.FilterNames.Add(filterName);
                info.TenantFilterNames.Add(filterName);
            }
        }
        else if (contextType is not null)
        {
            var newInfo = new MutableEntityInfo
            {
                EntityType = entityType,
                TenantPropertyName = tenantProperty,
                HasExplicitFilter = true,
                DbContextType = contextType,
            };
            if (filterName is not null)
            {
                newInfo.FilterNames.Add(filterName);
                newInfo.TenantFilterNames.Add(filterName);
            }
            _entities[entityType] = newInfo;
        }
    }

    private void AnalyzeToTableCall(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        if (methodSymbol.Parameters.Length < 1)
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 1)
            return;

        var constantValue = semanticModel.GetConstantValue(args[0].Expression);
        if (!constantValue.HasValue || constantValue.Value is not string tableName)
            return;

        var entityType = ResolveEntityFromFluentChain(invocation, semanticModel);
        if (entityType is null)
            return;

        if (!_toTableNames.TryGetValue(entityType, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _toTableNames[entityType] = names;
        }

        names.Add(tableName);
        names.Add(tableName.ToLowerInvariant());
        names.Add(ConfigOptions.ToSnakeCase(tableName));
    }

    private void CheckFilterValueRooting(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax lambdaArg,
        SemanticModel semanticModel,
        INamedTypeSymbol entityType,
        INamedTypeSymbol contextType)
    {
        // Find binary expressions (== or !=) in the lambda where one side is e.TenantId
        // and check whether the OTHER side is rooted in `this` (the DbContext instance).
        SyntaxNode lambdaBody;
        if (lambdaArg is SimpleLambdaExpressionSyntax simple)
            lambdaBody = simple.Body;
        else if (lambdaArg is ParenthesizedLambdaExpressionSyntax paren)
            lambdaBody = paren.Body;
        else
            return;

        foreach (var binary in lambdaBody.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>())
        {
            if (!binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression) &&
                !binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression))
                continue;

            // Determine which side is the entity property and which is the value
            ExpressionSyntax? valueSide = null;
            if (IsEntityTenantPropertyAccess(binary.Left, semanticModel))
                valueSide = binary.Right;
            else if (IsEntityTenantPropertyAccess(binary.Right, semanticModel))
                valueSide = binary.Left;

            if (valueSide is null)
                continue;

            // Skip constants and literals — they're intentional
            var constant = semanticModel.GetConstantValue(valueSide);
            if (constant.HasValue)
                continue;

            if (!IsValueRootedInInstance(valueSide, semanticModel, contextType))
            {
                // Get a precise location on the HasQueryFilter method name
                var location = invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    ? memberAccess.Name.GetLocation()
                    : invocation.GetLocation();

                _deferredDiagnostics.Add(new DeferredDiagnostic(
                    Descriptors.TL012,
                    location,
                    entityType.Name,
                    contextType.Name));
                return; // One TL012 per HasQueryFilter call is enough
            }

            // Value IS rooted in instance — check if the root field is readonly (TL111)
            var rootField = FindRootInstanceField(valueSide, semanticModel, contextType);
            if (rootField is not null && rootField.IsReadOnly &&
                !_poolingUnsafeFields.ContainsKey(contextType))
            {
                _poolingUnsafeFields[contextType] = rootField.Name;
                _deferredDiagnostics.Add(new DeferredDiagnostic(
                    Descriptors.TL111,
                    rootField.Locations.FirstOrDefault() ?? Location.None,
                    contextType.Name,
                    rootField.Name));
            }
        }
    }

    private bool IsEntityTenantPropertyAccess(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        return _options.TenantPropertyNames.Contains(memberAccess.Name.Identifier.ValueText);
    }

    private static bool IsValueRootedInInstance(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        INamedTypeSymbol contextType)
    {
        // Walk to the root of the expression chain and check if it's `this` or an instance member.
        var root = GetExpressionRoot(expression);

        if (root is ThisExpressionSyntax)
            return true;

        if (root is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            return IsInstanceMemberOf(symbol, contextType);
        }

        // For invocations without a receiver (static method calls like GetTenantId()), check the symbol
        if (root is InvocationExpressionSyntax rootInvocation)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(rootInvocation).Symbol as IMethodSymbol;
            if (methodSymbol is not null && methodSymbol.IsStatic)
                return false;

            // Instance method on implicit this
            if (methodSymbol is not null && !methodSymbol.IsStatic &&
                IsContainedInType(methodSymbol.ContainingType, contextType))
                return true;
        }

        return false;
    }

    private static ExpressionSyntax GetExpressionRoot(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            if (current is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
            }
            else if (current is InvocationExpressionSyntax invocation &&
                     invocation.Expression is MemberAccessExpressionSyntax invMemberAccess)
            {
                current = invMemberAccess.Expression;
            }
            else
            {
                return current;
            }
        }
    }

    private static bool IsInstanceMemberOf(ISymbol? symbol, INamedTypeSymbol contextType)
    {
        if (symbol is null)
            return false;

        if (symbol is IFieldSymbol field && !field.IsStatic &&
            IsContainedInType(field.ContainingType, contextType))
            return true;

        if (symbol is IPropertySymbol property && !property.IsStatic &&
            IsContainedInType(property.ContainingType, contextType))
            return true;

        return false;
    }

    private static bool IsContainedInType(INamedTypeSymbol? containingType, INamedTypeSymbol contextType)
    {
        if (containingType is null)
            return false;

        var current = contextType;
        while (current is not null)
        {
            if (current.Equals(containingType, SymbolEqualityComparer.Default))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static IFieldSymbol? FindRootInstanceField(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        INamedTypeSymbol contextType)
    {
        var root = GetExpressionRoot(expression);

        if (root is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is IFieldSymbol field && !field.IsStatic &&
                IsContainedInType(field.ContainingType, contextType))
                return field;
        }
        else if (root is ThisExpressionSyntax thisExpr &&
                 thisExpr.Parent is MemberAccessExpressionSyntax memberAccess)
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol is IFieldSymbol field && !field.IsStatic &&
                IsContainedInType(field.ContainingType, contextType))
                return field;
        }

        return null;
    }

    private bool LambdaReferencesTenantProperty(SyntaxNode lambdaNode, SemanticModel semanticModel)
    {
        // Extract the lambda parameter symbol to verify member accesses are on the entity,
        // not on an arbitrary receiver (e.g., someService.TenantId).
        IParameterSymbol? lambdaParam = null;
        if (lambdaNode is SimpleLambdaExpressionSyntax simpleLambda)
        {
            lambdaParam = semanticModel.GetDeclaredSymbol(simpleLambda.Parameter) as IParameterSymbol;
        }
        else if (lambdaNode is ParenthesizedLambdaExpressionSyntax parenLambda
                 && parenLambda.ParameterList.Parameters.Count > 0)
        {
            lambdaParam = semanticModel.GetDeclaredSymbol(parenLambda.ParameterList.Parameters[0]) as IParameterSymbol;
        }

        foreach (var memberAccess in lambdaNode.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!_options.TenantPropertyNames.Contains(memberAccess.Name.Identifier.ValueText))
                continue;

            // Verify the receiver is the lambda parameter (the entity)
            if (lambdaParam is not null)
            {
                var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
                if (receiverSymbol is IParameterSymbol paramSym
                    && paramSym.Equals(lambdaParam, SymbolEqualityComparer.Default))
                {
                    return true;
                }
            }
            else
            {
                // Fallback: if we can't resolve the lambda parameter, accept the match
                return true;
            }
        }

        return false;
    }

    private INamedTypeSymbol? ResolveEntityFromFluentChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Walk up the fluent chain looking for Entity<T>() or the EntityTypeBuilder<T> receiver
        var current = invocation.Expression;

        while (current is MemberAccessExpressionSyntax memberAccess)
        {
            var receiverExpr = memberAccess.Expression;

            if (receiverExpr is InvocationExpressionSyntax receiverInvocation)
            {
                var receiverSymbol = semanticModel.GetSymbolInfo(receiverInvocation).Symbol as IMethodSymbol;
                if (receiverSymbol is not null)
                {
                    // Check if this is Entity<T>()
                    if (receiverSymbol.Name == "Entity" && receiverSymbol.TypeArguments.Length == 1)
                    {
                        return receiverSymbol.TypeArguments[0] as INamedTypeSymbol;
                    }

                    // Otherwise, check the return type for EntityTypeBuilder<T>
                    var entityFromReturnType = ExtractEntityFromBuilderType(receiverSymbol.ReturnType);
                    if (entityFromReturnType is not null)
                        return entityFromReturnType;
                }

                // Continue walking up the chain
                current = receiverInvocation.Expression;
            }
            else if (receiverExpr is IdentifierNameSyntax identifier)
            {
                // Could be a parameter like 'builder' in IEntityTypeConfiguration
                var paramSymbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (paramSymbol is IParameterSymbol param)
                {
                    return ExtractEntityFromBuilderType(param.Type);
                }
                break;
            }
            else
            {
                break;
            }
        }

        return null;
    }

    private INamedTypeSymbol? ExtractEntityFromBuilderType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
            return null;

        // Check for EntityTypeBuilder<T>
        if (namedType.Name == "EntityTypeBuilder" && namedType.TypeArguments.Length == 1)
            return namedType.TypeArguments[0] as INamedTypeSymbol;

        return null;
    }

    private ImmutableHashSet<string> CollectTableNames(
        INamedTypeSymbol entityType,
        string? dbSetPropertyName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Entity type name
        var entityName = entityType.Name;
        names.Add(entityName);
        names.Add(entityName.ToLowerInvariant());
        names.Add(ConfigOptions.ToSnakeCase(entityName));

        // DbSet property name (usually plural)
        if (dbSetPropertyName is not null && !names.Contains(dbSetPropertyName))
        {
            names.Add(dbSetPropertyName);
            names.Add(dbSetPropertyName.ToLowerInvariant());
            names.Add(ConfigOptions.ToSnakeCase(dbSetPropertyName));
        }

        // [Table("name")] attribute
        foreach (var attribute in entityType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "TableAttribute" &&
                attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string attrTableName)
            {
                names.Add(attrTableName);
                names.Add(attrTableName.ToLowerInvariant());
                names.Add(ConfigOptions.ToSnakeCase(attrTableName));
            }
        }

        return names.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private string? FindTenantProperty(INamedTypeSymbol type)
    {
        // Walk the type hierarchy looking for a tenant property
        var current = type;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    _options.TenantPropertyNames.Contains(property.Name))
                {
                    return property.Name;
                }
            }
            current = current.BaseType;
        }
        return null;
    }

    private bool IsGlobalEntity(INamedTypeSymbol type) =>
        _options.GlobalEntities.Contains(type.Name);

    private bool IsDbSetType(INamedTypeSymbol type) =>
        _dbSetOpenType is not null &&
        type.OriginalDefinition.Equals(_dbSetOpenType, SymbolEqualityComparer.Default);

    private INamedTypeSymbol? FindContextForEntity(INamedTypeSymbol entityType)
    {
        if (_entities.TryGetValue(entityType, out var existing))
            return existing.DbContextType;

        return null;
    }

    private void PropagateInheritedFilters()
    {
        // EF Core only allows HasQueryFilter on the root entity of an inheritance hierarchy,
        // and the filter applies to all derived types. Propagate filter status accordingly.
        foreach (var kvp in _entities)
        {
            if (kvp.Value.HasExplicitFilter)
                continue;

            var baseType = kvp.Value.EntityType.BaseType;
            while (baseType is not null)
            {
                if (_entities.TryGetValue(baseType, out var baseInfo) && baseInfo.HasExplicitFilter)
                {
                    kvp.Value.HasExplicitFilter = true;
                    break;
                }

                // Also check entities not in the dict but whose type has a filter
                // (e.g., base entity not exposed as DbSet but has HasQueryFilter)
                baseType = baseType.BaseType;
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.Equals(baseType, SymbolEqualityComparer.Default))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private sealed class MutableEntityInfo
    {
        public INamedTypeSymbol EntityType { get; set; } = null!;
        public string TenantPropertyName { get; set; } = null!;
        public bool HasExplicitFilter { get; set; }
        public List<string> FilterNames { get; } = new();
        public List<string> TenantFilterNames { get; } = new();
        public INamedTypeSymbol DbContextType { get; set; } = null!;
        public string? DbSetPropertyName { get; set; }
        public IPropertySymbol? DbSetProperty { get; set; }
        public bool HasEntityTypeConfiguration { get; set; }
    }
}

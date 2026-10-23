using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TenantLint.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantIsolationAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> PlaceholderReasons =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "", "TODO", "reason", "n/a", "TODO: reason");

    private static readonly ImmutableHashSet<string> RawSqlMethodNames =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "ExecuteSql", "ExecuteSqlAsync",
            "ExecuteSqlRaw", "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
            "SqlQuery", "SqlQueryRaw");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Descriptors.TL000,
            Descriptors.TL001,
            Descriptors.TL001NoSelective,
            Descriptors.TL002,
            Descriptors.TL102,
            Descriptors.TL003,
            Descriptors.TL103,
            Descriptors.TL004,
            Descriptors.TL104,
            Descriptors.TL005,
            Descriptors.TL105,
            Descriptors.TL006,
            Descriptors.TL007,
            Descriptors.TL007Captive,
            Descriptors.TL008,
            Descriptors.TL009,
            Descriptors.TL010,
            Descriptors.TL011,
            Descriptors.TL111,
            Descriptors.TL012,
            Descriptors.TL013,
            Descriptors.TL113,
            Descriptors.TL014,
            Descriptors.TL014Change,
            Descriptors.TL015,
            Descriptors.TL201,
            Descriptors.TL202,
            Descriptors.TL203,
            Descriptors.TL900,
            Descriptors.TL901,
            Descriptors.TL902);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStartContext =>
        {
            var compilation = compilationStartContext.Compilation;
            var options = ConfigOptions.Create(
                compilationStartContext.Options.AnalyzerConfigOptionsProvider,
                compilation);

            // TL201: #pragma warning disable TLxxx without justification (EF-agnostic)
            compilationStartContext.RegisterSyntaxTreeAction(
                treeContext => AnalyzePragmaDirectives(treeContext));

            // TL202: [SuppressMessage] targeting TL rules without justification (EF-agnostic)
            compilationStartContext.RegisterSymbolAction(
                symbolContext => AnalyzeSuppressMessageAttribute(symbolContext),
                SymbolKind.Method, SymbolKind.NamedType);

            // TL203: TL rules globally disabled via NoWarn or .editorconfig (EF-agnostic)
            compilationStartContext.RegisterCompilationEndAction(endContext =>
                AnalyzeGlobalSuppressions(
                    endContext,
                    compilationStartContext.Options.AnalyzerConfigOptionsProvider));

            // TL007: Static field/property with tenant state (EF-agnostic)
            compilationStartContext.RegisterSymbolAction(
                symbolContext => AnalyzeStaticTenantField(symbolContext, options),
                SymbolKind.Field, SymbolKind.Property);

            // TL014: ABP tenant-filter bypass (EF-agnostic — ABP has its own filter system)
            var abpDataFilter = compilation.GetTypeByMetadataName("Volo.Abp.Data.IDataFilter");
            var abpCurrentTenant = compilation.GetTypeByMetadataName("Volo.Abp.MultiTenancy.ICurrentTenant");
            var abpIMultiTenant = compilation.GetTypeByMetadataName("Volo.Abp.MultiTenancy.IMultiTenant");

            if (abpDataFilter is not null || abpCurrentTenant is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeAbpBypass(
                        operationContext, options,
                        abpDataFilter, abpCurrentTenant, abpIMultiTenant),
                    OperationKind.Invocation);
            }

            // TL015: Finbuckle TenantMismatchMode.Ignore (EF-agnostic — Finbuckle has its own mode)
            var tenantMismatchModeType = compilation.GetTypeByMetadataName(
                "Finbuckle.MultiTenant.EntityFrameworkCore.TenantMismatchMode");

            if (tenantMismatchModeType is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeFinbuckleMismatchMode(
                        operationContext, options, tenantMismatchModeType),
                    OperationKind.SimpleAssignment);
            }

            var builder = new TenantModelBuilder(compilation, options);
            var model = builder.Build();

            if (!model.HasEfCore)
                return;

            // Resolve EntityFrameworkQueryableExtensions type
            var efQueryableExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");

            // Detect if selective overload exists (2-param IgnoreQueryFilters)
            var hasSelectiveOverload = false;
            if (efQueryableExtensions is not null)
            {
                hasSelectiveOverload = efQueryableExtensions.GetMembers("IgnoreQueryFilters")
                    .OfType<IMethodSymbol>()
                    .Any(m => m.Parameters.Length == 2);
            }

            // Check if CrossTenantAttribute exists in compilation
            var hasCrossTenantAttribute = HasCrossTenantAttributeInCompilation(
                compilation, options.CrossTenantAttribute);

            // TL001 + TL002 + TL003: Register single invocation operation action
            var relationalDbFacadeExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions");

            var dapperSqlMapper = compilation.GetTypeByMetadataName("Dapper.SqlMapper");
            var dapperCommandDefinition = compilation.GetTypeByMetadataName("Dapper.CommandDefinition");

            if (efQueryableExtensions is not null ||
                relationalDbFacadeExtensions is not null ||
                dapperSqlMapper is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext =>
                    {
                        if (efQueryableExtensions is not null)
                        {
                            AnalyzeInvocation(
                                operationContext, model, options, efQueryableExtensions,
                                hasSelectiveOverload, hasCrossTenantAttribute);
                        }
                        if (relationalDbFacadeExtensions is not null)
                        {
                            AnalyzeRawSqlInvocation(
                                operationContext, model, options, relationalDbFacadeExtensions);
                        }
                        if (dapperSqlMapper is not null)
                        {
                            AnalyzeDapperInvocation(
                                operationContext, model, options,
                                dapperSqlMapper, dapperCommandDefinition);
                        }
                    },
                    OperationKind.Invocation);
            }

            // TL901/TL902: Register symbol actions
            compilationStartContext.RegisterSymbolAction(
                symbolContext => AnalyzeMethodSymbol(symbolContext, options),
                SymbolKind.Method);

            compilationStartContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedTypeSymbol(symbolContext, options),
                SymbolKind.NamedType);

            // TL010/TL004: Detect tenant-scoped entity bound from request body / client input
            var aspNetTypes = AspNetCoreTypes.Resolve(compilation);
            if (aspNetTypes.HasAny)
            {
                compilationStartContext.RegisterSymbolAction(
                    symbolContext => AnalyzeActionParameterBinding(
                        symbolContext, model, options, aspNetTypes),
                    SymbolKind.Method);
            }

            // TL004: Detect direct header/query reads for tenant values
            if (aspNetTypes.HasHeaderOrQuery)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeDirectHeaderQueryAccess(
                        operationContext, options, aspNetTypes),
                    OperationKind.PropertyReference);

                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeTryGetValueCall(
                        operationContext, options, aspNetTypes),
                    OperationKind.Invocation);
            }

            // TL010: Detect body-bound tenant entity in minimal API handlers
            var endpointRouteBuilderExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions");
            if (endpointRouteBuilderExtensions is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeMinimalApiHandler(
                        operationContext, model, options, endpointRouteBuilderExtensions),
                    OperationKind.Invocation);
            }

            // TL005/TL105: Report unfiltered tenant entities via SymbolAction (local diagnostic)
            // Build a lookup of DbSet properties that need TL005/TL105 reporting
            var unfilteredDbSetProperties = new Dictionary<IPropertySymbol, TenantEntityInfo>(
                SymbolEqualityComparer.Default);
            foreach (var kvp in model.TenantEntities)
            {
                var info = kvp.Value;
                if (info.FilterState != FilterState.Filtered && info.DbSetProperty is not null)
                    unfilteredDbSetProperties[info.DbSetProperty] = info;
            }

            if (unfilteredDbSetProperties.Count > 0)
            {
                compilationStartContext.RegisterSymbolAction(
                    symbolContext =>
                    {
                        var property = (IPropertySymbol)symbolContext.Symbol;
                        if (!unfilteredDbSetProperties.TryGetValue(property, out var info))
                            return;

                        var descriptor = info.FilterState == FilterState.Unknown
                            ? Descriptors.TL105
                            : Descriptors.TL005;

                        var propertiesBuilder = ImmutableDictionary.CreateBuilder<string, string?>();
                        propertiesBuilder.Add("EntityTypeName", info.EntityType.ToDisplayString());
                        propertiesBuilder.Add("TenantPropertyName", info.TenantPropertyName);
                        propertiesBuilder.Add("HasEntityTypeConfiguration",
                            info.HasEntityTypeConfiguration.ToString());
                        propertiesBuilder.Add("DbContextTypeName", info.DbContextType.ToDisplayString());

                        // Pass named filter info so code fix can generate the correct syntax
                        var contextUsesNamedFilters = model.TenantEntities.Values
                            .Any(e => SymbolEqualityComparer.Default.Equals(e.DbContextType, info.DbContextType)
                                      && e.FilterNames.Length > 0);
                        if (contextUsesNamedFilters || options.TenantFilterName is not null)
                        {
                            propertiesBuilder.Add("UseNamedFilter", "True");
                            propertiesBuilder.Add("TenantFilterName",
                                options.TenantFilterName ?? "Tenant");
                        }

                        symbolContext.ReportDiagnostic(Diagnostic.Create(
                            descriptor,
                            property.Locations.FirstOrDefault() ?? Location.None,
                            propertiesBuilder.ToImmutable(),
                            info.EntityType.Name,
                            info.TenantPropertyName,
                            info.DbContextType.Name));
                    },
                    SymbolKind.Property);
            }

            // TL011: Detect pooled DbContext registration with readonly tenant filter field
            var efServiceCollectionExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions");

            if (efServiceCollectionExtensions is not null && model.PoolingUnsafeFields.Count > 0)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzePoolRegistration(
                        operationContext, model, efServiceCollectionExtensions),
                    OperationKind.Invocation);
            }

            // TL013/TL113: Detect raw ADO.NET SQL on tenant tables
            var dbCommandType = compilation.GetTypeByMetadataName("System.Data.Common.DbCommand");
            var iDbCommandType = compilation.GetTypeByMetadataName("System.Data.IDbCommand");

            if (dbCommandType is not null || iDbCommandType is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeCommandTextAssignment(
                        operationContext, model, options,
                        dbCommandType, iDbCommandType),
                    OperationKind.SimpleAssignment);

                if (dbCommandType is not null)
                {
                    compilationStartContext.RegisterOperationAction(
                        operationContext => AnalyzeDbCommandCreation(
                            operationContext, model, options, dbCommandType),
                        OperationKind.ObjectCreation);
                }
            }

            // TL011/TL012/TL111 + TL900: Report deferred diagnostics and dynamic filter contexts
            if (model.DeferredDiagnostics.Length > 0 || model.DynamicFilterContexts.Count > 0)
            {
                compilationStartContext.RegisterCompilationEndAction(endContext =>
                {
                    foreach (var deferred in model.DeferredDiagnostics)
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(
                            deferred.Descriptor,
                            deferred.Location,
                            deferred.Args));
                    }

                    foreach (var contextType in model.DynamicFilterContexts)
                    {
                        var location = contextType.Locations.FirstOrDefault() ?? Location.None;
                        endContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.TL900,
                            location,
                            contextType.Name));
                    }
                });
            }

            // TL009: RLS session variable leak (SET without LOCAL, set_config with false)
            if (relationalDbFacadeExtensions is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeRlsSessionVariable(
                        operationContext, options, relationalDbFacadeExtensions),
                    OperationKind.Invocation);
            }

            // TL009: Also check ADO.NET CommandText for RLS patterns
            if (dbCommandType is not null || iDbCommandType is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeRlsCommandText(
                        operationContext, options, dbCommandType, iDbCommandType),
                    OperationKind.SimpleAssignment);
            }

            // TL006: Cache key without tenant context
            var memoryCacheType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Caching.Memory.IMemoryCache");
            var distributedCacheType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Caching.Distributed.IDistributedCache");
            var hybridCacheType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Caching.Hybrid.HybridCache");

            if (memoryCacheType is not null || distributedCacheType is not null ||
                hybridCacheType is not null)
            {
                compilationStartContext.RegisterOperationAction(
                    operationContext => AnalyzeCacheKeyInvocation(
                        operationContext, model, options,
                        memoryCacheType, distributedCacheType, hybridCacheType),
                    OperationKind.Invocation);
            }

            // TL008: Background service without tenant scope
            var backgroundServiceType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Hosting.BackgroundService");
            var hostedServiceType = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Hosting.IHostedService");

            if ((backgroundServiceType is not null || hostedServiceType is not null) &&
                model.TenantEntities.Count > 0)
            {
                compilationStartContext.RegisterSymbolAction(
                    symbolContext => AnalyzeBackgroundService(
                        symbolContext, model, options,
                        backgroundServiceType, hostedServiceType),
                    SymbolKind.NamedType);
            }
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol efQueryableExtensions,
        bool hasSelectiveOverload,
        bool hasCrossTenantAttribute)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        // Check if this is EntityFrameworkQueryableExtensions.IgnoreQueryFilters
        var containingType = targetMethod.ReducedFrom?.ContainingType ?? targetMethod.ContainingType;
        if (!containingType.Equals(efQueryableExtensions, SymbolEqualityComparer.Default))
            return;

        if (targetMethod.Name != "IgnoreQueryFilters")
            return;

        // Check if parameterless: original definition has 1 param (the `this IQueryable<T>`)
        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        if (originalMethod.Parameters.Length != 1)
            return;

        // Extract T from the type arguments
        if (targetMethod.TypeArguments.Length != 1)
            return;

        var entityType = targetMethod.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return;

        // Check if T is tenant-scoped
        if (!model.IsTenantScoped(entityType))
            return;

        // Check [CrossTenant] exemption on enclosing method/class
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Get entity info for filter names
        var entityInfo = model.TenantEntities[entityType];
        var nonTenantFilterNames = entityInfo.FilterNames
            .Except(entityInfo.TenantFilterNames)
            .ToImmutableArray();

        // Choose descriptor
        var useSelective = hasSelectiveOverload && nonTenantFilterNames.Length > 0;
        var descriptor = useSelective ? Descriptors.TL001 : Descriptors.TL001NoSelective;

        // Build diagnostic properties for the code fix
        var propertiesBuilder = ImmutableDictionary.CreateBuilder<string, string?>();
        propertiesBuilder.Add("HasSelectiveOverload", hasSelectiveOverload.ToString());
        propertiesBuilder.Add("HasCrossTenantAttribute", hasCrossTenantAttribute.ToString());
        if (nonTenantFilterNames.Length > 0)
        {
            propertiesBuilder.Add("NonTenantFilterNames",
                string.Join(",", nonTenantFilterNames));
        }

        // Narrow location to the IgnoreQueryFilters name token
        var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
            && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Syntax.GetLocation();

        // Build message args
        var filterNamesArg = useSelective
            ? string.Join(", ", nonTenantFilterNames.Select(n => "\"" + n + "\""))
            : string.Empty;

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            location,
            propertiesBuilder.ToImmutable(),
            entityType.Name,
            filterNamesArg));
    }

    private static void AnalyzeRawSqlInvocation(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol relationalDbFacadeExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        // Check method name is a known raw SQL method
        if (!RawSqlMethodNames.Contains(targetMethod.Name))
            return;

        // Check this is RelationalDatabaseFacadeExtensions (extension method)
        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        var containingType = originalMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(relationalDbFacadeExtensions, SymbolEqualityComparer.Default))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Extract SQL text
        if (!SqlAnalyzer.TryExtractSqlFromInvocation(invocation, "sql", out var sqlText))
            return;

        ReportRawSqlDiagnostic(context, invocation, sqlText, model, options,
            Descriptors.TL002, Descriptors.TL102);
    }

    private static void ReportRawSqlDiagnostic(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        string sqlText,
        TenantModel model,
        ConfigOptions options,
        DiagnosticDescriptor warningDescriptor,
        DiagnosticDescriptor infoDescriptor)
    {
        var result = SqlAnalyzer.Analyze(sqlText, model, options);
        if (!result.HasTenantTableReference)
            return;

        var descriptor = result.HasTenantColumnInPredicate
            ? infoDescriptor
            : warningDescriptor;

        var methodName = invocation.TargetMethod.Name;

        // Narrow location to the method name token
        var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
            && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor, location,
            methodName, result.MatchedTableName, result.MatchedColumnName));
    }

    private static void AnalyzePoolRegistration(
        OperationAnalysisContext context,
        TenantModel model,
        INamedTypeSymbol efServiceCollectionExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        var containingType = targetMethod.ReducedFrom?.ContainingType ?? targetMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(efServiceCollectionExtensions, SymbolEqualityComparer.Default))
            return;

        var methodName = targetMethod.Name;
        if (methodName != "AddDbContextPool" && methodName != "AddPooledDbContextFactory")
            return;

        // Get the TContext type argument
        if (targetMethod.TypeArguments.Length < 1)
            return;

        var contextType = targetMethod.TypeArguments[0] as INamedTypeSymbol;
        if (contextType is null)
            return;

        // Check if this context has a readonly tenant filter field
        if (!model.PoolingUnsafeFields.TryGetValue(contextType, out var fieldName))
            return;

        // Narrow location to the method name
        var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
            && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL011,
            location,
            contextType.Name,
            methodName,
            fieldName));
    }

    private static void AnalyzeDapperInvocation(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol dapperSqlMapper,
        INamedTypeSymbol? dapperCommandDefinition)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        // Check method name starts with Query or Execute (covers all Dapper SQL methods)
        if (!IsDapperSqlMethod(targetMethod.Name))
            return;

        // Check containing type is Dapper.SqlMapper
        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        var containingType = originalMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(dapperSqlMapper, SymbolEqualityComparer.Default))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Try to extract SQL from "sql" parameter (string overloads)
        if (SqlAnalyzer.TryExtractSqlFromInvocation(invocation, "sql", out var sqlText))
        {
            ReportRawSqlDiagnostic(context, invocation, sqlText, model, options,
                Descriptors.TL003, Descriptors.TL103);
            return;
        }

        // Try to extract SQL from CommandDefinition parameter
        if (dapperCommandDefinition is not null &&
            TryExtractSqlFromCommandDefinitionArg(invocation, dapperCommandDefinition, out sqlText))
        {
            ReportRawSqlDiagnostic(context, invocation, sqlText, model, options,
                Descriptors.TL003, Descriptors.TL103);
        }
    }

    private static bool TryExtractSqlFromCommandDefinitionArg(
        IInvocationOperation invocation,
        INamedTypeSymbol commandDefinitionType,
        out string sqlText)
    {
        sqlText = "";

        foreach (var arg in invocation.Arguments)
        {
            if (arg.Parameter?.Name != "command")
                continue;

            var creation = FindObjectCreation(arg.Value);

            // If not found directly, try following local variable declaration via syntax
            if (creation is null && arg.Value is ILocalReferenceOperation localRef)
            {
                creation = FindLocalVariableCreation(localRef);
            }

            if (creation is not null &&
                creation.Type?.Equals(commandDefinitionType, SymbolEqualityComparer.Default) == true)
            {
                foreach (var ctorArg in creation.Arguments)
                {
                    if (ctorArg.Parameter?.Name == "commandText")
                        return SqlAnalyzer.TryExtractSqlFromArgument(ctorArg, out sqlText);
                }
            }

            break;
        }

        return false;
    }

    private static IObjectCreationOperation? FindObjectCreation(IOperation operation)
    {
        var value = operation;
        while (value is IConversionOperation conv)
            value = conv.Operand;
        return value as IObjectCreationOperation;
    }

    private static IObjectCreationOperation? FindLocalVariableCreation(ILocalReferenceOperation localRef)
    {
        // Walk the syntax to find the variable declarator and its initializer
        var localSymbol = localRef.Local;
        foreach (var syntaxRef in localSymbol.DeclaringSyntaxReferences)
        {
            var declaratorSyntax = syntaxRef.GetSyntax();
            if (declaratorSyntax is VariableDeclaratorSyntax declarator &&
                declarator.Initializer?.Value is ObjectCreationExpressionSyntax or
                    ImplicitObjectCreationExpressionSyntax)
            {
                // Find the matching IOperation in the parent block
                var semanticModel = localRef.SemanticModel;
                if (semanticModel is not null)
                {
                    var initOp = semanticModel.GetOperation(declarator.Initializer.Value);
                    if (initOp is IObjectCreationOperation creation)
                        return creation;
                    // Unwrap conversion
                    if (initOp is IConversionOperation convOp &&
                        convOp.Operand is IObjectCreationOperation innerCreation)
                        return innerCreation;
                }
            }
        }

        return null;
    }

    private static readonly ImmutableHashSet<string> BodyBindingMapMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "MapPost", "MapPut", "MapPatch");

    private static void AnalyzeMinimalApiHandler(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol endpointRouteBuilderExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        if (!BodyBindingMapMethods.Contains(targetMethod.Name))
            return;

        var containingType = targetMethod.ReducedFrom?.ContainingType ?? targetMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(endpointRouteBuilderExtensions, SymbolEqualityComparer.Default))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Find the delegate argument — extract its method symbol
        IMethodSymbol? handlerMethod = null;
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value;
            while (value is IConversionOperation conv)
                value = conv.Operand;

            if (value is IDelegateCreationOperation delegateCreation)
            {
                var target = delegateCreation.Target;
                if (target is IAnonymousFunctionOperation anonFunc)
                    handlerMethod = anonFunc.Symbol;
                else if (target is IMethodReferenceOperation methodRef)
                    handlerMethod = methodRef.Method;
                break;
            }
        }

        if (handlerMethod is null)
            return;

        // Check each parameter for body-bound tenant entity
        foreach (var param in handlerMethod.Parameters)
        {
            var paramType = param.Type as INamedTypeSymbol;
            if (paramType is null)
                continue;

            if (!model.IsTenantScoped(paramType))
                continue;

            // In minimal APIs, complex types are body-bound by default for POST/PUT/PATCH.
            // Skip primitives, strings, CancellationToken, special types.
            if (param.Type.SpecialType != SpecialType.None)
                continue;
            if (param.Type.Name == "String" || param.Type.Name == "CancellationToken" ||
                param.Type.Name == "HttpContext" || param.Type.Name == "HttpRequest" ||
                param.Type.Name == "HttpResponse" || param.Type.Name == "ClaimsPrincipal")
                continue;
            if (param.Type.TypeKind == TypeKind.Enum)
                continue;

            var tenantPropertyName = model.TenantEntities.TryGetValue(paramType, out var info)
                ? info.TenantPropertyName
                : options.TenantPropertyNames.FirstOrDefault() ?? "TenantId";

            // Report on the handler argument in the MapPost call (more useful location)
            var location = param.Locations.FirstOrDefault()
                           ?? invocation.Syntax.GetLocation();

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.TL010,
                location,
                paramType.Name,
                tenantPropertyName));
        }
    }

    private static void AnalyzeDirectHeaderQueryAccess(
        OperationAnalysisContext context,
        ConfigOptions options,
        AspNetCoreTypes aspNetTypes)
    {
        var propertyRef = (IPropertyReferenceOperation)context.Operation;

        if (!propertyRef.Property.IsIndexer)
            return;

        var receiverType = propertyRef.Instance?.Type;
        if (receiverType is null)
            return;

        string? sourceLabel;
        bool isHeader;
        if (aspNetTypes.HeaderDictionary is not null &&
            DerivesFromOrImplements(receiverType, null, aspNetTypes.HeaderDictionary))
        {
            sourceLabel = "Request.Headers";
            isHeader = true;
        }
        else if (aspNetTypes.QueryCollection is not null &&
                 DerivesFromOrImplements(receiverType, null, aspNetTypes.QueryCollection))
        {
            sourceLabel = "Request.Query";
            isHeader = false;
        }
        else
        {
            return;
        }

        if (propertyRef.Arguments.Length == 0)
            return;

        var argOp = propertyRef.Arguments[0].Value;
        if (!argOp.ConstantValue.HasValue || argOp.ConstantValue.Value is not string key)
            return;

        var normalizedKey = isHeader ? NormalizeHeaderName(key) : key;
        if (!options.TenantPropertyNames.Contains(normalizedKey))
            return;

        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL004,
            propertyRef.Syntax.GetLocation(),
            key,
            sourceLabel));
    }

    private static void AnalyzeTryGetValueCall(
        OperationAnalysisContext context,
        ConfigOptions options,
        AspNetCoreTypes aspNetTypes)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (invocation.TargetMethod.Name != "TryGetValue")
            return;

        var receiverType = invocation.Instance?.Type;
        if (receiverType is null)
            return;

        string? sourceLabel;
        bool isHeader;
        if (aspNetTypes.HeaderDictionary is not null &&
            DerivesFromOrImplements(receiverType, null, aspNetTypes.HeaderDictionary))
        {
            sourceLabel = "Request.Headers";
            isHeader = true;
        }
        else if (aspNetTypes.QueryCollection is not null &&
                 DerivesFromOrImplements(receiverType, null, aspNetTypes.QueryCollection))
        {
            sourceLabel = "Request.Query";
            isHeader = false;
        }
        else
        {
            return;
        }

        if (invocation.Arguments.Length == 0)
            return;

        var keyArg = invocation.Arguments[0].Value;
        if (!keyArg.ConstantValue.HasValue || keyArg.ConstantValue.Value is not string key)
            return;

        var normalizedKey = isHeader ? NormalizeHeaderName(key) : key;
        if (!options.TenantPropertyNames.Contains(normalizedKey))
            return;

        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL004,
            invocation.Syntax.GetLocation(),
            key,
            sourceLabel));
    }

    private static string NormalizeHeaderName(string headerName)
    {
        // Strip X- prefix (case-insensitive)
        if (headerName.Length > 2 &&
            (headerName[0] == 'X' || headerName[0] == 'x') &&
            headerName[1] == '-')
        {
            headerName = headerName.Substring(2);
        }

        // Strip dashes
        if (headerName.IndexOf('-') >= 0)
            headerName = headerName.Replace("-", "");

        return headerName;
    }

    private static void AnalyzeCommandTextAssignment(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol? dbCommandType,
        INamedTypeSymbol? iDbCommandType)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        // Check if LHS is CommandText property
        if (assignment.Target is not IPropertyReferenceOperation propertyRef)
            return;

        if (propertyRef.Property.Name != "CommandText")
            return;

        // Check if the property's containing type derives from DbCommand or implements IDbCommand
        var receiverType = propertyRef.Instance?.Type;
        if (receiverType is null)
            return;

        if (!DerivesFromOrImplements(receiverType, dbCommandType, iDbCommandType))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Extract SQL text from RHS
        if (!SqlAnalyzer.TryExtractSqlFromOperation(assignment.Value, out var sqlText))
            return;

        var result = SqlAnalyzer.Analyze(sqlText, model, options);
        if (!result.HasTenantTableReference)
            return;

        var descriptor = result.HasTenantColumnInPredicate
            ? Descriptors.TL113
            : Descriptors.TL013;

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor, assignment.Syntax.GetLocation(),
            "CommandText", result.MatchedTableName, result.MatchedColumnName));
    }

    private static void AnalyzeDbCommandCreation(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol dbCommandType)
    {
        var creation = (IObjectCreationOperation)context.Operation;

        // Check if the created type derives from DbCommand
        var createdType = creation.Type as INamedTypeSymbol;
        if (createdType is null)
            return;

        if (!DerivesFrom(createdType, dbCommandType))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Find the string parameter (named "cmdText" or "commandText", or ordinal 0 if string)
        string? sqlText = null;
        foreach (var arg in creation.Arguments)
        {
            var paramName = arg.Parameter?.Name;
            if (paramName == "cmdText" || paramName == "commandText")
            {
                SqlAnalyzer.TryExtractSqlFromArgument(arg, out sqlText);
                break;
            }

            // Fallback: first string parameter at ordinal 0
            if (arg.Parameter?.Ordinal == 0 &&
                arg.Parameter.Type.SpecialType == SpecialType.System_String)
            {
                SqlAnalyzer.TryExtractSqlFromArgument(arg, out sqlText);
                break;
            }
        }

        if (sqlText is null || sqlText.Length == 0)
            return;

        var result = SqlAnalyzer.Analyze(sqlText, model, options);
        if (!result.HasTenantTableReference)
            return;

        var descriptor = result.HasTenantColumnInPredicate
            ? Descriptors.TL113
            : Descriptors.TL013;

        var typeName = createdType.Name;

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor, creation.Syntax.GetLocation(),
            typeName, result.MatchedTableName, result.MatchedColumnName));
    }

    private static bool DerivesFromOrImplements(
        ITypeSymbol type,
        INamedTypeSymbol? baseType,
        INamedTypeSymbol? interfaceType)
    {
        if (baseType is not null && DerivesFrom(type, baseType))
            return true;

        if (interfaceType is not null)
        {
            if (type.Equals(interfaceType, SymbolEqualityComparer.Default))
                return true;
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Equals(interfaceType, SymbolEqualityComparer.Default))
                    return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol baseType)
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

    private static bool IsDapperSqlMethod(string methodName) =>
        methodName.StartsWith("Query", StringComparison.Ordinal) ||
        methodName.StartsWith("Execute", StringComparison.Ordinal);

    private static bool IsExemptByCrossTenant(ISymbol? containingSymbol, string crossTenantAttributeName)
    {
        // Walk up to find enclosing method
        var current = containingSymbol;
        while (current is not null)
        {
            if (current is IMethodSymbol method)
            {
                if (HasCrossTenantAttribute(method, crossTenantAttributeName))
                    return true;

                // Check containing type
                if (method.ContainingType is not null &&
                    HasCrossTenantAttribute(method.ContainingType, crossTenantAttributeName))
                    return true;

                return false;
            }

            current = current.ContainingSymbol;
        }

        return false;
    }

    private static bool HasCrossTenantAttribute(ISymbol symbol, string crossTenantAttributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName is null)
                continue;

            if (string.Equals(attrName, crossTenantAttributeName, StringComparison.Ordinal) ||
                string.Equals(attrName, crossTenantAttributeName + "Attribute", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void AnalyzeMethodSymbol(SymbolAnalysisContext context, ConfigOptions options)
    {
        var method = (IMethodSymbol)context.Symbol;

        foreach (var attr in method.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName is null)
                continue;

            if (!IsCrossTenantAttributeName(attrName, options.CrossTenantAttribute))
                continue;

            // Check reason arg
            if (IsMissingOrPlaceholderReason(attr))
            {
                var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                               ?? method.Locations.FirstOrDefault()
                               ?? Location.None;

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL901,
                    location,
                    method.Name));
            }

            return;
        }
    }

    private static void AnalyzeNamedTypeSymbol(SymbolAnalysisContext context, ConfigOptions options)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        foreach (var attr in type.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName is null)
                continue;

            if (!IsCrossTenantAttributeName(attrName, options.CrossTenantAttribute))
                continue;

            // Report TL902 for class-level [CrossTenant]
            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                           ?? type.Locations.FirstOrDefault()
                           ?? Location.None;

            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.TL902,
                location,
                type.Name));

            // Also check reason for TL901
            if (IsMissingOrPlaceholderReason(attr))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL901,
                    location,
                    type.Name));
            }

            return;
        }
    }

    private static bool IsCrossTenantAttributeName(string attrName, string configuredName)
    {
        return string.Equals(attrName, configuredName, StringComparison.Ordinal) ||
               string.Equals(attrName, configuredName + "Attribute", StringComparison.Ordinal);
    }

    private static bool IsMissingOrPlaceholderReason(AttributeData attr)
    {
        // No constructor args → missing reason
        if (attr.ConstructorArguments.Length == 0)
            return true;

        var firstArg = attr.ConstructorArguments[0];
        if (firstArg.Value is not string reason)
            return true;

        return PlaceholderReasons.Contains(reason.Trim());
    }

    private static void AnalyzeActionParameterBinding(
        SymbolAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        AspNetCoreTypes aspNetTypes)
    {
        var method = (IMethodSymbol)context.Symbol;

        // Check [CrossTenant] exemption on method or containing type
        if (HasCrossTenantAttribute(method, options.CrossTenantAttribute))
            return;
        if (method.ContainingType is not null &&
            HasCrossTenantAttribute(method.ContainingType, options.CrossTenantAttribute))
            return;

        // Determine if the containing type is an [ApiController]
        var isApiController = aspNetTypes.ApiController is not null &&
                              method.ContainingType is not null &&
                              HasAttribute(method.ContainingType, aspNetTypes.ApiController);

        foreach (var param in method.Parameters)
        {
            // TL004/TL104: Check if parameter name matches tenant property names
            // and is bound from client input
            if (options.TenantPropertyNames.Contains(param.Name))
            {
                var bindingSource = GetClientInputBindingSource(param, aspNetTypes);
                if (bindingSource is not null)
                {
                    var location = param.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.TL004,
                        location,
                        param.Name,
                        bindingSource));
                    continue;
                }

                // TL104: FromRoute — Info severity (route tenants are a legitimate pattern)
                if (HasAttribute(param, aspNetTypes.FromRoute))
                {
                    var location = param.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.TL104,
                        location,
                        param.Name));
                    continue;
                }
            }

            // TL010: Check if parameter type is a tenant-scoped entity bound from body
            var paramType = param.Type as INamedTypeSymbol;
            if (paramType is null)
                continue;

            if (!model.IsTenantScoped(paramType))
                continue;

            // Get tenant property name for the message
            var tenantPropertyName = model.TenantEntities.TryGetValue(paramType, out var info)
                ? info.TenantPropertyName
                : options.TenantPropertyNames.FirstOrDefault() ?? "TenantId";

            // Check for explicit [FromBody] or [FromForm]
            if (HasAttribute(param, aspNetTypes.FromBody) ||
                HasAttribute(param, aspNetTypes.FromForm))
            {
                ReportTL010(context, param, paramType, tenantPropertyName);
                continue;
            }

            // Check for implicit body binding on [ApiController]
            if (isApiController && IsImplicitlyBodyBound(param, aspNetTypes))
            {
                ReportTL010(context, param, paramType, tenantPropertyName);
            }
        }
    }

    private static void ReportTL010(
        SymbolAnalysisContext context,
        IParameterSymbol param,
        INamedTypeSymbol paramType,
        string tenantPropertyName)
    {
        var location = param.Locations.FirstOrDefault() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL010,
            location,
            paramType.Name,
            tenantPropertyName));
    }

    private static string? GetClientInputBindingSource(IParameterSymbol param, AspNetCoreTypes aspNetTypes)
    {
        if (HasAttribute(param, aspNetTypes.FromHeader))
            return "FromHeader";
        if (HasAttribute(param, aspNetTypes.FromQuery))
            return "FromQuery";
        if (HasAttribute(param, aspNetTypes.FromForm))
            return "FromForm";
        // Note: FromRoute is handled separately as TL104 (Info severity)
        return null;
    }

    private static bool IsImplicitlyBodyBound(IParameterSymbol param, AspNetCoreTypes aspNetTypes)
    {
        // If the parameter has any explicit binding attribute, it's not implicitly body-bound
        if (HasAttribute(param, aspNetTypes.FromServices) ||
            HasAttribute(param, aspNetTypes.FromRoute) ||
            HasAttribute(param, aspNetTypes.FromQuery) ||
            HasAttribute(param, aspNetTypes.FromHeader) ||
            HasAttribute(param, aspNetTypes.FromBody) ||
            HasAttribute(param, aspNetTypes.FromForm))
            return false;

        // Primitives, strings, and CancellationToken are not implicitly body-bound
        var type = param.Type;
        if (type.SpecialType != SpecialType.None)
            return false;

        if (type.Name == "String" || type.Name == "CancellationToken")
            return false;

        if (type.TypeKind == TypeKind.Enum)
            return false;

        // Complex types without explicit binding are implicitly body-bound on [ApiController]
        return true;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
    {
        if (attributeType is null)
            return false;

        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass is not null &&
                attr.AttributeClass.Equals(attributeType, SymbolEqualityComparer.Default))
                return true;
        }

        return false;
    }

    private static bool HasCrossTenantAttributeInCompilation(
        Compilation compilation, string crossTenantAttributeName)
    {
        // Check for the attribute type in the compilation
        // Look for both "CrossTenant" and "CrossTenantAttribute" names
        var names = new[] { crossTenantAttributeName, crossTenantAttributeName + "Attribute" };
        foreach (var name in names)
        {
            foreach (var symbol in compilation.GetSymbolsWithName(name, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol namedType &&
                    namedType.BaseType?.Name == "Attribute")
                    return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------
    // TL201: #pragma warning disable TLxxx without justification
    // ---------------------------------------------------------------

    private static void AnalyzePragmaDirectives(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);

        foreach (var directive in root.DescendantTrivia()
                     .Where(t => t.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
                     .Select(t => t.GetStructure())
                     .OfType<PragmaWarningDirectiveTriviaSyntax>())
        {
            if (!directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                continue;

            foreach (var errorCode in directive.ErrorCodes)
            {
                var codeText = errorCode.ToString().Trim();
                if (!codeText.StartsWith("TL", StringComparison.Ordinal))
                    continue;

                // Don't flag suppression of TL201 itself (self-referential)
                if (codeText == "TL201")
                    continue;

                // Check for justification comment on the same line or previous line
                if (HasJustificationComment(directive, context.Tree))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL201,
                    directive.GetLocation(),
                    codeText));
                break; // One report per pragma line is enough
            }
        }
    }

    private static bool HasJustificationComment(PragmaWarningDirectiveTriviaSyntax directive, SyntaxTree tree)
    {
        var directiveSpan = directive.SyntaxTree.GetLineSpan(directive.Span);
        var directiveLine = directiveSpan.StartLinePosition.Line;
        var text = tree.GetText();

        // Check same line for trailing comment
        if (directiveLine < text.Lines.Count)
        {
            var line = text.Lines[directiveLine];
            var lineText = line.ToString();
            var commentIndex = lineText.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                var comment = lineText.Substring(commentIndex + 2).Trim();
                if (!IsPlaceholderComment(comment))
                    return true;
            }
        }

        // Check previous line for comment
        if (directiveLine > 0)
        {
            var prevLine = text.Lines[directiveLine - 1];
            var prevText = prevLine.ToString().Trim();
            if (prevText.StartsWith("//", StringComparison.Ordinal))
            {
                var comment = prevText.Substring(2).Trim();
                if (!IsPlaceholderComment(comment))
                    return true;
            }
        }

        return false;
    }

    private static bool IsPlaceholderComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return true;

        return PlaceholderReasons.Contains(comment) ||
               comment.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
               comment.Equals("temporary", StringComparison.OrdinalIgnoreCase) ||
               comment.Equals("fixme", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // TL202: [SuppressMessage] targeting TL rules without justification
    // ---------------------------------------------------------------

    private static void AnalyzeSuppressMessageAttribute(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        // Skip assembly-level attributes
        if (symbol is IAssemblySymbol)
            return;

        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass is null)
                continue;

            if (attr.AttributeClass.Name != "SuppressMessageAttribute" &&
                attr.AttributeClass.Name != "SuppressMessage")
                continue;

            if (attr.AttributeClass.ContainingNamespace?.ToDisplayString() !=
                "System.Diagnostics.CodeAnalysis")
                continue;

            // Check if targeting TL rules
            if (attr.ConstructorArguments.Length < 2)
                continue;

            var category = attr.ConstructorArguments[0].Value as string ?? "";
            var checkId = attr.ConstructorArguments[1].Value as string ?? "";

            if (!category.Equals("TenantIsolation", StringComparison.OrdinalIgnoreCase) &&
                !checkId.StartsWith("TL", StringComparison.Ordinal))
                continue;

            // Check Justification named argument
            string? justification = null;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Justification")
                {
                    justification = namedArg.Value.Value as string;
                    break;
                }
            }

            if (justification is null || IsPlaceholderComment(justification))
            {
                var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                               ?? symbol.Locations.FirstOrDefault()
                               ?? Location.None;

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL202,
                    location,
                    checkId));
            }
        }
    }

    // ---------------------------------------------------------------
    // TL203: TL rules globally disabled via NoWarn or .editorconfig
    // ---------------------------------------------------------------

    private static void AnalyzeGlobalSuppressions(
        CompilationAnalysisContext context,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var suppressedIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check NoWarn in compilation options
        foreach (var kvp in context.Compilation.Options.SpecificDiagnosticOptions)
        {
            if (kvp.Key.StartsWith("TL", StringComparison.Ordinal) &&
                kvp.Value == ReportDiagnostic.Suppress &&
                !TL203IgnoredIds.Contains(kvp.Key))
            {
                suppressedIds.Add(kvp.Key);
            }
        }

        // Check .editorconfig severity overrides per syntax tree
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var treeOptions = optionsProvider.GetOptions(tree);

            // Check TL rule IDs: TL000-TL999 and TL200 range
            foreach (var id in KnownTlRuleIds)
            {
                if (!TL203IgnoredIds.Contains(id) &&
                    treeOptions.TryGetValue($"dotnet_diagnostic.{id}.severity", out var severity) &&
                    string.Equals(severity, "none", StringComparison.OrdinalIgnoreCase))
                {
                    suppressedIds.Add(id);
                }
            }

            break; // Only need to check one tree (project-wide editorconfig)
        }

        if (suppressedIds.Count == 0)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL203,
            Location.None,
            string.Join(", ", suppressedIds)));
    }

    private static readonly ImmutableArray<string> KnownTlRuleIds = ImmutableArray.Create(
        "TL001", "TL002", "TL003", "TL004", "TL005", "TL006", "TL007", "TL008", "TL009",
        "TL010", "TL011", "TL012", "TL013", "TL014", "TL015",
        "TL102", "TL103", "TL104", "TL105", "TL113",
        "TL201", "TL202",
        "TL900", "TL901", "TL902");

    // Rules that are isEnabledByDefault: false — suppressing these is not a concern
    // because they are off by default. TL203 should not flag them.
    // Rules that are isEnabledByDefault: false or are meta-rules (TL201–TL203)
    // that should not trigger TL203 when suppressed in SpecificDiagnosticOptions.
    // IDs that TL203 should not flag when suppressed: disabled-by-default rules
    // and anti-tamper meta-rules (TL201–TL203) to avoid self-referential loops.
    private static readonly ImmutableHashSet<string> TL203IgnoredIds =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "TL000", "TL111", "TL201", "TL202", "TL203");

    // ---------------------------------------------------------------
    // TL014: ABP tenant-filter bypass
    // ---------------------------------------------------------------

    private static void AnalyzeAbpBypass(
        OperationAnalysisContext context,
        ConfigOptions options,
        INamedTypeSymbol? abpDataFilter,
        INamedTypeSymbol? abpCurrentTenant,
        INamedTypeSymbol? abpIMultiTenant)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Check IDataFilter.Disable<IMultiTenant>()
        if (abpDataFilter is not null && abpIMultiTenant is not null &&
            targetMethod.Name == "Disable")
        {
            var containingType = targetMethod.ContainingType;
            if (containingType is null)
                goto checkCurrentTenant;

            // IDataFilter.Disable<T>() — check T is IMultiTenant
            if (containingType.Equals(abpDataFilter, SymbolEqualityComparer.Default) &&
                targetMethod.TypeArguments.Length == 1 &&
                targetMethod.TypeArguments[0].Equals(abpIMultiTenant, SymbolEqualityComparer.Default))
            {
                ReportAbpBypass(context, invocation, "IDataFilter.Disable<IMultiTenant>");
                return;
            }

            // IDataFilter<IMultiTenant>.Disable()
            if (containingType.IsGenericType &&
                containingType.OriginalDefinition.ToDisplayString() == "Volo.Abp.Data.IDataFilter<TFilter>" &&
                containingType.TypeArguments.Length == 1 &&
                containingType.TypeArguments[0].Equals(abpIMultiTenant, SymbolEqualityComparer.Default))
            {
                ReportAbpBypass(context, invocation, "IDataFilter<IMultiTenant>.Disable");
                return;
            }
        }

        checkCurrentTenant:
        // Check ICurrentTenant.Change()
        if (abpCurrentTenant is not null && targetMethod.Name == "Change")
        {
            var containingType = targetMethod.ContainingType;
            if (containingType is not null &&
                containingType.Equals(abpCurrentTenant, SymbolEqualityComparer.Default))
            {
                var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
                    && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                        ? memberAccess.Name.GetLocation()
                        : invocation.Syntax.GetLocation();

                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL014Change,
                    location));
                return;
            }
        }
    }

    private static void ReportAbpBypass(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        string methodDescription)
    {
        var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
            && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL014,
            location,
            methodDescription));
    }

    // ---------------------------------------------------------------
    // TL015: Finbuckle TenantMismatchMode.Ignore
    // ---------------------------------------------------------------

    private static void AnalyzeFinbuckleMismatchMode(
        OperationAnalysisContext context,
        ConfigOptions options,
        INamedTypeSymbol tenantMismatchModeType)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        // Check if RHS is TenantMismatchMode.Ignore
        var value = assignment.Value;
        while (value is IConversionOperation conv)
            value = conv.Operand;

        if (!value.ConstantValue.HasValue)
            return;

        if (value is not IFieldReferenceOperation fieldRef)
            return;

        if (!fieldRef.Field.ContainingType.Equals(tenantMismatchModeType, SymbolEqualityComparer.Default))
            return;

        if (fieldRef.Field.Name != "Ignore")
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL015,
            assignment.Syntax.GetLocation()));
    }

    // ---------------------------------------------------------------
    // TL007: Static field/property with tenant state
    // ---------------------------------------------------------------

    private static readonly ImmutableHashSet<string> AsyncLocalBackedTypes =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "AsyncLocal",
            "IMultiTenantContextAccessor",
            "IHttpContextAccessor");

    private static void AnalyzeStaticTenantField(SymbolAnalysisContext context, ConfigOptions options)
    {
        var symbol = context.Symbol;
        bool isStatic;
        ITypeSymbol? fieldType;
        string fieldName;

        if (symbol is IFieldSymbol field)
        {
            // Enum fields are inherently static but are not tenant state
            if (field.ContainingType?.TypeKind == TypeKind.Enum)
                return;

            // Constants are not mutable tenant state
            if (field.IsConst)
                return;

            isStatic = field.IsStatic;
            fieldType = field.Type;
            fieldName = field.Name;
        }
        else if (symbol is IPropertySymbol property)
        {
            isStatic = property.IsStatic;
            fieldType = property.Type;
            fieldName = property.Name;
        }
        else
        {
            return;
        }

        if (!isStatic)
            return;

        // Check if the type is AsyncLocal-backed (safe)
        var typeName = fieldType.Name;
        if (AsyncLocalBackedTypes.Contains(typeName))
            return;

        // Check the full type name for Finbuckle accessor
        var fullTypeName = fieldType.ToDisplayString();
        if (fullTypeName.StartsWith("Finbuckle.MultiTenant.IMultiTenantContextAccessor", StringComparison.Ordinal) ||
            fullTypeName.StartsWith("Microsoft.AspNetCore.Http.IHttpContextAccessor", StringComparison.Ordinal))
            return;

        // Check if the type name or field name relates to tenant
        var isTenantRelated = false;

        if (typeName.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0)
            isTenantRelated = true;

        if (!isTenantRelated && fieldName.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0)
            isTenantRelated = true;

        if (!isTenantRelated)
        {
            foreach (var propName in options.TenantPropertyNames)
            {
                if (fieldName.Equals(propName, StringComparison.OrdinalIgnoreCase) ||
                    typeName.IndexOf(propName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isTenantRelated = true;
                    break;
                }
            }
        }

        if (!isTenantRelated)
            return;

        var location = symbol.Locations.FirstOrDefault() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL007,
            location,
            fieldName));
    }

    // ---------------------------------------------------------------
    // TL006: Cache key without tenant context
    // ---------------------------------------------------------------

    private static readonly ImmutableHashSet<string> MemoryCacheMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "Get", "Set", "TryGetValue", "GetOrCreate", "GetOrCreateAsync");

    private static readonly ImmutableHashSet<string> DistributedCacheMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "Get", "GetAsync", "Set", "SetAsync",
            "GetString", "GetStringAsync", "SetString", "SetStringAsync");

    private static readonly ImmutableHashSet<string> HybridCacheMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "GetOrCreateAsync", "SetAsync", "RemoveAsync");

    private static void AnalyzeCacheKeyInvocation(
        OperationAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol? memoryCacheType,
        INamedTypeSymbol? distributedCacheType,
        INamedTypeSymbol? hybridCacheType)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Determine if this is a cache method call
        bool isCacheCall = false;
        var receiverType = invocation.Instance?.Type ?? targetMethod.ContainingType;
        if (receiverType is null)
            return;

        if (memoryCacheType is not null &&
            MemoryCacheMethods.Contains(targetMethod.Name) &&
            ImplementsInterface(receiverType, memoryCacheType))
            isCacheCall = true;

        if (!isCacheCall && distributedCacheType is not null &&
            DistributedCacheMethods.Contains(targetMethod.Name) &&
            ImplementsInterface(receiverType, distributedCacheType))
            isCacheCall = true;

        if (!isCacheCall && hybridCacheType is not null &&
            HybridCacheMethods.Contains(targetMethod.Name) &&
            DerivesFromOrImplements(receiverType, hybridCacheType, null))
            isCacheCall = true;

        if (!isCacheCall)
            return;

        // Extract key argument (first parameter)
        if (invocation.Arguments.Length == 0)
            return;

        var keyArg = invocation.Arguments[0];
        if (keyArg.Parameter?.Type?.SpecialType != SpecialType.System_String)
            return;

        // Check if the key includes tenant context
        var keyValue = keyArg.Value;
        while (keyValue is IConversionOperation conv)
            keyValue = conv.Operand;

        if (KeyIncludesTenantContext(keyValue, options))
            return;

        // Non-constant key — skip (can't analyze)
        if (!keyValue.ConstantValue.HasValue &&
            keyValue is not IInterpolatedStringOperation)
            return;

        // Check if the method also touches tenant-scoped data
        if (!MethodTouchesTenantData(context.ContainingSymbol, model))
            return;

        var keyText = keyValue.ConstantValue.HasValue
            ? keyValue.ConstantValue.Value?.ToString() ?? "<key>"
            : ExtractInterpolationText(keyValue);

        var location = keyArg.Syntax?.GetLocation() ?? invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL006,
            location,
            keyText));
    }

    private static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol interfaceType)
    {
        if (type.Equals(interfaceType, SymbolEqualityComparer.Default))
            return true;
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Equals(interfaceType, SymbolEqualityComparer.Default))
                return true;
        }
        return false;
    }

    private static bool KeyIncludesTenantContext(IOperation keyOp, ConfigOptions options)
    {
        // For interpolated strings, check if any expression references tenant
        if (keyOp is IInterpolatedStringOperation interpolated)
        {
            foreach (var part in interpolated.Parts)
            {
                if (part is IInterpolationOperation interpolation)
                {
                    if (ReferencesTenanValue(interpolation.Expression, options))
                        return true;
                }
            }
            return false;
        }

        return false;
    }

    private static bool ReferencesTenanValue(IOperation operation, ConfigOptions options)
    {
        // Check if the expression references a member with tenant-related name
        if (operation is IMemberReferenceOperation memberRef)
        {
            var memberName = memberRef.Member.Name;
            foreach (var propName in options.TenantPropertyNames)
            {
                if (memberName.IndexOf(propName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (memberName.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (operation is ILocalReferenceOperation localRef)
        {
            var localName = localRef.Local.Name;
            foreach (var propName in options.TenantPropertyNames)
            {
                if (localName.IndexOf(propName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (localName.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        if (operation is IParameterReferenceOperation paramRef)
        {
            var paramName = paramRef.Parameter.Name;
            foreach (var propName in options.TenantPropertyNames)
            {
                if (paramName.IndexOf(propName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (paramName.IndexOf("Tenant", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string ExtractInterpolationText(IOperation keyOp)
    {
        if (keyOp is IInterpolatedStringOperation interpolated)
        {
            var parts = new System.Text.StringBuilder();
            foreach (var part in interpolated.Parts)
            {
                if (part is IInterpolatedStringTextOperation textPart &&
                    textPart.ConstantValue.HasValue)
                    parts.Append(textPart.ConstantValue.Value);
                else
                    parts.Append("{...}");
            }
            return parts.ToString();
        }
        return "<key>";
    }

    private static bool MethodTouchesTenantData(ISymbol? containingSymbol, TenantModel model)
    {
        // Walk up to find the enclosing method
        var method = containingSymbol;
        while (method is not null && method is not IMethodSymbol)
            method = method.ContainingSymbol;

        if (method is not IMethodSymbol methodSymbol)
            return false;

        // Check if the method's containing type has any DbSet<T> properties where T is tenant-scoped
        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return false;

        // Check the type's fields/properties for DbContext references
        foreach (var member in containingType.GetMembers())
        {
            ITypeSymbol? memberType = null;
            if (member is IFieldSymbol f)
                memberType = f.Type;
            else if (member is IPropertySymbol p)
                memberType = p.Type;

            if (memberType is null)
                continue;

            // Check if the type is a DbContext with tenant entities
            if (IsDbContextWithTenantEntities(memberType, model))
                return true;
        }

        // Also check method parameters
        foreach (var param in methodSymbol.Parameters)
        {
            if (IsDbContextWithTenantEntities(param.Type, model))
                return true;
        }

        return false;
    }

    private static bool IsDbContextWithTenantEntities(ITypeSymbol type, TenantModel model)
    {
        // Check if any of the model's tenant entities have this as their DbContext type
        var namedType = type as INamedTypeSymbol;
        if (namedType is null)
            return false;

        foreach (var kvp in model.TenantEntities)
        {
            if (SymbolEqualityComparer.Default.Equals(kvp.Value.DbContextType, namedType))
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------
    // TL009: RLS session variable leak
    // ---------------------------------------------------------------

    private static readonly Regex SetSessionVarPattern = new(
        @"\bSET\s+(?!LOCAL\b)(\w+\.\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SetConfigFalsePattern = new(
        @"\bset_config\s*\([^,]+,\s*[^,]+,\s*false\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void AnalyzeRlsSessionVariable(
        OperationAnalysisContext context,
        ConfigOptions options,
        INamedTypeSymbol relationalDbFacadeExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = invocation.TargetMethod;

        if (!RawSqlMethodNames.Contains(targetMethod.Name))
            return;

        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        var containingType = originalMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(relationalDbFacadeExtensions, SymbolEqualityComparer.Default))
            return;

        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        if (!SqlAnalyzer.TryExtractSqlFromInvocation(invocation, "sql", out var sqlText))
            return;

        ReportRlsIfNeeded(context, invocation.Syntax, sqlText);
    }

    private static void AnalyzeRlsCommandText(
        OperationAnalysisContext context,
        ConfigOptions options,
        INamedTypeSymbol? dbCommandType,
        INamedTypeSymbol? iDbCommandType)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;

        if (assignment.Target is not IPropertyReferenceOperation propertyRef)
            return;

        if (propertyRef.Property.Name != "CommandText")
            return;

        var receiverType = propertyRef.Instance?.Type;
        if (receiverType is null)
            return;

        if (!DerivesFromOrImplements(receiverType, dbCommandType, iDbCommandType))
            return;

        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        if (!SqlAnalyzer.TryExtractSqlFromOperation(assignment.Value, out var sqlText))
            return;

        ReportRlsIfNeeded(context, assignment.Syntax, sqlText);
    }

    private static void ReportRlsIfNeeded(OperationAnalysisContext context, SyntaxNode syntax, string sqlText)
    {
        // Check for SET without LOCAL
        var setMatch = SetSessionVarPattern.Match(sqlText);
        if (setMatch.Success)
        {
            // Make sure it's not SET LOCAL, SET TRANSACTION, etc.
            var before = sqlText.Substring(0, setMatch.Index + 3);
            if (!before.EndsWith("SET", StringComparison.OrdinalIgnoreCase))
            {
                // Probably part of a larger word
            }
            else
            {
                var afterSet = sqlText.Substring(setMatch.Index + 3).TrimStart();
                if (!afterSet.StartsWith("LOCAL", StringComparison.OrdinalIgnoreCase) &&
                    !afterSet.StartsWith("TRANSACTION", StringComparison.OrdinalIgnoreCase))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.TL009,
                        syntax.GetLocation(),
                        "SET " + setMatch.Groups[1].Value));
                    return;
                }
            }
        }

        // Check for set_config(..., false)
        if (SetConfigFalsePattern.IsMatch(sqlText))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.TL009,
                syntax.GetLocation(),
                "set_config(..., false)"));
        }
    }

    // ---------------------------------------------------------------
    // TL008: Background service without tenant scope
    // ---------------------------------------------------------------

    private static void AnalyzeBackgroundService(
        SymbolAnalysisContext context,
        TenantModel model,
        ConfigOptions options,
        INamedTypeSymbol? backgroundServiceType,
        INamedTypeSymbol? hostedServiceType)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Check [CrossTenant] exemption on the class
        if (HasCrossTenantAttribute(type, options.CrossTenantAttribute))
            return;

        // Check if this type is a BackgroundService or IHostedService
        var isBackground = false;
        if (backgroundServiceType is not null && DerivesFrom(type, backgroundServiceType))
            isBackground = true;

        if (!isBackground && hostedServiceType is not null)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Equals(hostedServiceType, SymbolEqualityComparer.Default))
                {
                    isBackground = true;
                    break;
                }
            }
        }

        if (!isBackground)
            return;

        // Check if the type has a DbContext field/parameter with tenant entities
        var hasDbContext = false;
        var hasScope = false;
        var hasTenantSet = false;

        foreach (var member in type.GetMembers())
        {
            ITypeSymbol? memberType = null;
            if (member is IFieldSymbol f)
                memberType = f.Type;
            else if (member is IPropertySymbol p)
                memberType = p.Type;

            if (memberType is null)
                continue;

            if (IsDbContextWithTenantEntities(memberType, model))
                hasDbContext = true;

            // Check if it has IServiceScopeFactory
            if (memberType.Name == "IServiceScopeFactory" || memberType.Name == "IServiceProvider")
                hasScope = true;

            // Check for tenant-setting services
            if (memberType.Name == "ICurrentTenant" ||
                memberType.Name.Contains("TenantContext") ||
                memberType.Name.Contains("TenantAccessor"))
                hasTenantSet = true;
        }

        if (!hasDbContext)
            return;

        if (hasScope || hasTenantSet)
            return;

        var location = type.Locations.FirstOrDefault() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.TL008,
            location,
            type.Name));
    }
}

internal sealed class AspNetCoreTypes
{
    public INamedTypeSymbol? FromBody { get; }
    public INamedTypeSymbol? FromForm { get; }
    public INamedTypeSymbol? ApiController { get; }
    public INamedTypeSymbol? FromServices { get; }
    public INamedTypeSymbol? FromRoute { get; }
    public INamedTypeSymbol? FromQuery { get; }
    public INamedTypeSymbol? FromHeader { get; }
    public INamedTypeSymbol? HeaderDictionary { get; }
    public INamedTypeSymbol? QueryCollection { get; }

    public bool HasAny =>
        FromBody is not null || FromForm is not null || ApiController is not null;

    public bool HasHeaderOrQuery =>
        HeaderDictionary is not null || QueryCollection is not null;

    private AspNetCoreTypes(
        INamedTypeSymbol? fromBody,
        INamedTypeSymbol? fromForm,
        INamedTypeSymbol? apiController,
        INamedTypeSymbol? fromServices,
        INamedTypeSymbol? fromRoute,
        INamedTypeSymbol? fromQuery,
        INamedTypeSymbol? fromHeader,
        INamedTypeSymbol? headerDictionary,
        INamedTypeSymbol? queryCollection)
    {
        FromBody = fromBody;
        FromForm = fromForm;
        ApiController = apiController;
        FromServices = fromServices;
        FromRoute = fromRoute;
        FromQuery = fromQuery;
        FromHeader = fromHeader;
        HeaderDictionary = headerDictionary;
        QueryCollection = queryCollection;
    }

    public static AspNetCoreTypes Resolve(Compilation compilation)
    {
        return new AspNetCoreTypes(
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromFormAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ApiControllerAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromRouteAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IHeaderDictionary"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IQueryCollection"));
    }
}

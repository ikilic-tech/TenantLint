using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
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
            Descriptors.TL005,
            Descriptors.TL105,
            Descriptors.TL010,
            Descriptors.TL011,
            Descriptors.TL111,
            Descriptors.TL012,
            Descriptors.TL013,
            Descriptors.TL113,
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
            // TL004: Check if parameter name matches tenant property names
            // and is bound from client input ([FromHeader], [FromQuery], [FromRoute], [FromForm])
            var bindingSource = GetClientInputBindingSource(param, aspNetTypes);
            if (bindingSource is not null &&
                options.TenantPropertyNames.Contains(param.Name))
            {
                var location = param.Locations.FirstOrDefault() ?? Location.None;
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.TL004,
                    location,
                    param.Name,
                    bindingSource));
                continue;
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
        if (HasAttribute(param, aspNetTypes.FromRoute))
            return "FromRoute";
        if (HasAttribute(param, aspNetTypes.FromForm))
            return "FromForm";
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

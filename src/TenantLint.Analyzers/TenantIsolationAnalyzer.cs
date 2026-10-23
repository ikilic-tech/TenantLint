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
            Descriptors.TL005,
            Descriptors.TL105,
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

            // TL900: Report dynamic filter contexts (CompilationEnd — no code fix needed)
            if (model.DynamicFilterContexts.Count > 0)
            {
                compilationStartContext.RegisterCompilationEndAction(endContext =>
                {
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

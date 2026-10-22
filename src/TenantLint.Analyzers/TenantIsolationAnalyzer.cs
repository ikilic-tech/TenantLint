using System.Collections.Immutable;
using System.Linq;
using System.Text;
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
            Descriptors.TL002Filtered,
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

            // TL001 + TL002: Register single invocation operation action
            var relationalDbFacadeExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions");

            if (efQueryableExtensions is not null || relationalDbFacadeExtensions is not null)
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

            // Report TL900 for each DbContext with dynamic filters
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

        // Check this is RelationalDatabaseFacadeExtensions (extension method).
        // For methods taking FormattableString, the compiler may resolve
        // the extension method differently — check both ReducedFrom and original.
        var originalMethod = targetMethod.ReducedFrom ?? targetMethod;
        var containingType = originalMethod.ContainingType;
        if (containingType is null ||
            !containingType.Equals(relationalDbFacadeExtensions, SymbolEqualityComparer.Default))
            return;

        // Check [CrossTenant] exemption
        if (IsExemptByCrossTenant(context.ContainingSymbol, options.CrossTenantAttribute))
            return;

        // Extract SQL text
        if (!TryExtractSqlText(invocation, out var sqlText))
            return;

        // Analyze the SQL
        var result = SqlAnalyzer.Analyze(sqlText, model, options);
        if (!result.HasTenantTableReference)
            return;

        // Choose descriptor based on predicate heuristic
        var descriptor = result.HasTenantColumnInPredicate
            ? Descriptors.TL002Filtered
            : Descriptors.TL002;

        // Narrow location to the method name token
        var location = invocation.Syntax is InvocationExpressionSyntax invocationSyntax
            && invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Name.GetLocation()
                : invocation.Syntax.GetLocation();

        // When tenant column appears in predicate, downgrade to Info
        var effectiveSeverity = result.HasTenantColumnInPredicate
            ? DiagnosticSeverity.Info
            : (DiagnosticSeverity?)null;

        var diagnostic = effectiveSeverity.HasValue
            ? Diagnostic.Create(
                descriptor,
                location,
                effectiveSeverity.Value,
                additionalLocations: null,
                properties: null,
                targetMethod.Name,
                result.MatchedTableName,
                result.MatchedColumnName)
            : Diagnostic.Create(
                descriptor,
                location,
                targetMethod.Name,
                result.MatchedTableName,
                result.MatchedColumnName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool TryExtractSqlText(IInvocationOperation invocation, out string sqlText)
    {
        sqlText = "";

        // Find the sql parameter argument
        IArgumentOperation? sqlArg = null;
        foreach (var arg in invocation.Arguments)
        {
            if (arg.Parameter?.Name == "sql")
            {
                sqlArg = arg;
                break;
            }
        }

        if (sqlArg is null)
            return false;

        // Try IOperation-based extraction first
        if (TryExtractSqlFromOperation(sqlArg.Value, out sqlText))
            return true;

        // Fallback: extract from syntax (handles cases where IOperation lowers
        // interpolated strings to FormattableStringFactory.Create or other patterns)
        return TryExtractSqlFromSyntax(sqlArg, out sqlText);
    }

    private static bool TryExtractSqlFromOperation(IOperation operation, out string sqlText)
    {
        sqlText = "";

        // Unwrap conversion operations
        var value = operation;
        while (value is IConversionOperation conversion)
            value = conversion.Operand;

        // Check for constant string
        if (value.ConstantValue.HasValue && value.ConstantValue.Value is string constantSql)
        {
            sqlText = constantSql;
            return true;
        }

        // Check for interpolated string
        if (value is IInterpolatedStringOperation interpolated)
        {
            return TryBuildSqlFromInterpolation(interpolated, out sqlText);
        }

        // Handle FormattableString lowering: FormattableStringFactory.Create(format, args)
        if (value is IInvocationOperation factoryCall &&
            factoryCall.TargetMethod.Name == "Create" &&
            factoryCall.TargetMethod.ContainingType?.Name == "FormattableStringFactory" &&
            factoryCall.Arguments.Length >= 1)
        {
            var formatArg = factoryCall.Arguments[0].Value;
            while (formatArg is IConversionOperation formatConversion)
                formatArg = formatConversion.Operand;

            if (formatArg.ConstantValue.HasValue && formatArg.ConstantValue.Value is string format)
            {
                sqlText = format;
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildSqlFromInterpolation(IInterpolatedStringOperation interpolated, out string sqlText)
    {
        var sb = new StringBuilder();
        var placeholderIndex = 0;
        foreach (var part in interpolated.Parts)
        {
            if (part is IInterpolatedStringTextOperation textPart)
            {
                if (textPart.ConstantValue.HasValue && textPart.ConstantValue.Value is string text)
                    sb.Append(text);
            }
            else
            {
                sb.Append('{').Append(placeholderIndex++).Append('}');
            }
        }

        sqlText = sb.ToString();
        return sqlText.Length > 0;
    }

    private static bool TryExtractSqlFromSyntax(IArgumentOperation argument, out string sqlText)
    {
        sqlText = "";

        // Walk the syntax tree to find an interpolated string expression
        var syntax = argument.Syntax;
        if (syntax is null)
            return false;

        // Find the interpolated string in the argument's syntax subtree
        var interpolatedSyntax = FindDescendantOrSelf<InterpolatedStringExpressionSyntax>(syntax);
        if (interpolatedSyntax is not null)
        {
            var sb = new StringBuilder();
            var placeholderIndex = 0;
            foreach (var content in interpolatedSyntax.Contents)
            {
                if (content is InterpolatedStringTextSyntax textPart)
                {
                    sb.Append(textPart.TextToken.ValueText);
                }
                else if (content is InterpolationSyntax)
                {
                    sb.Append('{').Append(placeholderIndex++).Append('}');
                }
            }

            sqlText = sb.ToString();
            return sqlText.Length > 0;
        }

        // Check for a constant string in syntax (e.g., string literal argument)
        var literalSyntax = FindDescendantOrSelf<LiteralExpressionSyntax>(syntax);
        if (literalSyntax is not null && literalSyntax.Token.Value is string literalText)
        {
            sqlText = literalText;
            return true;
        }

        return false;
    }

    private static T? FindDescendantOrSelf<T>(SyntaxNode node) where T : SyntaxNode
    {
        if (node is T match)
            return match;

        foreach (var child in node.DescendantNodes())
        {
            if (child is T childMatch)
                return childMatch;
        }

        return null;
    }

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

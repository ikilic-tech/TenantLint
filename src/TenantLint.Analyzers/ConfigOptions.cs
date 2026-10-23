using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TenantLint.Analyzers;

internal sealed class ConfigOptions
{
    private static readonly ImmutableHashSet<string> DefaultTenantPropertyNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "TenantId");

    public ImmutableHashSet<string> TenantPropertyNames { get; }
    public ImmutableHashSet<string> TenantColumnNames { get; }
    public ImmutableHashSet<string> GlobalEntities { get; }
    public string? TenantFilterName { get; }
    public string CrossTenantAttribute { get; }
    public ImmutableHashSet<string> FilteredBaseTypes { get; }
    public ImmutableHashSet<string> TenantMarkerInterfaces { get; }

    private ConfigOptions(
        ImmutableHashSet<string> tenantPropertyNames,
        ImmutableHashSet<string> tenantColumnNames,
        ImmutableHashSet<string> globalEntities,
        string? tenantFilterName,
        string crossTenantAttribute,
        ImmutableHashSet<string> filteredBaseTypes,
        ImmutableHashSet<string> tenantMarkerInterfaces)
    {
        TenantPropertyNames = tenantPropertyNames;
        TenantColumnNames = tenantColumnNames;
        GlobalEntities = globalEntities;
        TenantFilterName = tenantFilterName;
        CrossTenantAttribute = crossTenantAttribute;
        FilteredBaseTypes = filteredBaseTypes;
        TenantMarkerInterfaces = tenantMarkerInterfaces;
    }

    public static ConfigOptions Create(
        AnalyzerConfigOptionsProvider optionsProvider,
        Compilation compilation)
    {
        // Read from first syntax tree's analyzer config (project-wide .editorconfig)
        AnalyzerConfigOptions options;
        var trees = compilation.SyntaxTrees;
        if (trees.Any())
        {
            options = optionsProvider.GetOptions(trees.First());
        }
        else
        {
            options = optionsProvider.GlobalOptions;
        }

        var tenantPropertyNames = ParseCsvOption(
            options,
            "tenantlint.tenant_property_names",
            DefaultTenantPropertyNames,
            StringComparer.OrdinalIgnoreCase);

        var explicitColumnNames = ParseCsvOptionOrNull(
            options,
            "tenantlint.tenant_column_names",
            StringComparer.OrdinalIgnoreCase);

        var tenantColumnNames = explicitColumnNames ?? DeriveColumnNames(tenantPropertyNames);

        var globalEntities = ParseCsvOption(
            options,
            "tenantlint.global_entities",
            ImmutableHashSet<string>.Empty,
            StringComparer.OrdinalIgnoreCase);

        var tenantFilterName = ParseStringOption(options, "tenantlint.tenant_filter_name");

        var crossTenantAttribute = ParseStringOption(options, "tenantlint.cross_tenant_attribute")
                                   ?? "CrossTenant";

        var filteredBaseTypes = ParseCsvOption(
            options,
            "tenantlint.filtered_base_types",
            ImmutableHashSet<string>.Empty,
            StringComparer.OrdinalIgnoreCase);

        var tenantMarkerInterfaces = ParseCsvOption(
            options,
            "tenantlint.tenant_marker_interfaces",
            ImmutableHashSet<string>.Empty,
            StringComparer.OrdinalIgnoreCase);

        return new ConfigOptions(
            tenantPropertyNames,
            tenantColumnNames,
            globalEntities,
            tenantFilterName,
            crossTenantAttribute,
            filteredBaseTypes,
            tenantMarkerInterfaces);
    }

    internal static ConfigOptions CreateDefault()
    {
        return new ConfigOptions(
            DefaultTenantPropertyNames,
            DeriveColumnNames(DefaultTenantPropertyNames),
            ImmutableHashSet<string>.Empty,
            tenantFilterName: null,
            crossTenantAttribute: "CrossTenant",
            filteredBaseTypes: ImmutableHashSet<string>.Empty,
            tenantMarkerInterfaces: ImmutableHashSet<string>.Empty);
    }

    internal static ConfigOptions CreateWithPropertyNames(params string[] propertyNames)
    {
        var names = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, propertyNames);
        return new ConfigOptions(
            names,
            DeriveColumnNames(names),
            ImmutableHashSet<string>.Empty,
            tenantFilterName: null,
            crossTenantAttribute: "CrossTenant",
            filteredBaseTypes: ImmutableHashSet<string>.Empty,
            tenantMarkerInterfaces: ImmutableHashSet<string>.Empty);
    }

    internal static ConfigOptions CreateWithFilteredBaseTypes(params string[] baseTypes)
    {
        return new ConfigOptions(
            DefaultTenantPropertyNames,
            DeriveColumnNames(DefaultTenantPropertyNames),
            ImmutableHashSet<string>.Empty,
            tenantFilterName: null,
            crossTenantAttribute: "CrossTenant",
            filteredBaseTypes: ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, baseTypes),
            tenantMarkerInterfaces: ImmutableHashSet<string>.Empty);
    }

    internal static ConfigOptions CreateWithTenantMarkerInterfaces(params string[] interfaces)
    {
        return new ConfigOptions(
            DefaultTenantPropertyNames,
            DeriveColumnNames(DefaultTenantPropertyNames),
            ImmutableHashSet<string>.Empty,
            tenantFilterName: null,
            crossTenantAttribute: "CrossTenant",
            filteredBaseTypes: ImmutableHashSet<string>.Empty,
            tenantMarkerInterfaces: ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, interfaces));
    }

    internal static ConfigOptions CreateWithGlobalEntities(params string[] globalEntities)
    {
        return new ConfigOptions(
            DefaultTenantPropertyNames,
            DeriveColumnNames(DefaultTenantPropertyNames),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, globalEntities),
            tenantFilterName: null,
            crossTenantAttribute: "CrossTenant",
            filteredBaseTypes: ImmutableHashSet<string>.Empty,
            tenantMarkerInterfaces: ImmutableHashSet<string>.Empty);
    }

    internal static ImmutableHashSet<string> DeriveColumnNames(ImmutableHashSet<string> propertyNames)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in propertyNames)
        {
            builder.Add(name);
            builder.Add(name.ToLowerInvariant());
            builder.Add(ToSnakeCase(name));
        }
        return builder.ToImmutable();
    }

    internal static string ToSnakeCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        var sb = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static ImmutableHashSet<string> ParseCsvOption(
        AnalyzerConfigOptions options,
        string key,
        ImmutableHashSet<string> defaultValue,
        StringComparer comparer)
    {
        return ParseCsvOptionOrNull(options, key, comparer) ?? defaultValue;
    }

    private static ImmutableHashSet<string>? ParseCsvOptionOrNull(
        AnalyzerConfigOptions options,
        string key,
        StringComparer comparer)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        var builder = ImmutableHashSet.CreateBuilder<string>(comparer);
        foreach (var item in value.Split(','))
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
                builder.Add(trimmed);
        }

        return builder.Count > 0 ? builder.ToImmutable() : null;
    }

    private static string? ParseStringOption(AnalyzerConfigOptions options, string key)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}

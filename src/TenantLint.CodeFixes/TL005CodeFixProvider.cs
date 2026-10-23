using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TenantLint.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TL005CodeFixProvider))]
[Shared]
public sealed class TL005CodeFixProvider : CodeFixProvider
{
    private static readonly string[] LambdaParamCandidates = { "e", "x", "entity", "t", "f" };

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("TL005");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // Skip if entity has IEntityTypeConfiguration (fix would go to wrong place)
            if (diagnostic.Properties.TryGetValue("HasEntityTypeConfiguration", out var hasConfig) &&
                hasConfig == "True")
                continue;

            // Get entity type name for idempotency check
            var entityTypeName = "T";
            if (diagnostic.Properties.TryGetValue("EntityTypeName", out var fullName) && fullName is not null)
            {
                entityTypeName = fullName.Contains(".")
                    ? fullName.Substring(fullName.LastIndexOf('.') + 1)
                    : fullName;
            }

            // Check if there's already a HasQueryFilter for this entity in OnModelCreating
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
            if (classDecl is null)
                continue;

            var onModelCreating = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == "OnModelCreating");

            if (onModelCreating?.Body is not null)
            {
                var alreadyHasFilter = onModelCreating.Body.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.ToString().Contains("HasQueryFilter")
                        && inv.ToString().Contains(entityTypeName));
                if (alreadyHasFilter)
                    continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add HasQueryFilter stub",
                    ct => AddHasQueryFilterStubAsync(context.Document, diagnostic, ct),
                    equivalenceKey: "TL005_AddFilter"),
                diagnostic);
        }
    }

    private static async Task<Document> AddHasQueryFilterStubAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // Detect document line ending style
        var sourceText = root.ToFullString();
        var eol = sourceText.Contains("\r\n") ? "\r\n" : "\n";
        var eolTrivia = SyntaxFactory.EndOfLine(eol);

        // Find the DbSet property from the diagnostic location
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null)
            return document;

        // Get entity type name and tenant property from diagnostic properties
        var entityTypeName = "T";
        if (diagnostic.Properties.TryGetValue("EntityTypeName", out var fullName) && fullName is not null)
        {
            entityTypeName = fullName.Contains(".")
                ? fullName.Substring(fullName.LastIndexOf('.') + 1)
                : fullName;
        }

        var tenantPropertyName = "TenantId";
        if (diagnostic.Properties.TryGetValue("TenantPropertyName", out var propName) && propName is not null)
            tenantPropertyName = propName;

        // Check if named filter syntax should be used
        var useNamedFilter = diagnostic.Properties.TryGetValue("UseNamedFilter", out var useNf) &&
                             useNf == "True";
        var tenantFilterName = "Tenant";
        if (diagnostic.Properties.TryGetValue("TenantFilterName", out var tfn) && tfn is not null)
            tenantFilterName = tfn;

        // Find OnModelCreating method
        var onModelCreating = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "OnModelCreating");

        SyntaxNode newRoot;

        if (onModelCreating?.Body is not null)
        {
            // Use the actual parameter name from OnModelCreating
            var modelBuilderParam = "modelBuilder";
            if (onModelCreating.ParameterList.Parameters.Count > 0)
                modelBuilderParam = onModelCreating.ParameterList.Parameters[0].Identifier.ValueText;

            // Choose a lambda parameter that doesn't collide with identifiers in scope
            var lambdaParam = PickLambdaParam(onModelCreating);

            // Build the HasQueryFilter statement text
            var statementText = BuildStatementText(
                modelBuilderParam, entityTypeName, lambdaParam, tenantPropertyName,
                useNamedFilter, tenantFilterName);

            // Determine indentation from existing statements
            var indent = "        "; // default: 8 spaces
            var existingStatements = onModelCreating.Body.Statements;
            if (existingStatements.Count > 0)
            {
                var lastLeading = existingStatements.Last().GetLeadingTrivia().ToString();
                var ws = new string(lastLeading.SkipWhile(c => c == '\r' || c == '\n')
                    .TakeWhile(c => c == ' ' || c == '\t').ToArray());
                if (ws.Length > 0)
                    indent = ws;
            }

            var statement = SyntaxFactory.ParseStatement(statementText)
                .WithLeadingTrivia(SyntaxFactory.Whitespace(indent))
                .WithTrailingTrivia(eolTrivia);

            var newBody = onModelCreating.Body.AddStatements(statement);
            var newMethod = onModelCreating.WithBody(newBody);
            newRoot = root.ReplaceNode(onModelCreating, newMethod);
        }
        else
        {
            // Create a new OnModelCreating override — use standard "modelBuilder" and "e"
            var lambdaParam = "e";
            var statementText = BuildStatementText(
                "modelBuilder", entityTypeName, lambdaParam, tenantPropertyName,
                useNamedFilter, tenantFilterName);

            var methodText =
                "protected override void OnModelCreating(ModelBuilder modelBuilder)" + eol +
                "    {" + eol +
                "        base.OnModelCreating(modelBuilder);" + eol +
                "        " + statementText + eol +
                "    }";

            var method = SyntaxFactory.ParseMemberDeclaration(methodText);
            if (method is null)
                return document;

            method = method
                .WithLeadingTrivia(eolTrivia, SyntaxFactory.Whitespace("    "))
                .WithAdditionalAnnotations(Formatter.Annotation);

            var newClass = classDecl.AddMembers(method);
            newRoot = root.ReplaceNode(classDecl, newClass);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static string BuildStatementText(
        string modelBuilderParam,
        string entityTypeName,
        string lambdaParam,
        string tenantPropertyName,
        bool useNamedFilter,
        string tenantFilterName)
    {
        var filterExpr = $"{lambdaParam} => {lambdaParam}.{tenantPropertyName} == TODO_SET_TENANT_ACCESSOR";
        return useNamedFilter
            ? $"{modelBuilderParam}.Entity<{entityTypeName}>().HasQueryFilter(\"{tenantFilterName}\", {filterExpr});"
            : $"{modelBuilderParam}.Entity<{entityTypeName}>().HasQueryFilter({filterExpr});";
    }

    private static string PickLambdaParam(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
            return "e";

        var usedIdentifiers = new System.Collections.Generic.HashSet<string>();

        // Collect parameter names
        foreach (var param in method.ParameterList.Parameters)
            usedIdentifiers.Add(param.Identifier.ValueText);

        // Collect all identifier tokens in the method body
        foreach (var token in method.Body.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
                usedIdentifiers.Add(token.ValueText);
        }

        foreach (var candidate in LambdaParamCandidates)
        {
            if (!usedIdentifiers.Contains(candidate))
                return candidate;
        }

        return "e0";
    }
}

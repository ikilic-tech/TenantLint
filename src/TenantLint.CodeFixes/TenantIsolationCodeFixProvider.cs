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

namespace TenantLint.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TenantIsolationCodeFixProvider))]
[Shared]
public sealed class TenantIsolationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("TL001");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var node = root.FindNode(diagnosticSpan);

            // The location is on the IgnoreQueryFilters name — walk up to get the invocation
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation is null)
                continue;

            var properties = diagnostic.Properties;

            // Fix A: Selective IgnoreQueryFilters (offered only when selective overload exists and non-tenant filters exist)
            if (properties.TryGetValue("HasSelectiveOverload", out var hasSelective) &&
                hasSelective == "True" &&
                properties.TryGetValue("NonTenantFilterNames", out var filterNamesStr) &&
                !string.IsNullOrEmpty(filterNamesStr))
            {
                var filterNames = filterNamesStr!.Split(',');
                var filterNamesDisplay = string.Join(", ", filterNames.Select(n => "\"" + n + "\""));

                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Use IgnoreQueryFilters([{filterNamesDisplay}])",
                        ct => RewriteToSelectiveAsync(context.Document, invocation, filterNames, ct),
                        equivalenceKey: "TL001_Selective"),
                    diagnostic);
            }

            // Fix B: Add [CrossTenant("")] (offered only when CrossTenantAttribute exists in compilation)
            if (properties.TryGetValue("HasCrossTenantAttribute", out var hasCrossTenant) &&
                hasCrossTenant == "True")
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Add [CrossTenant(\"\")]",
                        ct => AddCrossTenantAttributeAsync(context.Document, invocation, ct),
                        equivalenceKey: "TL001_CrossTenant"),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> RewriteToSelectiveAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string[] filterNames,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // Build the collection expression: ["SoftDelete", "Other"]
        var elements = filterNames.Select(name =>
            (CollectionElementSyntax)SyntaxFactory.ExpressionElement(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(name))));

        var collectionExpr = SyntaxFactory.CollectionExpression(
            SyntaxFactory.SeparatedList(elements));

        // Build the argument list with the collection expression
        var argument = SyntaxFactory.Argument(collectionExpr);
        var argumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(argument));

        // Replace the empty argument list with the new one
        var newInvocation = invocation.WithArgumentList(argumentList);
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddCrossTenantAttributeAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // Find the enclosing method declaration
        var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null)
            return document;

        // Build [CrossTenant("")] attribute
        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("CrossTenant"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(""))))));

        // Detect the end-of-line style from the source
        var eol = method.SyntaxTree.GetText().Lines.Count > 1
            && method.SyntaxTree.GetText().ToString().Contains("\r\n")
            ? SyntaxFactory.ElasticCarriageReturnLineFeed
            : SyntaxFactory.ElasticLineFeed;

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(eol);

        // Add leading trivia from method to maintain indentation
        var leadingTrivia = method.GetLeadingTrivia();
        var newAttributeList = attributeList.WithLeadingTrivia(leadingTrivia);

        var newMethod = method.AddAttributeLists(newAttributeList);
        var newRoot = root.ReplaceNode(method, newMethod);

        return document.WithSyntaxRoot(newRoot);
    }
}

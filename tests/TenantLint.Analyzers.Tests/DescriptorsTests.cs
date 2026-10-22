using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace TenantLint.Analyzers.Tests;

public class DescriptorsTests
{
    private const string ExpectedBase =
        "https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/";

    public static IEnumerable<object[]> AllDescriptors()
    {
        foreach (var field in typeof(Descriptors).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(DiagnosticDescriptor) &&
                field.GetValue(null) is DiagnosticDescriptor d)
            {
                yield return new object[] { field.Name, d };
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllDescriptors))]
    public void HelpLinkUri_StartsWithCorrectBase(string fieldName, DiagnosticDescriptor descriptor)
    {
        _ = fieldName; // used for test display name
        Assert.StartsWith(ExpectedBase, descriptor.HelpLinkUri);
    }

    [Theory]
    [MemberData(nameof(AllDescriptors))]
    public void HelpLinkUri_PointsToExistingDocsFile(string fieldName, DiagnosticDescriptor descriptor)
    {
        // Skip the hidden placeholder — it has no docs page
        if (descriptor.DefaultSeverity == DiagnosticSeverity.Hidden)
            return;

        var fileName = descriptor.HelpLinkUri!.Substring(ExpectedBase.Length);
        var docsPath = Path.Combine(FindRepoRoot(), "docs", "rules", fileName);
        Assert.True(File.Exists(docsPath),
            $"Descriptor {fieldName} ({descriptor.Id}) points to {fileName} but the file does not exist at {docsPath}");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Cannot find repository root from " + AppContext.BaseDirectory);
    }
}

using Xunit;

namespace TenantLint.Analyzers.Tests;

public class TenantIsolationAnalyzerTests
{
    [Fact]
    public async Task EmptySource_NoDiagnostics()
    {
        var source = "";
        await CSharpAnalyzerVerifier<TenantIsolationAnalyzer>.VerifyAnalyzerAsync(source);
    }
}

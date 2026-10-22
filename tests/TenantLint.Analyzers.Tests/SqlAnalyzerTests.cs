using System.Collections.Immutable;
using Xunit;

namespace TenantLint.Analyzers.Tests;

public class SqlAnalyzerTests
{
    // ===========================================================
    // ContainsTableNameWord — Word Boundary Tests
    // ===========================================================

    [Fact]
    public void ContainsTableNameWord_ExactMatch_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM Customers", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM customers", "Customers"));
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM CUSTOMERS", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_SubstringReject_ReturnsFalse()
    {
        Assert.False(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM CustomerLogs", "Customer"));
    }

    [Fact]
    public void ContainsTableNameWord_SubstringPrefix_ReturnsFalse()
    {
        Assert.False(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM OldCustomers", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_SchemaQualified_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM dbo.Customers", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_QuotedIdentifier_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM \"Customers\"", "Customers"));
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT * FROM [Customers]", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_AtEndOfString_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("DELETE FROM Customers", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_EmptyInputs_ReturnsFalse()
    {
        Assert.False(SqlAnalyzer.ContainsTableNameWord("", "Customers"));
        Assert.False(SqlAnalyzer.ContainsTableNameWord("SELECT *", ""));
        Assert.False(SqlAnalyzer.ContainsTableNameWord(null!, "Customers"));
        Assert.False(SqlAnalyzer.ContainsTableNameWord("SELECT *", null!));
    }

    [Fact]
    public void ContainsTableNameWord_WithAlias_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord("SELECT c.Name FROM Customers c", "Customers"));
    }

    [Fact]
    public void ContainsTableNameWord_InWhereClause_ReturnsTrue()
    {
        Assert.True(SqlAnalyzer.ContainsTableNameWord(
            "SELECT * FROM Orders WHERE CustomerId IN (SELECT Id FROM Customers)", "Customers"));
    }

    // ===========================================================
    // HasTenantColumnInPredicate — Predicate Heuristic Tests
    // ===========================================================

    [Fact]
    public void HasTenantColumnInPredicate_WhereWithParam_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id = @tid", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_OnWithInterpolation_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "TenantId");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers c ON c.TenantId = {0}", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_AndWithParam_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE IsActive = 1 AND tenant_id = @tid", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_OrWithParam_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id = @t1 OR tenant_id = @t2", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_SelectOnly_ReturnsFalse()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT tenant_id, Name FROM Customers", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_OrderBy_ReturnsFalse()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers ORDER BY tenant_id", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_GroupBy_ReturnsFalse()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT tenant_id, COUNT(*) FROM Customers GROUP BY tenant_id", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_EmptySql_ReturnsFalse()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate("", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_EmptyColumns_ReturnsFalse()
    {
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id = @tid",
            ImmutableHashSet<string>.Empty));
    }

    [Fact]
    public void HasTenantColumnInPredicate_WhereNoParam_ReturnsFalse()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.False(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id IS NOT NULL", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_PositionalParam_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id = $1", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_OracleParam_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "tenant_id");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenant_id = :tid", columns));
    }

    [Fact]
    public void HasTenantColumnInPredicate_CaseInsensitiveColumn_ReturnsTrue()
    {
        var columns = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "TenantId");
        Assert.True(SqlAnalyzer.HasTenantColumnInPredicate(
            "SELECT * FROM Customers WHERE tenantid = @tid", columns));
    }
}

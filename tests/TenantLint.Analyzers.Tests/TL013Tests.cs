using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL013Tests
{
    private const string SharedUsings = @"
using System;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
";

    private const string TenantEntityAndContext = @"
public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}
";

    private const string TestCommandStub = @"
public class TestCommand : DbCommand
{
    public TestCommand(string cmdText, DbConnection conn) { CommandText = cmdText; }
    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => throw new NotImplementedException();
    protected override DbTransaction DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object ExecuteScalar() => null;
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => throw new NotImplementedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotImplementedException();
}
";

    // ===========================================================
    // Positive — TL013 (Warning)
    // ===========================================================

    [Fact]
    public async Task DbCommand_CommandText_TenantTable_ReportsTL013()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        {|#0:cmd.CommandText = ""DELETE FROM Customers WHERE IsDeleted = 1""|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL013)
            .WithLocation(0)
            .WithArguments("CommandText", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task IDbCommand_CommandText_TenantTable_ReportsTL013()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(IDbCommand cmd)
    {
        {|#0:cmd.CommandText = ""DELETE FROM Customers WHERE IsDeleted = 1""|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL013)
            .WithLocation(0)
            .WithArguments("CommandText", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task DbCommandDerived_Constructor_TenantTable_ReportsTL013()
    {
        var source = SharedUsings + TenantEntityAndContext + TestCommandStub + @"
public class MyService
{
    public void DoWork(DbConnection conn)
    {
        var cmd = {|#0:new TestCommand(""DELETE FROM Customers"", conn)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL013)
            .WithLocation(0)
            .WithArguments("TestCommand", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Positive — TL113 (Info, tenant column in predicate)
    // ===========================================================

    [Fact]
    public async Task DbCommand_CommandText_TenantColumnInPredicate_ReportsTL113()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        {|#0:cmd.CommandText = ""DELETE FROM Customers WHERE TenantId = @tid AND IsDeleted = 1""|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL113)
            .WithLocation(0)
            .WithArguments("CommandText", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative — no diagnostic
    // ===========================================================

    [Fact]
    public async Task DbCommand_CommandText_NonTenantTable_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        cmd.CommandText = ""DELETE FROM Countries WHERE Id = 1"";
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task DbCommand_CommandText_NonConstantSql_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        cmd.CommandText = GetSql();
    }

    private string GetSql() => ""DELETE FROM Customers"";
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task DbCommand_CommandText_CrossTenantMethod_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

public class MyService
{
    [CrossTenant(""Admin cleanup"")]
    public void DoWork(DbCommand cmd)
    {
        cmd.CommandText = ""DELETE FROM Customers WHERE IsDeleted = 1"";
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task DbCommand_CommandText_WordBoundary_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        cmd.CommandText = ""DELETE FROM CustomerLogs WHERE Id = 1"";
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task DbCommand_CommandText_GlobalEntity_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyService
{
    public void DoWork(DbCommand cmd)
    {
        cmd.CommandText = ""DELETE FROM Customers WHERE IsDeleted = 1"";
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = Customer
";
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig);
    }

    [Fact]
    public async Task UserDefinedCommandText_NotDbCommand_NoDiagnostic()
    {
        var source = SharedUsings + TenantEntityAndContext + @"
public class MyCommand
{
    public string CommandText { get; set; }
}

public class MyService
{
    public void DoWork()
    {
        var cmd = new MyCommand();
        cmd.CommandText = ""DELETE FROM Customers WHERE IsDeleted = 1"";
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}

using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Semantics;

// Names that differ only in case (decision 0107): their presence proves the database compares names by case, which binding
// doesn't model, so a reference to either is unknown rather than bound to the first. Reading reports a table's or a body's such
// names as an error (HAR1022, decision 0119); the binder is given them anyway here, as a model built otherwise may hold them.
public partial class BinderTests
{
    private const string CaseVariantColumns = "CREATE TABLE dbo.item (id int NOT NULL PRIMARY KEY, Code varchar(10) NOT NULL, code int NULL);";

    [Theory]
    [InlineData("Code")]
    [InlineData("code")]
    public void A_column_whose_name_another_differs_from_only_in_case_binds_to_neither(string written)
    {
        var resolution = Assert.IsType<AmbiguousColumn>(new Sample($"SELECT {written}, id FROM dbo.item;", CaseVariantColumns, caseVariants: true).Resolve(written));

        Assert.Contains("Code and code", resolution.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_of_a_table_with_case_variants_elsewhere_still_binds() =>
        Assert.IsType<TableColumn>(new Sample("SELECT Code, id FROM dbo.item;", CaseVariantColumns, caseVariants: true).Resolve("id"));

    [Fact]
    public void A_star_leaves_columns_that_differ_only_in_case_unknown()
    {
        var sample = new Sample("SELECT * FROM dbo.item;", CaseVariantColumns, caseVariants: true);
        var star = (SelectStarExpression)((QuerySpecification)((SelectStatement)sample.Statement).QueryExpression).SelectElements[0];
        var scope = ((BoundSpecification)sample.Query()).Scope;

        var columns = Assert.IsType<ExpandedStar>(sample.Binder.Star(star, scope)).Columns;

        Assert.Equal(["id", "Code", "code"], columns.Select(c => c.Name));
        Assert.IsType<TableColumn>(columns[0].Column);
        Assert.IsType<AmbiguousColumn>(columns[1].Column);
        Assert.IsType<AmbiguousColumn>(columns[2].Column);
    }

    [Theory]
    [InlineData("@Total int", "DECLARE @total varchar(10);", "@Total", "@Total|@total")]
    [InlineData("@Total int", "DECLARE @total varchar(10);", "@TOTAL", "@Total|@total")]
    [InlineData("", "DECLARE @n int; DECLARE @N int;", "@n", "@n|@N")]
    [InlineData("", "DECLARE @rows TABLE (a int); DECLARE @Rows int;", "@rows", "@Rows|@rows")]
    public void A_variable_whose_name_another_differs_from_only_in_case_is_neither(string parameters, string body, string written, string variants)
    {
        var (declarations, _) = Declarations(parameters, body, caseVariants: true);

        var resolved = Assert.IsType<CaseVariantVariable>(Binder.Resolve(new VariableReference { Name = written }, NameEnvironment.Empty.WithDeclarations(declarations)));

        Assert.Equal(variants, string.Join("|", resolved.Variants));
    }

    [Fact]
    public void A_variable_declared_twice_with_one_spelling_is_still_one_variable()
    {
        var (declarations, _) = Declarations("", "DECLARE @n int;\nIF 1 = 0 DECLARE @n int;");

        Assert.IsType<LocalVariable>(Binder.Resolve(new VariableReference { Name = "@n" }, NameEnvironment.Empty.WithDeclarations(declarations)));
    }
}

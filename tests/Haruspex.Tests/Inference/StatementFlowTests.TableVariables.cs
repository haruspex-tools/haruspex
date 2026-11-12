using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class StatementFlowTests
{
    [Fact]
    public void A_table_variable_is_read_as_declared_whatever_is_inserted()
    {
        var doc = Procedure("""
            DECLARE @v TABLE (id int NOT NULL PRIMARY KEY, name nvarchar(10) NULL, c dbo.code);
            INSERT INTO @v (id, name, c) SELECT u.id, u.name, N'x' FROM dbo.u AS u;
            SELECT v.id, v.name, v.c FROM @v AS v;
            """);

        Assert.Empty(doc.Unknowns);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Equal([new IrNullReason(IrNullRule.NullableColumn, "@v.name")], Field(doc, "name").NullableBecause);

        // A table variable takes the database's alias types, NOT NULL included (observed).
        Assert.False(Field(doc, "c").Nullable);
        Assert.Equal(4, Scalar(Field(doc, "c")).Facets!.Length!.Value);
    }

    [Fact]
    public void A_table_variable_read_without_an_alias_binds_by_its_name()
    {
        var doc = Procedure("DECLARE @v TABLE (id int NOT NULL);\nSELECT id FROM @v;");

        Assert.False(Field(doc, "id").Nullable);
    }

    [Fact]
    public void A_table_variable_declared_in_a_branch_exists_after_it()
    {
        // Declarations are compiled, not run (observed).
        var doc = Procedure("IF @x = 1 BEGIN DECLARE @v TABLE (id int NOT NULL); END\nSELECT v.id FROM @v AS v;", "@x int");

        Assert.False(Field(doc, "id").Nullable);
    }
}

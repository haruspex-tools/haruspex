using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

public partial class ApplyViewTests
{
    [Fact]
    public void A_values_list_of_one_row_names_its_values()
    {
        var doc = Procedure("SELECT t.id, v.c, v.nm FROM dbo.t AS t CROSS APPLY (VALUES (t.code, t.name)) AS v (c, nm);");

        Typed(doc);
        Assert.Empty(Rules(doc, "c"));
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "nm"));
    }

    [Fact]
    public void Outer_apply_of_a_values_list_never_null_extends()
    {
        // A VALUES list always has its rows.
        var doc = Procedure("SELECT t.id, v.c FROM dbo.t AS t OUTER APPLY (VALUES (t.code), (t.code)) AS v (c);");

        Assert.Empty(Rules(doc, "c"));
    }

    [Fact]
    public void A_values_column_whose_rows_have_the_same_type_has_it_and_is_null_when_any_is()
    {
        var doc = Procedure("SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES (t.n), (t.id)) AS v (c);");

        Typed(doc);
        Assert.Equal([IrNullRules.NullableColumn], Rules(doc, "c"));
        Assert.Equal((IrScalarKind.Number, "int"), (Assert.IsType<IrScalar>(Field(doc, "c").Type).Kind, Assert.IsType<IrScalar>(Field(doc, "c").Type).Sql));
    }

    [Fact]
    public void A_values_column_whose_rows_differ_in_type_is_unknown()
    {
        // A_values_column_takes_the_type_of_highest_precedence_among_its_rows: the values are converted, which is not modelled.
        var doc = Procedure("SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES (t.name), (t.code)) AS v (c);");

        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(doc, "results[0][].c").Reason);
    }

    [Theory]
    [InlineData("('width', t.n), ('height', t.id)", "varchar", 6)]
    [InlineData("('a', t.n), ('', t.id)", "varchar", 1)]
    [InlineData("('', t.n)", "varchar", 1)]
    [InlineData("('ab', t.n), (N'xyz', t.id)", "nvarchar", 3)]
    public void A_values_column_of_string_literals_is_a_non_null_string_of_the_longest(string rows, string sql, int length)
    {
        // A_values_column_of_string_literals_is_a_string_of_the_longest: the unpivot idiom's label column.
        var doc = Procedure($"SELECT v.label, v.size FROM dbo.t AS t CROSS APPLY (VALUES {rows}) AS v (label, size);");

        Assert.Empty(Rules(doc, "label"));
        var scalar = Assert.IsType<IrScalar>(Field(doc, "label").Type);
        Assert.Equal($"{sql}({length})", scalar.Sql);
        Assert.Equal(length, scalar.Facets?.Length?.Value);
    }

    [Theory]
    [InlineData("('caf\u00e9'), ('tea')")]
    [InlineData("(t.name), (N'zz')")]
    public void A_values_column_of_other_strings_is_unknown(string rows) =>
        Assert.Equal(IrUnknownReasons.UnsupportedExpression, Unknown(Procedure($"SELECT v.label FROM dbo.t AS t CROSS APPLY (VALUES {rows}) AS v (label);"), "results[0][].label").Reason);

    [Fact]
    public void A_values_list_in_from_is_modelled_too()
    {
        var doc = Procedure("SELECT s.id FROM (VALUES (1), (2)) AS s (id);");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
    }
}

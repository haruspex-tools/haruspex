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
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "nm"));
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
        Assert.Equal([IrNullRule.NullableColumn], Rules(doc, "c"));
        Assert.Equal((IrScalarKind.Number, "int"), (Assert.IsType<IrScalar>(Field(doc, "c").Type).Kind, Assert.IsType<IrScalar>(Field(doc, "c").Type).Sql));
    }

    [Theory]
    [InlineData("(t.name), (t.code)", "nvarchar(10)", new[] { IrNullRule.NullableColumn })]
    [InlineData("(t.name), (N'zz')", "nvarchar(10)", new[] { IrNullRule.NullableColumn })]
    [InlineData("(1), (2.5)", "numeric(2,1)", new IrNullRule[0])]
    [InlineData("(t.id), (CAST(1 AS money))", "money", new IrNullRule[0])]
    [InlineData("(NULL), (N'text')", "nvarchar(4)", new[] { IrNullRule.NullLiteral })]
    public void A_values_column_whose_rows_differ_in_type_takes_the_one_of_highest_precedence(string rows, string sql, IrNullRule[] rules)
    {
        // A_values_column_takes_the_type_of_highest_precedence_among_its_rows, as a set operation's column does
        // (SetTypeGridTests, its values form): one rule over the rows, column by column, a NULL adding only its null.
        var doc = Procedure($"SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES {rows}) AS v (c);");

        Typed(doc);
        Assert.Equal(sql, Assert.IsType<IrScalar>(Field(doc, "c").Type).Sql);
        Assert.Equal(rules, Rules(doc, "c"));
    }

    [Fact]
    public void A_values_list_types_each_column_by_its_own_rows()
    {
        var doc = Procedure("SELECT v.kind, v.note FROM (VALUES ('a', NULL), ('bb', N'text')) AS v (kind, note);");

        Typed(doc);
        Assert.Equal("varchar(2)", Assert.IsType<IrScalar>(Field(doc, "kind").Type).Sql);
        Assert.Empty(Rules(doc, "kind"));
        Assert.Equal("nvarchar(4)", Assert.IsType<IrScalar>(Field(doc, "note").Type).Sql);
        Assert.Equal([IrNullRule.NullLiteral], Rules(doc, "note"));
    }

    [Fact]
    public void A_values_column_whose_rows_sql_server_refuses_to_combine_is_unknown_naming_both_types()
    {
        // Types_without_an_implicit_conversion_are_refused_with_an_operand_type_clash, for VALUES as for a set operation.
        var unknown = Unknown(Procedure("SELECT v.c FROM dbo.t AS t CROSS APPLY (VALUES (t.id), (NEWID())) AS v (c);"), "results[0][].c");

        Assert.Equal(IrUnknownReason.UnsupportedExpression, unknown.Reason);
        Assert.Contains("refuses to combine int and uniqueidentifier", unknown.Detail, StringComparison.Ordinal);
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

    [Fact]
    public void A_values_column_with_a_varchar_literal_beyond_ascii_is_unknown() =>
        // Its length in bytes depends on the database's code page.
        Assert.Equal(IrUnknownReason.UnsupportedExpression, Unknown(Procedure("SELECT v.label FROM dbo.t AS t CROSS APPLY (VALUES ('caf\u00e9'), ('tea')) AS v (label);"), "results[0][].label").Reason);

    [Fact]
    public void A_values_list_in_from_is_modelled_too()
    {
        var doc = Procedure("SELECT s.id FROM (VALUES (1), (2)) AS s (id);");

        Typed(doc);
        Assert.Empty(Rules(doc, "id"));
    }
}

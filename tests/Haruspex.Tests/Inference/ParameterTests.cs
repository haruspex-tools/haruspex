using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A procedure's parameters in the IR: each keeps its declared type's facts, which are what SQL Server keeps of a value sent
/// to it, so that a client can check a value against them before sending it (<c>ParameterObservationTests</c> observes what
/// the server does with one outside them).
/// </summary>
public class ParameterTests
{
    /// <summary>The one parameter of <c>dbo.p</c>, declared as <paramref name="declaration"/>.</summary>
    private static IrParameter Parameter(string declaration, string? collation = null)
    {
        using var dir = new TempDirectory();
        dir.File("p.sql", $"CREATE PROCEDURE dbo.p {declaration} AS SELECT 1 AS one;");
        var project = SqlProject.Load(dir.Path, collation);
        var doc = project.Infer(project.Select([ObjectPattern.Parse("dbo.p")]).Procedures, HaruspexConfig.None(".")).Expected();
        return Assert.Single(doc.Objects[0].Parameters);
    }

    private static IrFacets Facets(IrParameter parameter) => Assert.IsType<IrScalar>(parameter.Type).Facets!;

    [Fact]
    public void An_nvarchar_parameter_keeps_its_length_in_utf16_code_units()
    {
        var length = Facets(Parameter("@name nvarchar(20)")).Length!;

        Assert.Equal((20, false, IrLengthUnit.Utf16), (length.Value, length.IsFixed, length.Unit));
    }

    [Fact]
    public void A_char_parameter_keeps_its_fixed_length_in_bytes()
    {
        var length = Facets(Parameter("@code char(3)")).Length!;

        Assert.Equal((3, true, IrLengthUnit.Bytes, IrByteEncoding.Unknown), (length.Value, length.IsFixed, length.Unit, length.Encoding));
    }

    [Fact]
    public void A_varchar_parameter_counts_bytes_in_the_database_s_default_collation()
    {
        // A parameter takes the database's default collation, as a column of its tables without COLLATE does.
        var length = Facets(Parameter("@name varchar(10)", "Latin1_General_100_CI_AS_SC_UTF8")).Length!;

        Assert.Equal((IrLengthUnit.Bytes, IrByteEncoding.Utf8), (length.Unit, length.Encoding));
    }

    [Fact]
    public void A_decimal_parameter_keeps_its_digits_and_the_range_they_hold()
    {
        var facets = Facets(Parameter("@price decimal(5,2)"));

        Assert.Equal((5, 2), (facets.Digits!.Precision, facets.Digits.Scale));
        Assert.Equal(("-999.99", "999.99"), (facets.Min!.Value, facets.Max!.Value));
    }

    [Fact]
    public void An_integer_parameter_keeps_its_type_s_range()
    {
        var facets = Facets(Parameter("@count tinyint"));

        Assert.Equal(("0", "255"), (facets.Min!.Value, facets.Max!.Value));
    }

    [Fact]
    public void A_parameter_of_max_length_claims_no_length() =>
        Assert.Null(Assert.IsType<IrScalar>(Parameter("@notes nvarchar(max)").Type).Facets);

    [Fact]
    public void A_date_parameter_keeps_its_type_spelt_with_its_defaults() =>

        // The IR has no facet for a date's range or a time's fraction; the type, spelt as decision 0041 fixes, says them.
        Assert.Equal("datetime2(7)", Assert.IsType<IrScalar>(Parameter("@at datetime2").Type).Sql);

    [Fact]
    public void A_table_valued_parameter_is_unknown_with_a_reason()
    {
        using var dir = new TempDirectory();
        dir.File("t.sql", "CREATE TYPE dbo.ids AS TABLE (id int NOT NULL);");
        dir.File("p.sql", "CREATE PROCEDURE dbo.p @ids dbo.ids READONLY AS SELECT 1 AS one;");
        var project = SqlProject.Load(dir.Path);
        var doc = project.Infer(project.Select([ObjectPattern.Parse("dbo.p")]).Procedures, HaruspexConfig.None(".")).Expected();

        Assert.IsType<IrUnknownType>(Assert.Single(doc.Objects[0].Parameters).Type);
        Assert.Contains(doc.Unknowns, u => u.Path == "parameters.@ids");
    }
}

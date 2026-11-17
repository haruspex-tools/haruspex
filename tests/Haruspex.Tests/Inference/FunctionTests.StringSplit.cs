using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

public partial class FunctionTests
{
    [Theory]
    [InlineData("t.s, ','", "varchar(10)")]
    [InlineData("t.c, ','", "varchar(5)")]
    [InlineData("t.nc, N','", "nvarchar(4)")]
    [InlineData("t.ns, N','", "nvarchar(20)")]
    [InlineData("t.vm, ','", "varchar(max)")]
    [InlineData("t.nm, N','", "nvarchar(max)")]
    [InlineData("t.s, N','", "nvarchar(10)")]
    [InlineData("N'a,b', N','", "nvarchar(3)")]
    public void String_split_gives_value_of_its_strings_type_varying_and_never_null(string arguments, string type)
    {
        // String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked. A NULL string gives no
        // rows, so value is never null, even of a nullable column.
        var doc = Procedure($"SELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT({arguments}) AS v;");
        Assert.Equal(type, Typed(doc, "a"));
    }

    [Fact]
    public void String_split_has_ordinal_a_bigint_from_1_only_with_the_literal_1()
    {
        // String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked.
        var doc = Procedure("SELECT v.value AS a, v.ordinal AS b FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s, ',', 1) AS v;");
        Assert.Equal("varchar(10)", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal(("1", "9223372036854775807", "STRING_SPLIT"), Range(doc, "b"));

        Assert.Equal(["value"], Fields(Procedure("SELECT v.* FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s, ',', 0) AS v;")).Select(f => f.Name));
        Assert.Equal(["value"], Fields(Procedure("SELECT v.* FROM STRING_SPLIT(N'a', N',', NULL) AS v;")).Select(f => f.Name));
    }

    [Theory]
    [InlineData("SELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.n, ',') AS v;")]
    [InlineData("DECLARE @o int = 1; SELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s, ',', @o) AS v;")]
    [InlineData("SELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s, ',', 2) AS v;")]
    [InlineData("SELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s) AS v;")]
    public void String_split_of_what_the_server_refuses_is_unknown(string body)
    {
        // A string that isn't a string, and an ordinal flag that isn't the literal 0, 1 or NULL, are refused.
        var doc = Procedure(body);
        Unknown(doc, "a");
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(130, true)]
    [InlineData(170, true)]
    public void String_split_is_unknown_in_a_project_whose_level_is_below_130(int level, bool typed)
    {
        // String_split_needs_compatibility_level_130_and_has_its_ordinal_from_there: below it SQL Server doesn't find the name.
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT v.value AS a FROM dbo.t AS t CROSS APPLY STRING_SPLIT(t.s, ',') AS v;");
        var project = dir.File("db/db.sqlproj", $"""
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <Name>db</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
                <CompatibilityMode>{level}</CompatibilityMode>
              </PropertyGroup>
            </Project>
            """);
        var loaded = SqlProject.LoadProject(project, []);
        Assert.Empty(loaded.Diagnostics);
        var doc = loaded.Infer(loaded.Procedures).Expected();
        if (typed)
        {
            Assert.Equal("varchar(10)", Typed(doc, "a"));
        }
        else
        {
            Unknown(doc, "a");
            Assert.Contains(doc.Unknowns, u => u.Detail.Contains("compatibility level 130", StringComparison.Ordinal) && u.Reason == IrUnknownReason.UnsupportedSource);
        }
    }
}

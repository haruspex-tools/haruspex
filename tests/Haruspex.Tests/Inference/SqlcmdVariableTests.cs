using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// SQLCMD variables in bodies, <c>'$(Region)'</c>: a deployment replaces each with its value before SQL Server sees the text, so
/// a literal holding one is unknown, with the reason <c>sqlcmd-variable</c>, and nothing drawn from its written text (its length,
/// its value, its type, a JSON document it spells) is claimed. Each rule rests on <c>SqlcmdVariableObservationTests</c> in the
/// differential tests, which deploy a body with the variable set to values of different lengths, the empty one too; why every
/// source is read this way is decision 0046.
/// </summary>
public class SqlcmdVariableTests
{
    private const string Tables = """
        CREATE TABLE kiosk.stall (stall_id int NOT NULL PRIMARY KEY, region varchar(30) NOT NULL, opened date NULL);
        GO
        CREATE TABLE kiosk.zone (
            zone_id int NOT NULL PRIMARY KEY,
            region varchar(30) NOT NULL CONSTRAINT ck_zone_region CHECK (region IN ('$(Region)', 'south')),
            kind varchar(10) NOT NULL CONSTRAINT ck_zone_kind CHECK (kind IN ('east', 'west')),
            code varchar(10) NOT NULL CONSTRAINT uq_zone_code UNIQUE,
            [$(Label)] int NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("kiosk.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE kiosk.p AS\n{body}"));

    private static IrUnknownReason? ReasonOf(IrDocument doc, string name) =>
        IrNavigation.Field(doc, name).Type is IrUnknownType unknown ? unknown.Reason : null;

    [Theory]
    [InlineData("'$(Region)'")]
    [InlineData("N'$(Region)'")]
    [InlineData("'north-$(Region)-west'")]
    [InlineData("('$(Region)')")]
    public void A_literal_holding_a_variable_is_unknown_since_a_deployment_replaces_it(string literal)
    {
        var doc = Procedure($"SELECT {literal} AS region;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "region"));
        var unknown = Assert.Single(doc.Unknowns);
        Assert.Equal(IrUnknownReason.SqlcmdVariable, unknown.Reason);
        Assert.Contains("$(Region)", unknown.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("'$5'")]
    [InlineData("'$(not a name)'")]
    [InlineData("'$('")]
    [InlineData("'(Region)'")]
    public void A_literal_without_a_variable_s_form_keeps_its_text(string literal)
    {
        var doc = Procedure($"SELECT {literal} AS plain;");

        Assert.Null(ReasonOf(doc, "plain"));
    }

    [Fact]
    public void A_variable_in_a_comment_changes_nothing()
    {
        var doc = Procedure("-- $(Region)\nSELECT 'south' /* $(Region) */ AS region;");

        Assert.Equal("varchar(5)", ((IrScalar)IrNavigation.Field(doc, "region").Type).Sql);
    }

    [Fact]
    public void A_variable_the_script_sets_itself_is_its_value()
    {
        // sqlcmd's own :setvar wins over -v and the environment (observed with sqlcmd on 15 Nov 2026), so its value is known.
        var doc = InferProcedure.Infer(
            "kiosk.p",
            ("tables.sql", Tables),
            ("p.sql", ":setvar Region \"south\"\nGO\nCREATE PROCEDURE kiosk.p AS\nSELECT '$(Region)' AS region;"));

        Assert.Equal("varchar(5)", ((IrScalar)IrNavigation.Field(doc, "region").Type).Sql);
    }

    [Fact]
    public void Its_length_is_not_folded_from_the_text()
    {
        var doc = Procedure("SELECT len('$(Region)') AS n, datalength(N'$(Region)') AS bytes;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "n"));
        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "bytes"));
    }

    [Fact]
    public void Concat_of_a_literal_holding_a_variable_is_unknown_for_that_reason()
    {
        var doc = Procedure("SELECT CONCAT('kiosk-', '$(Region)') AS host, CONCAT_WS('-', 'kiosk', '$(Region)') AS joined;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "host"));
        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "joined"));
    }

    [Fact]
    public void A_case_of_literals_one_holding_a_variable_has_no_value_set()
    {
        var doc = Procedure("SELECT CASE WHEN s.stall_id = 1 THEN '$(Region)' ELSE 'south' END AS region FROM kiosk.stall AS s;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "region"));
    }

    [Fact]
    public void A_variable_assigned_from_it_carries_no_fact_of_the_text()
    {
        var doc = Procedure("DECLARE @r varchar(30) = '$(Region)';\nSELECT @r AS region;");

        // The declared type stands, as for any value assigned to it; nothing of the text's length or value does.
        var type = IrNavigation.Field(doc, "region").Type;
        Assert.True(type is IrUnknownType or IrScalar { Facets: null or { Values: null, MinLength: null } }, type.ToString());
    }

    [Fact]
    public void A_union_of_literals_one_holding_a_variable_has_no_value_set()
    {
        var doc = Procedure("SELECT 'south' AS region UNION ALL SELECT '$(Region)';");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "region"));
    }

    [Fact]
    public void A_json_document_spelt_with_a_variable_has_no_shape_from_its_text()
    {
        var doc = Procedure("""SELECT json_query('{"region":"$(Region)"}') AS doc FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;""");

        Assert.Contains(doc.Unknowns, u => u.Reason == IrUnknownReason.SqlcmdVariable);
        Assert.DoesNotContain(doc.Types.Values.SelectMany(t => t.Fields), f => f.Name == "region");
    }

    [Fact]
    public void A_conversion_of_it_is_not_judged_by_the_text()
    {
        var doc = Procedure("SELECT CAST('$(Limit)' AS int) AS n;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "n"));
    }

    [Fact]
    public void A_check_set_with_a_variable_among_its_items_gives_no_set()
    {
        // Published, the constraint is the variable's value: ([region]='south' OR [region]='') with an empty one (observed).
        var doc = Procedure("SELECT z.region, z.kind FROM kiosk.zone AS z;");

        Assert.Null(((IrScalar)IrNavigation.Field(doc, "region").Type).Facets?.Values);
        Assert.NotNull(((IrScalar)IrNavigation.Field(doc, "kind").Type).Facets?.Values);
    }

    [Theory]
    [InlineData("SELECT 1 AS [$(Label)];")]
    [InlineData("SELECT 1 AS '$(Label)';")]
    [InlineData("SELECT z.[$(Label)] FROM kiosk.zone AS z;")]
    [InlineData("SELECT z.zone_id, z.[$(Label)] AS [$(Label)] FROM kiosk.zone AS z FOR JSON PATH;")]
    [InlineData("SELECT z.zone_id AS [area.$(Label)] FROM kiosk.zone AS z FOR JSON PATH;")]
    [InlineData("SELECT 1 AS [$(Label)], 2 AS [$(Other)];")]
    public void A_key_holding_a_variable_leaves_the_result_s_keys_unknown(string body)
    {
        // Published, SELECT 1 AS [$(Label)] returns a column named by the value (observed).
        var doc = Procedure(body);

        Assert.Equal(IrUnknownReason.SqlcmdVariable, Assert.IsType<IrUnknownType>(Assert.Single(doc.Objects[0].Results!).Type).Reason);
        Assert.Equal(IrUnknownReason.SqlcmdVariable, Assert.Single(doc.Unknowns).Reason);
    }

    [Fact]
    public void A_json_object_key_holding_a_variable_leaves_the_object_unknown()
    {
        var doc = Procedure("SELECT json_object('$(Label)': z.zone_id) AS doc FROM kiosk.zone AS z;");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "doc"));
    }

    [Fact]
    public void A_json_path_holding_a_variable_finds_nothing_so_claims_only_what_any_value_may_be()
    {
        // No guard is needed: a document traced here holds only keys known as written (one spelt with a variable is unknown), so
        // a path holding a variable names a key it lacks, and the value is read as absent: nullable, of the function's type.
        var doc = Procedure("""
            DECLARE @doc nvarchar(max) = (SELECT 1 AS north FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
            SELECT json_value(@doc, '$."$(Key)"') AS v, json_value(@doc, 'strict $."$(Key)"') AS s;
            """);

        foreach (var name in new[] { "v", "s" })
        {
            var field = IrNavigation.Field(doc, name);
            Assert.Equal(new IrScalar(IrScalarKind.String, null, "nvarchar(4000)"), (IrScalar)field.Type with { Facets = null });
            Assert.Null(((IrScalar)field.Type).Facets?.Values);
            Assert.True(field.Nullable, name);
        }
    }

    [Fact]
    public void A_values_column_with_a_variable_is_unknown()
    {
        var doc = Procedure("SELECT v.label FROM (VALUES ('north'), ('$(Region)')) AS v (label);");

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "label"));
    }

    [Fact]
    public void A_json_value_of_a_document_with_a_variable_is_not_refused_by_its_text()
    {
        // '$(Doc)' is no JSON object as written; deployed it may be one, so the call is not called refused.
        var doc = Procedure("SELECT json_value('$(Doc)', '$.a') AS a;");

        Assert.DoesNotContain(doc.Unknowns, u => u.Detail.Contains("refuses", StringComparison.Ordinal));
    }

    [Fact]
    public void A_unique_key_compared_with_a_variable_does_not_prove_one_row()
    {
        var doc = Procedure("SELECT z.zone_id FROM kiosk.zone AS z WHERE z.code = '$(Code)';");

        Assert.Equal(IrCardinality.Many, doc.Objects[0].Results![0].Cardinality);
    }

    [Fact]
    public void A_project_that_declares_the_variable_with_a_value_reads_it_as_unknown_too()
    {
        // The build records every declared value empty, and a publish is given its own (decision 0046), so the project's value
        // says nothing about what is deployed.
        using var folder = new TempDirectory();
        folder.File("p.sql", "CREATE SCHEMA kiosk;\nGO\nCREATE PROCEDURE kiosk.p AS SELECT '$(Region)' AS region;");
        var project = folder.File("kiosk.sqlproj", """
            <Project Sdk="Microsoft.Build.Sql/2.3.0">
              <PropertyGroup>
                <Name>kiosk</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
              </PropertyGroup>
              <ItemGroup>
                <SqlCmdVariable Include="Region">
                  <DefaultValue>north</DefaultValue>
                  <Value>north</Value>
                </SqlCmdVariable>
              </ItemGroup>
            </Project>
            """);
        var read = SqlProject.LoadProject(project, []);
        Assert.Empty(read.Diagnostics);

        var doc = read.Infer(read.Procedures).Expected();

        Assert.Equal(IrUnknownReason.SqlcmdVariable, ReasonOf(doc, "region"));
    }
}

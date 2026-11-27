using System.Text;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.Tests.Sources;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A column's JSON Schema named in the SQL, by the extended property <c>haruspex.jsonSchema</c>: it declares what
/// <c>columns</c> in the config file declares, recorded the same way, as declared and not proven; it must agree with the config
/// file where both declare a column; and what can't be read, or is on something that can't be declared, is said. Why:
/// docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md. The levels the server takes it on are observed in
/// <c>ColumnAnnotationObservationTests</c>; a package's annotation is read in the fixture <c>parcels</c>.
/// </summary>
public class ColumnAnnotationTests
{
    private const string Tables = """
        CREATE SCHEMA shop;
        GO
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            settings nvarchar(max) NULL,
            total int NOT NULL,
            doubled AS total * 2);
        GO
        CREATE TABLE shop.returns (return_id int NOT NULL PRIMARY KEY, settings nvarchar(max) NULL);
        GO
        CREATE SCHEMA archive;
        GO
        CREATE TABLE archive.orders (order_id int NOT NULL PRIMARY KEY, settings nvarchar(max) NULL);
        GO
        CREATE VIEW shop.order_view AS SELECT o.order_id, o.settings FROM shop.orders AS o;
        GO
        CREATE FUNCTION shop.order_settings() RETURNS TABLE AS RETURN SELECT o.settings FROM shop.orders AS o;
        GO
        CREATE FUNCTION shop.settings_of(@doc nvarchar(max)) RETURNS nvarchar(max) AS BEGIN RETURN @doc; END;
        GO
        CREATE PROCEDURE shop.save_settings @settings nvarchar(max) AS SELECT json_value(@settings, '$.mode') AS mode;
        GO
        CREATE PROCEDURE shop.save_other @settings nvarchar(max) AS SELECT json_value(@settings, '$.mode') AS mode;
        GO
        """;

    private const string Settings = """
        { "type": "object", "required": ["mode"], "properties": {
            "mode": { "enum": ["fast", "slow"] },
            "note": { "type": ["string", "null"] } },
          "$defs": { "other": { "type": "object", "properties": { "speed": { "enum": ["low", "high"] } } } } }
        """;

    private const string Procedure = """
        CREATE PROCEDURE shop.p AS
        SELECT o.settings, json_value(o.settings, '$.mode') AS mode, json_query(o.settings) AS live FROM shop.orders AS o FOR JSON PATH;
        """;

    /// <summary>The annotation of <c>shop.orders.settings</c> naming <paramref name="schema"/>.</summary>
    private static string Annotation(string schema = "schemas/settings.schema.json") =>
        $"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'{schema}', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';";

    /// <summary>Infers every procedure of the tables, <paramref name="procedures"/> and <paramref name="annotations"/> (a script of its own), with <paramref name="columns"/> in the config file.</summary>
    private static Outcome<IrDocument> Run(string procedures, string annotations, string columns = "")
    {
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/procedures.sql", procedures);
        if (annotations.Length > 0)
            dir.File("db/annotations.sql", annotations);
        dir.File("schemas/settings.schema.json", Settings);
        dir.File("schemas/copy.schema.json", Settings);
        var project = SqlProject.Load(Path.Combine(dir.Path, "db"));
        Assert.DoesNotContain(project.Diagnostics, d => d.IsError);
        var read = ConfigReader.Read(Encoding.UTF8.GetBytes(columns.Length == 0 ? "{}" : $$"""{ "columns": { {{columns}} } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);
        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        return project.Infer(project.Procedures, read.Expected());
    }

    private const string Configured = """ "shop.orders.settings": { "json": "schemas/settings.schema.json" } """;

    /// <summary>The one diagnostic of a run, as its code and message.</summary>
    private static (string Code, string Message) Only(Outcome<IrDocument> run)
    {
        var found = Assert.Single(run.Diagnostics);
        return (found.Code.ToString(), found.Message);
    }

    // ---- what it declares ---------------------------------------------------------------------------------------

    [Fact]
    public void An_annotation_on_a_table_s_column_types_its_json_as_the_same_declaration_in_the_config_file_does()
    {
        var annotated = Run(Procedure, Annotation()).Expected();
        var configured = Run(Procedure, "", Configured).Expected();

        Assert.Equal(configured.Objects, annotated.Objects);
        Assert.Equal(configured.Columns, annotated.Columns);
        Assert.Equal(configured.Overrides, annotated.Overrides);
        Assert.IsType<IrJsonText>(Field(annotated, "settings").Type);
        Assert.Equal(["fast", "slow"], Assert.IsType<IrScalar>(Field(annotated, "mode").Type).Facets?.Values?.Items.Select(v => v.Text));
    }

    [Fact]
    public void An_annotation_is_recorded_as_a_declaration_and_each_object_that_reads_it_lists_the_claim()
    {
        var doc = Run(Procedure, Annotation()).Expected();

        var declared = Assert.Single(doc.Columns);
        Assert.Equal(("shop.orders.settings", "schemas/settings.schema.json", null), (declared.Column, declared.Schema, declared.Pointer));
        Assert.Equal(["schemas/settings.schema.json"], declared.Files.Keys);
        var claim = Assert.Single(doc.Overrides, o => o.Setting == IrOverrideSetting.ColumnJson);
        Assert.Equal(("shop.p", "results", "shop.orders.settings"), (claim.Object, claim.Path, claim.Via));
    }

    [Fact]
    public void An_annotation_s_pointer_names_a_schema_inside_its_file_and_names_are_compared_ignoring_case()
    {
        var doc = Run(
            "CREATE PROCEDURE shop.p AS SELECT json_value(o.settings, '$.speed') AS speed FROM shop.orders AS o;",
            "EXEC sp_addextendedproperty N'HARUSPEX.JSONSCHEMA', N'schemas/settings.schema.json#/$defs/other', N'schema', N'SHOP', N'table', N'Orders', N'column', N'SETTINGS';").Expected();

        var declared = Assert.Single(doc.Columns);
        Assert.Equal(("shop.orders.settings", "/$defs/other"), (declared.Column, declared.Pointer));
        Assert.Equal(["high", "low"], Assert.IsType<IrScalar>(Field(doc, "speed").Type).Facets?.Values?.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_package_s_annotation_is_read_as_its_folder_s_is()
    {
        // The parcels fixture names route's schema in its table's script, which its package holds, and the others in haruspex.json.
        var fixture = Fixture.Named("parcels");
        var package = SqlProject.LoadPackage(DacpacEquivalenceTests.PackageOf(fixture), []);
        var config = ConfigReader.Read(fixture.Config!).Expected();
        Assert.DoesNotContain(config.Columns, c => c.Column == "route");

        var doc = package.Infer([new("parcels", "get_parcel")], config).Expected();

        var route = Assert.Single(doc.Columns, c => c.Column == "parcels.parcel.route");
        Assert.Equal(("schemas/stops.schema.json", "/$defs/route"), (route.Schema, route.Pointer));
        Assert.Contains(doc.Overrides, o => o.Setting == IrOverrideSetting.ColumnJson && o.Via == "parcels.parcel.route");
    }

    /// <summary>
    /// A scalar function <c>shop.settings_of(@doc)</c> as a build writes it (Microsoft.Build.Sql 2.3.0, its element copied from a
    /// package built of an invented script), for a package that defines a function.
    /// </summary>
    private const string SettingsOf = """<Element Type="SqlScalarFunction" Name="[shop].[settings_of]"><Property Name="IsAnsiNullsOn" Value="True" /><Relationship Name="BodyDependencies"><Entry><References Name="[shop].[settings_of].[@doc]" /></Entry></Relationship><Relationship Name="FunctionBody"><Entry><Element Type="SqlScriptFunctionImplementation"><Property Name="BodyScript"><Value><![CDATA[ begin return @doc; end;]]></Value></Property><Annotation Type="SysCommentsObjectAnnotation"><Property Name="HeaderContents" Value="create function shop.settings_of (@doc nvarchar(max)) returns nvarchar(max) as" /></Annotation></Element></Entry></Relationship><Relationship Name="Parameters"><Entry><Element Type="SqlSubroutineParameter" Name="[shop].[settings_of].[@doc]"><Relationship Name="Type"><Entry><Element Type="SqlTypeSpecifier"><Property Name="IsMax" Value="True" /><Relationship Name="Type"><Entry><References ExternalSource="BuiltIns" Name="[nvarchar]" /></Entry></Relationship></Element></Entry></Relationship></Element></Entry></Relationship><Relationship Name="Schema"><Entry><References Name="[shop]" /></Entry></Relationship><Relationship Name="Type"><Entry><Element Type="SqlTypeSpecifier"><Property Name="IsMax" Value="True" /><Relationship Name="Type"><Entry><References ExternalSource="BuiltIns" Name="[nvarchar]" /></Entry></Relationship></Element></Entry></Relationship></Element>""";

    /// <summary>A package's element setting the property <paramref name="property"/> to <paramref name="value"/> on what <paramref name="host"/> names.</summary>
    private static string Property(string host, string property, string value) =>
        $"""<Element Type="SqlExtendedProperty" Name="{host}.[{property}]"><Property Name="Value"><Value><![CDATA[N'{value}']]></Value></Property></Element>""";

    /// <summary>
    /// A package <c>app</c> that sets <paramref name="properties"/> on what a package of the same database it refers to,
    /// <c>base</c>, defines: the table <c>shop.orders</c>, the view <c>shop.order_view</c> over it, and the function
    /// <c>shop.settings_of</c>. <c>app</c>'s procedure <c>shop.p</c> reads <paramref name="from"/>. Inferred with the schema
    /// beside the config file.
    /// </summary>
    private static Outcome<IrDocument> TwoPackages(string from, params string[] properties)
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", Settings);
        TestPackage.Write(
            Path.Combine(dir.Path, "base.dacpac"),
            TestPackage.Table("shop", "orders", "order_id int", "settings nvarchar(4000)?")
                + TestPackage.View("shop", "order_view", "SELECT o.order_id, o.settings FROM shop.orders AS o;")
                + SettingsOf);
        TestPackage.Write(
            Path.Combine(dir.Path, "app.dacpac"),
            TestPackage.Procedure("shop", "p", $"SELECT o.settings FROM {from} AS o;") + string.Concat(properties),
            references: new TestPackage.Reference("base.dacpac"));
        var package = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []);
        var config = ConfigReader.Read(Encoding.UTF8.GetBytes("{}"), Path.Combine(dir.Path, "haruspex.json"), dir.Path).Expected();
        return Outcome.Combine(new Succeeded<int>(0, [.. package.Diagnostics]), package.Infer([new("shop", "p")], config)).Select(both => both.Second);
    }

    /// <summary>The annotation and the description of <paramref name="host"/>, set by <c>app</c>.</summary>
    private static string[] Both(string host) =>
        [Property(host, "haruspex.jsonSchema", "schemas/settings.schema.json"), Property(host, "MS_Description", "The settings of the order.")];

    [Fact]
    public void A_package_s_annotation_on_a_column_another_package_of_the_database_defines_is_read_as_a_table_s()
    {
        var doc = TwoPackages("shop.orders", Both("[SqlColumn].[shop].[orders].[settings]")).Expected();

        Assert.Equal("shop.orders.settings", Assert.Single(doc.Columns).Column);
        Assert.IsType<IrJsonText>(Field(doc, "settings").Type);
        Assert.Equal("The settings of the order.", Field(doc, "settings").Description);
    }

    [Fact]
    public void A_package_s_description_of_a_view_s_column_another_package_defines_reaches_the_field()
    {
        var doc = TwoPackages("shop.order_view", Property("[SqlColumn].[shop].[order_view].[settings]", "MS_Description", "The settings of the order.")).Expected();

        Assert.Equal("The settings of the order.", Field(doc, "settings").Description);
    }

    [Fact]
    public void A_package_s_annotation_on_a_view_s_column_another_package_defines_is_the_error_a_view_s_column_gets()
    {
        var run = TwoPackages("shop.order_view", Property("[SqlColumn].[shop].[order_view].[settings]", "haruspex.jsonSchema", "schemas/settings.schema.json"));

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4113", code);
        Assert.Contains("shop.order_view is a view", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_s_annotation_on_a_function_s_parameter_another_package_defines_is_the_warning_a_parameter_gets()
    {
        var run = TwoPackages("shop.orders", Property("[SqlSubroutineParameter].[shop].[settings_of].[@doc]", "haruspex.jsonSchema", "schemas/settings.schema.json"));

        var (code, message) = Only(run);
        Assert.Equal("HAR4116", code);
        Assert.Contains("the parameter shop.settings_of.@doc isn't read", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_s_annotation_on_a_column_no_package_defines_is_an_error_and_its_description_a_note()
    {
        var run = TwoPackages("shop.orders", Both("[SqlColumn].[shop].[nowhere].[settings]"));

        Assert.IsType<Failed<IrDocument>>(run);
        Assert.Contains(run.Diagnostics, d => d.Code.ToString() == "HAR4115" && d.Message.Contains("the source doesn't define shop.nowhere", StringComparison.Ordinal));
        Assert.Contains(run.Diagnostics, d => d.Code.ToString() == "HAR1024" && d.Message.Contains("shop.nowhere", StringComparison.Ordinal));
    }

    // ---- agreement with the config file ------------------------------------------------------------------------

    [Fact]
    public void An_annotation_that_names_the_schema_the_config_file_declares_agrees_and_is_one_declaration()
    {
        var run = Run(Procedure, Annotation(), Configured);

        Assert.Empty(run.Diagnostics);
        Assert.Equal(Run(Procedure, "", Configured).Expected().Columns, run.Expected().Columns);
    }

    [Theory]
    [InlineData("schemas/copy.schema.json", "schemas/copy.schema.json")]
    [InlineData("schemas/settings.schema.json#/$defs/other", "schemas/settings.schema.json#/$defs/other")]
    public void An_annotation_that_names_another_schema_than_the_config_file_is_an_error_and_neither_is_chosen(string annotated, string named)
    {
        var run = Run(Procedure, Annotation(annotated), Configured);

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4114", code);
        Assert.Contains($"shop.orders.settings is declared to hold JSON of {named} here and of schemas/settings.schema.json in the config file", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_annotation_disagreeing_with_a_config_declaration_spelt_in_another_case_is_still_an_error()
    {
        var run = Run(Procedure, Annotation("schemas/copy.schema.json"), """ "SHOP.ORDERS.Settings": { "json": "schemas/settings.schema.json" } """);

        Assert.Equal("HAR4114", Only(Assert.IsType<Failed<IrDocument>>(run)).Code);
    }

    [Fact]
    public void An_annotation_and_a_config_declaration_of_another_table_s_column_of_the_same_name_are_two_declarations()
    {
        var doc = Run(Procedure, Annotation(), """ "shop.returns.settings": { "json": "schemas/copy.schema.json" } """).Expected();

        Assert.Equal(
            [("shop.orders.settings", "schemas/settings.schema.json"), ("shop.returns.settings", "schemas/copy.schema.json")],
            doc.Columns.Select(c => (c.Column, c.Schema)).Order());
    }

    [Fact]
    public void An_annotation_on_a_table_of_one_schema_declares_nothing_of_the_same_table_in_another()
    {
        var doc = Run(Procedure, "EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'archive', N'TABLE', N'orders', N'COLUMN', N'settings';").Expected();

        Assert.Equal("archive.orders.settings", Assert.Single(doc.Columns).Column);
        Assert.DoesNotContain(doc.Overrides, o => o.Setting == IrOverrideSetting.ColumnJson);
        Assert.IsNotType<IrJsonText>(Field(doc, "settings").Type);
    }

    // ---- where it can't be declared ----------------------------------------------------------------------------

    [Theory]
    [InlineData("N'VIEW', N'order_view', N'COLUMN', N'settings'", "shop.order_view is a view, whose column holds what it reads: declare shop.orders.settings instead")]
    [InlineData("N'FUNCTION', N'order_settings', N'COLUMN', N'settings'", "shop.order_settings is a table function, whose column holds what its query returns")]
    [InlineData("N'TABLE', N'orders', N'COLUMN', N'total'", "shop.orders.total is of the type int, which can't hold JSON text")]
    [InlineData("N'TABLE', N'orders', N'COLUMN', N'doubled'", "shop.orders.doubled is a computed column")]
    public void An_annotation_on_a_column_that_can_t_hold_declared_json_is_the_error_the_config_file_gets(string levels, string problem)
    {
        var run = Run(Procedure, $"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', {levels};");

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4113", code);
        Assert.Contains(problem, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("N'PROCEDURE', N'save_settings', N'PARAMETER', N'@settings'", "the parameter shop.save_settings.@settings")]
    [InlineData("N'FUNCTION', N'settings_of', N'PARAMETER', N'@doc'", "the parameter shop.settings_of.@doc")]
    [InlineData("N'TABLE', N'orders'", "shop.orders")]
    public void An_annotation_on_a_parameter_or_an_object_is_a_warning_naming_it_and_declares_nothing(string levels, string named)
    {
        var run = Run(
            "CREATE PROCEDURE shop.p AS SELECT 1 AS one;",
            $"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', {levels};");

        var (code, message) = Only(run);
        Assert.Equal("HAR4116", code);
        Assert.Contains($"{named} isn't read", message, StringComparison.Ordinal);
        var doc = run.Expected();
        Assert.Empty(doc.Columns);
        Assert.DoesNotContain(doc.Overrides, o => o.Setting == IrOverrideSetting.ColumnJson);
    }

    [Theory]
    [InlineData("N'TABLE', N'parcels', N'COLUMN', N'settings'", "the source doesn't define shop.parcels")]
    [InlineData("N'TABLE', N'orders', N'COLUMN', N'missing'", "the table shop.orders has no column missing")]
    [InlineData("N'PROCEDURE', N'save_settings;2', N'PARAMETER', N'@settings'", "the source doesn't define shop.save_settings;2")]
    [InlineData("N'PROCEDURE', N'save_settings', N'PARAMETER', N'@other'", "the source doesn't define shop.save_settings.@other")]
    [InlineData("N'FUNCTION', N'missing', N'COLUMN', N'settings'", "the source doesn't define shop.missing")]
    public void An_annotation_on_something_not_in_the_source_is_an_error_naming_it(string levels, string problem)
    {
        var run = Run(Procedure, $"EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', {levels};");

        var (code, message) = Only(Assert.IsType<Failed<IrDocument>>(run));
        Assert.Equal("HAR4115", code);
        Assert.Contains(problem, message, StringComparison.Ordinal);
    }

    // ---- what can't be read ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DECLARE @s nvarchar(100) = N'schemas/settings.schema.json';\nEXEC sp_addextendedproperty N'haruspex.jsonSchema', @s, N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR4115", "is passed over")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', 42, N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR4115", "is passed over")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', N' ', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR4115", "is named by no text")]
    [InlineData("EXEC sp_updateextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR4115", "is updated here")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';\nEXEC sp_addextendedproperty N'Haruspex.JsonSchema', N'schemas/settings.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR4115", "is named more than once")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'https://example.com/settings.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR3112", "haruspex never fetches a schema")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/absent.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR3001", "cannot read the schema schemas/absent.schema.json")]
    [InlineData("EXEC sp_addextendedproperty N'haruspex.jsonSchema', N'schemas/settings.schema.json#/$defs/none', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';", "HAR3113", "which names nothing")]
    public void An_annotation_that_can_t_be_read_is_an_error_at_the_sql_that_names_it(string annotation, string expected, string problem)
    {
        var run = Run(Procedure, annotation);

        var found = Assert.Single(Assert.IsType<Failed<IrDocument>>(run).Diagnostics);
        Assert.Equal(expected, found.Code.ToString());
        Assert.Contains(problem, found.Message, StringComparison.Ordinal);
        Assert.EndsWith("annotations.sql", Assert.IsType<SourceLocation>(found.At).File, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_extended_property_on_the_column_is_no_annotation()
    {
        var doc = Run(Procedure, "EXEC sp_addextendedproperty N'jsonSchema', N'schemas/absent.schema.json', N'SCHEMA', N'shop', N'TABLE', N'orders', N'COLUMN', N'settings';").Expected();

        Assert.Empty(doc.Columns);
    }

    // ---- check -------------------------------------------------------------------------------------------------

    /// <summary>Generates in a project whose column is annotated, then makes <paramref name="change"/> and runs check; its exit and error output.</summary>
    private static (int Exit, string Error) CheckAfter(Action<TempDirectory> change)
    {
        using var dir = new TempDirectory();
        dir.File("db/tables.sql", Tables);
        dir.File("db/procedures.sql", Procedure);
        dir.File("db/annotations.sql", Annotation());
        dir.File("schemas/settings.schema.json", Settings);
        dir.File("schemas/copy.schema.json", Settings);
        dir.File("haruspex.json", """{ "source": "db", "out": "out", "schemas": { "shop": { "include": ["p"] } } }""");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, HaruspexCli.Run(["generate"], output, error, dir.Path));

        change(dir);
        error.GetStringBuilder().Clear();
        return (HaruspexCli.Run(["check"], output, error, dir.Path), error.ToString());
    }

    [Fact]
    public void Check_passes_when_nothing_changed()
    {
        var (exit, error) = CheckAfter(_ => { });

        Assert.True(exit == 0, error);
    }

    [Fact]
    public void Check_fails_when_the_annotated_schema_file_s_text_changes_though_no_type_does()
    {
        var (exit, error) = CheckAfter(dir => dir.File("schemas/settings.schema.json", Settings.Replace("\"type\": \"object\"", "\"type\": \"object\", \"description\": \"An order's settings\"", StringComparison.Ordinal)));

        Assert.NotEqual(0, exit);
        Assert.Contains("columns[\"shop.orders.settings\"]: changed from schemas/settings.schema.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_the_annotation_names_another_schema_file_of_the_same_text()
    {
        var (exit, error) = CheckAfter(dir => dir.File("db/annotations.sql", Annotation("schemas/copy.schema.json")));

        Assert.NotEqual(0, exit);
        Assert.Contains("columns[\"shop.orders.settings\"]: changed from schemas/settings.schema.json", error, StringComparison.Ordinal);
        Assert.Contains("schemas/copy.schema.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_fails_when_the_annotation_is_removed()
    {
        var (exit, error) = CheckAfter(dir => File.Delete(Path.Combine(dir.Path, "db", "annotations.sql")));

        Assert.NotEqual(0, exit);
        Assert.Contains("columns[\"shop.orders.settings\"]", error, StringComparison.Ordinal);
    }
}

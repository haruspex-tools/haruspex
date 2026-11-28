using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;
using Haruspex.SqlServer.Sources.Projects;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="DacpacReader"/> on the packages in <c>tests/dacpacs</c>: what each construct of <c>model.xml</c> becomes in the
/// model, how a built package and an extracted one differ, and how references are found, composed and reported. That a package
/// reads as its project's folder does is proved in <c>DacpacEquivalenceTests</c>.
/// </summary>
public partial class DacpacReaderTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    private static readonly Lazy<DacpacReadResult> Atelier = new(() => DacpacReader.Read(Path.Combine(Packages, "atelier", "atelier.dacpac")));

    private static readonly Lazy<DacpacReadResult> AtelierExtract = new(() => DacpacReader.Read(Path.Combine(Packages, "versions", "atelier-extract.dacpac")));

    private static readonly Lazy<DacpacReadResult> Clr = new(() => DacpacReader.Read(Path.Combine(Packages, "versions", "kiln-clr-extract.dacpac")));

    private static readonly Lazy<DacpacReadResult> Harbour = new(() => DacpacReader.Read(Path.Combine(Packages, "harbour", "harbour.dacpac")));

    private static readonly Lazy<DacpacReadResult> WideWorldImporters = new(() => DacpacReader.Read(Path.Combine(Packages, "corpus", "WideWorldImporters-ssdt.dacpac")));

    private static ObjectName Studio(string name) => new("studio", name);

    private static Table Kiln(DacpacReadResult read) => read.Model.FindTable(Studio("kiln"))!;

    private static readonly ObjectName Ingot = new("dbo", "ingot");

    private static readonly ObjectName Mould = new("dbo", "mould");

    /// <summary>A package of two tables, <c>dbo.ingot</c> with a check on <c>grade</c>, and <c>dbo.mould</c>, with the elements given.</summary>
    private static DacpacReadResult Smelter(TempDirectory folder, string elements = "", string? predeploy = null, string? postdeploy = null)
    {
        var path = Path.Combine(folder.Path, "smelter.dacpac");
        // A check written in its table carries the table's Disambiguator, which a build writes on the table too.
        TestPackage.Write(path, TestPackage.Table("dbo", "ingot", "id int", "grade int")[..^"</Element>".Length] + "<AttachedAnnotation Disambiguator=\"1\" /></Element>" + TestPackage.Check("dbo", "ingot", "grade > 0")
            + TestPackage.Table("dbo", "mould", "id int", "code int") + TestPackage.Procedure("dbo", "list_ingots", "SELECT i.grade FROM dbo.ingot AS i;") + elements);
        if (predeploy is not null)
            TestPackage.Part(path, "predeploy.sql", predeploy);
        if (postdeploy is not null)
            TestPackage.Part(path, "postdeploy.sql", postdeploy);
        return DacpacReader.Read(path);
    }

    [Fact]
    public void A_module_body_s_statements_that_weaken_a_table_are_read_as_a_folder_s_are()
    {
        // A load procedure that turns the checks off, and a guarded migration in another: a project's folder reads both, and so
        // must its package, which holds them as module text.
        using var folder = new TempDirectory();
        var read = Smelter(folder, TestPackage.Procedure("dbo", "load", "BEGIN ALTER TABLE dbo.ingot NOCHECK CONSTRAINT ALL; INSERT dbo.ingot VALUES (1, 0); ALTER TABLE dbo.ingot CHECK CONSTRAINT ALL; END")
            + TestPackage.Procedure("dbo", "migrate", "IF COL_LENGTH('dbo.mould', 'code') IS NOT NULL ALTER TABLE dbo.mould ALTER COLUMN code int NULL;"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal(new SourceLocation("smelter.dacpac/[dbo].[load]", 2, 7), Assert.Single(read.Model.FindTable(Ingot)!.Checks).DisabledAt);
        Assert.Equal(RedefiningStatement.AlterTable, read.Model.FindTableRedefinition(Mould)?.Statement);
    }

    [Fact]
    public void A_package_s_deployment_scripts_weaken_what_they_disable_drop_or_alter()
    {
        // A deployment runs them every time: the post-deployment script's NOCHECK and ALTER PROCEDURE stand in every database
        // deployed, so the package's model of either is no longer what the database holds.
        using var folder = new TempDirectory();
        var read = Smelter(
            folder,
            predeploy: "DROP TABLE IF EXISTS dbo.mould;\r\nGO\r\n",
            postdeploy: "ALTER TABLE dbo.ingot NOCHECK CONSTRAINT ALL;\r\nGO\r\nALTER PROCEDURE dbo.list_ingots AS SELECT CAST(NULL AS int) AS grade;\r\nGO\r\n");

        Assert.Empty(read.Diagnostics);
        Assert.Equal(new SourceLocation("smelter.dacpac/postdeploy.sql", 1, 1), Assert.Single(read.Model.FindTable(Ingot)!.Checks).DisabledAt);
        Assert.Equal(
            new Redefinition(new ObjectName("dbo", "list_ingots"), RedefiningStatement.AlterProcedure, new SourceLocation("smelter.dacpac/postdeploy.sql", 3, 1)),
            read.Model.FindProcedure(new ObjectName("dbo", "list_ingots"))!.Redefinition);
        Assert.Equal(new Redefinition(Mould, RedefiningStatement.DropTable, new SourceLocation("smelter.dacpac/predeploy.sql", 1, 1)), read.Model.FindTableRedefinition(Mould));
    }

    [Fact]
    public void A_built_package_s_load_procedure_and_post_deployment_script_weaken_what_they_turn_off_or_alter()
    {
        // orchard as DacFx builds it: a procedure that turns grove.crate's check and its tag's key off, and a post-deployment
        // script, with a file it includes by :r in its text, that turns grove.tree's check off and alters grove.count_trees.
        var read = DacpacReader.Read(Path.Combine(Packages, "orchard", "orchard.dacpac"));
        var crate = read.Model.FindTable(new ObjectName("grove", "crate"))!;
        var tree = read.Model.FindTable(new ObjectName("grove", "tree"))!;

        Assert.DoesNotContain(read.Diagnostics, d => d.IsError);
        Assert.Equal("orchard.dacpac/[grove].[load_crates]", Assert.Single(crate.Checks).DisabledAt?.File);
        Assert.Equal("orchard.dacpac/[grove].[load_crates]", Assert.Single(crate.UniqueKeys, k => k.Name == "ux_crate_tag").DisabledAt?.File);
        Assert.Equal(new SourceLocation("orchard.dacpac/postdeploy.sql", 5, 1), Assert.Single(tree.Checks).DisabledAt);
        Assert.Equal(
            new Redefinition(new ObjectName("grove", "count_trees"), RedefiningStatement.AlterProcedure, new SourceLocation("orchard.dacpac/postdeploy.sql", 11, 1)),
            read.Model.FindProcedure(new ObjectName("grove", "count_trees"))!.Redefinition);
    }

    [Fact]
    public void A_deployment_script_that_does_not_parse_is_a_warning_and_doubts_every_object_it_names()
    {
        using var folder = new TempDirectory();
        var read = Smelter(folder, postdeploy: ":on error exit\nALTER TABLE [dbo].[ingot] ALTER COLUMN grade int NULL;\n");

        var warning = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.DeploymentScriptNotRead, warning.Code);
        Assert.Equal(RedefiningStatement.UnreadScript, read.Model.FindTableRedefinition(Ingot)?.Statement);
        Assert.Null(read.Model.FindTableRedefinition(Mould));
        Assert.Null(read.Model.FindProcedure(new ObjectName("dbo", "list_ingots"))!.Redefinition);
    }

    [Fact]
    public void A_deployment_script_that_names_a_table_through_a_variable_it_does_not_set_doubts_every_object()
    {
        // A build keeps $(Target) as written: its value is the deployment's, whatever the project's default.
        using var folder = new TempDirectory();
        var read = Smelter(folder, postdeploy: "ALTER TABLE dbo.$(Target) NOCHECK CONSTRAINT ALL;\n");

        var warning = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.DeploymentScriptNotRead, warning.Code);
        Assert.Equal(RedefiningStatement.UnreadScript, read.Model.FindTableRedefinition(Ingot)?.Statement);
        Assert.Equal(RedefiningStatement.UnreadScript, read.Model.FindTableRedefinition(Mould)?.Statement);
    }

    [Fact]
    public void A_deployment_script_reads_a_variable_it_sets()
    {
        // As a build writes an include inlined after the :setvar before it.
        using var folder = new TempDirectory();
        var read = Smelter(folder, postdeploy: ":setvar Target ingot\nALTER TABLE dbo.$(Target) NOCHECK CONSTRAINT ALL;\n");

        Assert.Empty(read.Diagnostics);
        Assert.Equal(new SourceLocation("smelter.dacpac/postdeploy.sql", 2, 1), Assert.Single(read.Model.FindTable(Ingot)!.Checks).DisabledAt);
        Assert.Null(read.Model.FindTableRedefinition(Mould));
    }

    [Fact]
    public void A_package_records_its_name_and_the_sha256_of_its_bytes()
    {
        var path = Path.Combine(Packages, "atelier", "atelier.dacpac");

        Assert.Equal("atelier.dacpac", Atelier.Value.Package!.Name);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), Atelier.Value.Package.Sha256);
    }

    [Fact]
    public void A_built_package_reads_without_errors() =>
        Assert.DoesNotContain(Atelier.Value.Diagnostics, d => d.IsError);

    [Fact]
    public void A_column_s_type_nullability_identity_and_collation_are_read_from_its_element()
    {
        var kiln = Kiln(Atelier.Value);

        Assert.Equal(new SqlType(SqlTypeKind.BuiltIn, "int", []), kiln.Column("kiln_id")!.Type);
        Assert.True(kiln.Column("kiln_id")!.IsIdentity);
        Assert.Equal(DeclaredNullability.NotNull, kiln.Column("kiln_id")!.Nullability);
        Assert.Equal(new SqlType(SqlTypeKind.BuiltIn, "char", ["3"]), kiln.Column("glaze")!.Type);
        Assert.Equal("Latin1_General_BIN2", kiln.Column("glaze")!.Collation);
        Assert.Equal(DeclaredNullability.Null, kiln.Column("fired_at")!.Nullability);
    }

    [Fact]
    public void A_column_s_mask_and_a_module_run_as_its_owner_are_read_from_their_elements()
    {
        var model = WideWorldImporters.Value.Model;
        var suppliers = model.FindTable(new ObjectName("Purchasing", "Suppliers"))!;

        Assert.Equal("default()", suppliers.Column("BankAccountName")!.Mask);
        Assert.Null(suppliers.Column("SupplierName")!.Mask);

        // WideWorldImporters' own procedures run WITH EXECUTE AS OWNER, which its header keeps; a function without it runs as its caller.
        Assert.True(model.FindProcedure(new ObjectName("Application", "Configuration_ApplyPartitioning"))!.RunsAsAnother);
        Assert.Contains(model.Procedures, p => !p.RunsAsAnother);
    }

    [Fact]
    public void A_column_of_an_alias_type_names_it_with_its_schema_and_takes_its_not_null()
    {
        var label = Kiln(Atelier.Value).Column("label")!;

        Assert.Equal(new SqlType(SqlTypeKind.Named, "code", [], "studio"), label.Type);
        Assert.Equal(DeclaredNullability.NotNull, label.Nullability);
        Assert.Equal(new AliasType(Studio("code"), Atelier.Value.Model.FindUserType(Studio("code"))!.Span, new SqlType(SqlTypeKind.BuiltIn, "varchar", ["8"]), DeclaredNullability.NotNull), Atelier.Value.Model.FindUserType(Studio("code")));
    }

    [Fact]
    public void A_computed_column_keeps_its_expression_and_has_no_type()
    {
        var column = Kiln(Atelier.Value).Column("peak_f")!;

        Assert.True(column.IsComputed);
        Assert.Null(column.Type);
    }

    [Fact]
    public void A_check_written_on_a_column_is_that_column_s_and_one_on_the_table_is_the_table_s()
    {
        var checks = Kiln(Atelier.Value).Checks;

        Assert.Equal("glaze", Assert.Single(checks, c => c.Name is null && c.Column is not null).Column);
        Assert.Null(checks.Single(c => c.Name == "ck_kiln_id").Column);
    }

    [Fact]
    public void A_check_disabled_by_nocheck_is_recorded_as_disabled()
    {
        var check = Kiln(Atelier.Value).Checks.Single(c => c.Name == "ck_kiln_id");

        Assert.Equal(check.At, check.DisabledAt);
    }

    [Fact]
    public void A_check_a_build_added_by_alter_table_is_not_read_since_the_package_does_not_say_whether_it_is_trusted() =>
        Assert.DoesNotContain(Kiln(Atelier.Value).Checks, c => c.Name == "ck_kiln_late");

    [Fact]
    public void A_check_not_for_replication_says_so() =>
        Assert.True(Kiln(Atelier.Value).Checks.Single(c => c.Name == "ck_kiln_peak").IsNotForReplication);

    [Fact]
    public void Unique_keys_are_read_wherever_they_were_created_but_a_filtered_index_is_not_one()
    {
        var keys = Kiln(Atelier.Value).UniqueKeys.Select(k => k.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["ix_kiln_label", "ix_kiln_serial_peak", "pk_kiln", "uq_kiln_serial"], keys);
        Assert.Equal(["kiln_id"], Kiln(Atelier.Value).PrimaryKey);
    }

    [Fact]
    public void A_temporal_table_s_period_columns_are_hidden_and_its_history_table_is_read()
    {
        var pot = Atelier.Value.Model.FindTable(Studio("pot"))!;

        Assert.True(pot.Column("valid_from")!.IsHidden);
        Assert.NotNull(Atelier.Value.Model.FindTable(Studio("pot_history")));
    }

    [Fact]
    public void A_graph_table_is_marked_and_its_pseudo_columns_are_not_listed()
    {
        var taught = Atelier.Value.Model.FindTable(Studio("taught"))!;

        Assert.True(taught.IsGraph);
        Assert.Empty(taught.Columns);
        Assert.Equal(["name"], Atelier.Value.Model.FindTable(Studio("potter"))!.Columns.Select(c => c.Name));
    }

    [Fact]
    public void A_cascading_foreign_key_is_read()
    {
        var key = Assert.Single(Atelier.Value.Model.CascadingKeysTo(Studio("kiln")));

        Assert.Equal(Studio("pot"), key.Referencing);
    }

    [Fact]
    public void A_synonym_keeps_the_name_it_stands_for() =>
        Assert.Equal("studio.kiln", Atelier.Value.Model.Synonyms.Single().For.Text);

    [Fact]
    public void A_procedure_created_with_ansi_nulls_off_says_so()
    {
        Assert.True(Atelier.Value.Model.FindProcedure(Studio("nulls_off"))!.AnsiNullsOff);
        Assert.False(Atelier.Value.Model.FindProcedure(Studio("list_pots"))!.AnsiNullsOff);
    }

    [Fact]
    public void A_procedure_created_with_quoted_identifier_off_reads_a_double_quoted_word_as_a_string()
    {
        var body = (SqlBody)Atelier.Value.Model.FindProcedure(Studio("quoted_off"))!.Body;
        var select = (Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement)body.Statements.Statements.Single();
        var column = (Microsoft.SqlServer.TransactSql.ScriptDom.SelectScalarExpression)((Microsoft.SqlServer.TransactSql.ScriptDom.QuerySpecification)select.QueryExpression).SelectElements.Single();

        Assert.IsType<Microsoft.SqlServer.TransactSql.ScriptDom.StringLiteral>(column.Expression);
    }

    [Fact]
    public void An_encrypted_procedure_in_a_built_package_is_read_from_the_text_the_project_gave() =>
        Assert.IsType<SqlBody>(Atelier.Value.Model.FindProcedure(Studio("secret"))!.Body);

    [Fact]
    public void An_encrypted_procedure_in_an_extracted_package_has_an_encrypted_body_not_its_placeholder() =>
        Assert.IsType<EncryptedBody>(AtelierExtract.Value.Model.FindProcedure(Studio("secret"))!.Body);

    [Fact]
    public void An_extracted_package_marks_each_check_the_server_does_not_trust()
    {
        var checks = Kiln(AtelierExtract.Value).Checks;

        Assert.NotNull(checks.Single(c => c.Name == "ck_kiln_id").DisabledAt);
        Assert.NotNull(checks.Single(c => c.Name == "ck_kiln_peak").DisabledAt);
        Assert.Null(checks.Single(c => c.Name == "ck_kiln_late").DisabledAt);
    }

    /// <summary>
    /// A table's keys by name, each with whether it holds (its index can't have been disabled): <c>studio.kiln</c>'s and
    /// <c>studio.shelf</c>'s.
    /// </summary>
    private static Dictionary<string, bool> KeysHeld(DacpacReadResult read) =>
        new[] { Studio("kiln"), Studio("shelf") }.SelectMany(t => read.Model.FindTable(t)!.UniqueKeys)
            .ToDictionary(k => k.Name!, k => k.DisabledAt is null, StringComparer.Ordinal);

    [Fact]
    public void An_extract_marks_a_disabled_unique_index_but_nothing_on_a_key_constraint_whose_index_is_disabled()
    {
        // build.sh extract disables the indexes of pk_shelf, uq_kiln_serial and ix_kiln_serial_peak before it extracts, and the
        // server then holds two shelves with one id and two kilns with one serial (decision 0120).
        using var zip = ZipFile.OpenRead(Path.Combine(Packages, "versions", "atelier-extract.dacpac"));
        var elements = XDocument.Load(zip.GetEntry("model.xml")!.Open()).Descendants().Where(e => e.Name.LocalName == "Element").ToList();
        string? Disabled(string type, string name) => elements.Single(e => (string?)e.Attribute("Type") == type && (string?)e.Attribute("Name") == name)
            .Elements().SingleOrDefault(p => p.Name.LocalName == "Property" && (string?)p.Attribute("Name") == "IsDisabled")?.Attribute("Value")?.Value;

        Assert.Equal("True", Disabled("SqlIndex", "[studio].[kiln].[ix_kiln_serial_peak]"));
        Assert.Null(Disabled("SqlIndex", "[studio].[kiln].[ix_kiln_label]"));
        Assert.Null(Disabled("SqlPrimaryKeyConstraint", "[studio].[pk_shelf]"));
        Assert.Null(Disabled("SqlUniqueConstraint", "[studio].[uq_kiln_serial]"));
    }

    [Fact]
    public void An_extract_s_primary_keys_and_unique_constraints_are_no_key_and_its_unique_indexes_hold_unless_marked_disabled()
    {
        var held = KeysHeld(AtelierExtract.Value);

        Assert.False(held["pk_kiln"]);
        Assert.False(held["pk_shelf"]);
        Assert.False(held["uq_kiln_serial"]);
        Assert.True(held["ix_kiln_label"]);
        Assert.False(held["ix_kiln_serial_peak"]);
    }

    [Fact]
    public void A_built_package_s_keys_hold() =>
        // A build refuses ALTER INDEX on a constraint's index (SQL71001), so its keys are the database's once deployed.
        Assert.All(KeysHeld(Atelier.Value), k => Assert.True(k.Value, k.Key));

    [Fact]
    public void An_extract_declared_a_build_keeps_its_key_constraints_and_still_reads_its_checks_as_an_extract()
    {
        // The declaration is the user's assumption about the keys alone; the checks the extract marks are read as before.
        var read = DacpacReader.Read(Path.Combine(Packages, "versions", "atelier-extract.dacpac"), builds: ["Atelier-Extract.dacpac"]);

        Assert.All(KeysHeld(read).Where(k => k.Key != "ix_kiln_serial_peak"), k => Assert.True(k.Value, k.Key));
        Assert.False(KeysHeld(read)["ix_kiln_serial_peak"]);
        Assert.Contains(Kiln(read).Checks, c => c.Name == "ck_kiln_late");
        Assert.False(KeysHeld(DacpacReader.Read(Path.Combine(Packages, "versions", "atelier-extract.dacpac"), builds: ["atelier.dacpac"]))["pk_shelf"]);
    }

    [Theory]
    [InlineData("shelf_by_id", IrCardinality.AtMostOne, IrCardinality.Many)]
    [InlineData("kiln_by_serial", IrCardinality.AtMostOne, IrCardinality.Many)]
    [InlineData("kiln_by_label", IrCardinality.AtMostOne, IrCardinality.AtMostOne)]
    [InlineData("kiln_by_serial_and_peak", IrCardinality.AtMostOne, IrCardinality.Many)]
    public void A_lookup_by_a_key_is_at_most_one_row_from_a_build_and_from_an_extract_only_by_an_index_not_marked_disabled(string procedure, IrCardinality built, IrCardinality extracted)
    {
        Assert.Equal(built, Assert.Single(Infer("atelier", "atelier.dacpac", procedure).Objects[0].Results!).Cardinality);
        Assert.Equal(extracted, Assert.Single(Infer("versions", "atelier-extract.dacpac", procedure).Objects[0].Results!).Cardinality);
    }

    [Fact]
    public void A_location_in_a_module_names_the_package_and_the_quoted_object_and_counts_lines_in_the_module_s_text()
    {
        // The procedure's text starts with a one-line comment, so CREATE is on its second line.
        var odd = Atelier.Value.Model.Procedures.Single(p => p.Name.Name == "get:kiln/one");

        Assert.Equal("atelier.dacpac/[studio].[get:kiln/one]:2:1", odd.Span.Start.ToString());
        Assert.True(SourceLocation.TryParse(odd.Span.Start.ToString(), out var parsed));
        Assert.Equal("atelier.dacpac/[studio].[get:kiln/one]", parsed.File);
    }

    [Fact]
    public void A_module_s_lines_are_counted_from_its_own_text_in_an_extracted_package_as_in_a_built_one() =>
        Assert.Equal(
            Atelier.Value.Model.FindProcedure(Studio("list_pots"))!.Span.Start,
            AtelierExtract.Value.Model.FindProcedure(Studio("list_pots"))!.Span.Start with { File = "atelier.dacpac/[studio].[list_pots]" });

    [Fact]
    public void A_table_s_parts_are_located_by_their_place_in_its_list()
    {
        var kiln = Kiln(Atelier.Value);

        Assert.Equal(["atelier.dacpac/[studio].[kiln]:1:1", "atelier.dacpac/[studio].[kiln]:2:1"], kiln.Columns.Take(2).Select(c => c.At.ToString()));
        Assert.All(kiln.Checks, c => Assert.True(c.At.Line > kiln.Columns.Count));
        Assert.Equal(kiln.Checks.Count, kiln.Checks.Select(c => c.At).Distinct().Count());
    }

    [Fact]
    public void The_database_s_settings_are_recorded()
    {
        var settings = DacpacReader.Read(Path.Combine(Packages, "corpus", "WideWorldImporters.dacpac")).Model.Settings!;

        Assert.Equal("Latin1_General_100_CI_AS", settings.Collation);
        Assert.Equal(130, settings.CompatibilityLevel);
        Assert.True(settings.AnsiWarnings);
        Assert.True(settings.QuotedIdentifier);
    }

    [Fact]
    public void An_extract_records_the_collation_and_takes_the_level_of_the_provider_it_names()
    {
        // The extract was taken at its provider's level, so it records none (observed); the collation is there regardless.
        var settings = AtelierExtract.Value.Model.Settings!;

        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", settings.Collation);
        Assert.Equal(170, settings.CompatibilityLevel);
    }

    [Fact]
    public void Every_option_is_read_as_the_package_writes_it_absent_at_dacfx_s_default()
    {
        var on = Atelier.Value.Model.Settings;
        var off = DacpacReader.Read(Path.Combine(Packages, "versions", "options-off.dacpac")).Model.Settings;

        Assert.Equal(new DatabaseSettings("SQL_Latin1_General_CP1_CI_AS", 170, true, true, true, true, true, true, false, true), on);
        Assert.Equal(new DatabaseSettings("Japanese_CI_AS_KS_WS", 150, false, false, false, false, false, false, false, false), off);
    }

    [Fact]
    public void A_computed_column_persisted_not_null_is_not_null() =>
        Assert.Equal(DeclaredNullability.NotNull, Kiln(Atelier.Value).Column("peak_k")!.Nullability);

    [Fact]
    public void An_encrypted_scalar_function_in_an_extract_keeps_its_return_type_and_parameters()
    {
        var function = Assert.IsType<ScalarFunction>(AtelierExtract.Value.Model.FindFunction(Studio("secret_double")));

        Assert.IsType<EncryptedBody>(function.Body);
        Assert.Equal(new SqlType(SqlTypeKind.BuiltIn, "int", []), function.Returns);
        Assert.Equal(["@x"], function.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void An_encrypted_inline_function_in_an_extract_returns_its_listed_columns_each_nullable()
    {
        var function = Assert.IsType<TableFunction>(AtelierExtract.Value.Model.FindFunction(Studio("secret_kilns_over")));

        Assert.IsType<EncryptedBody>(function.Body);
        Assert.Equal(["kiln_id", "label"], function.Returns.Columns.Select(c => c.Name));
        Assert.All(function.Returns.Columns, c => Assert.Equal(DeclaredNullability.Null, c.Nullability));
    }

    [Fact]
    public void An_encrypted_view_in_an_extract_keeps_its_column_names_and_no_query()
    {
        var view = Assert.IsType<View>(AtelierExtract.Value.Model.FindReadable(Studio("secret_kilns")));

        Assert.Null(view.Query);
        Assert.Equal(["kiln_id"], view.Columns);
    }

    [Fact]
    public void A_clr_procedure_and_function_are_read_with_their_assembly_class_and_method()
    {
        Assert.Equal(new ClrBody("KilnClr", "Kiln", "Report"), Clr.Value.Model.FindProcedure(Studio("report"))!.Body);
        var function = Assert.IsType<ScalarFunction>(Clr.Value.Model.FindFunction(Studio("doubled")));
        Assert.Equal(new ClrBody("KilnClr", "Kiln", "Double"), function.Body);
        Assert.Equal(new SqlType(SqlTypeKind.BuiltIn, "int", []), function.Returns);
    }

    /// <summary>The IR of one procedure of a package, with no config.</summary>
    private static IrDocument Infer(string group, string package, string procedure) =>
        SqlProject.LoadPackage(Path.Combine(Packages, group, package), []).Infer([new ObjectName("studio", procedure)]).Expected();

    [Theory]
    [InlineData("calls_secret_fn", "doubled", "int")]
    [InlineData("reads_secret_tvf", "kiln_id", "int")]
    [InlineData("reads_secret_tvf", "label", "varchar(8)")]
    public void What_reads_an_encrypted_function_of_an_extract_is_typed_by_what_its_element_declares(string procedure, string field, string sql)
    {
        var type = Field(Infer("versions", "atelier-extract.dacpac", procedure), field).Type;

        Assert.Equal(sql, Assert.IsType<IrScalar>(type).Sql);
    }

    [Fact]
    public void What_reads_an_encrypted_view_of_an_extract_is_unknown_for_that_reason()
    {
        var doc = Infer("versions", "atelier-extract.dacpac", "reads_secret_view");

        Assert.Equal(IrUnknownReason.EncryptedDefinition, Assert.Single(doc.Unknowns).Reason);
    }

    [Fact]
    public void What_calls_a_clr_function_is_typed_by_its_declared_return_type_and_a_clr_procedure_is_external_code()
    {
        var answer = Field(Infer("versions", "kiln-clr-extract.dacpac", "uses_clr"), "answer").Type;

        Assert.Equal("int", Assert.IsType<IrScalar>(answer).Sql);
        Assert.Equal(IrUnknownReason.ExternalCode, Assert.Single(Infer("versions", "kiln-clr-extract.dacpac", "report").Unknowns).Reason);
    }

    /// <summary>A copy of <paramref name="package"/> in <paramref name="dir"/> whose <c>Origin.xml</c> is changed by <paramref name="change"/>, or left out when it gives null.</summary>
    private static string WithOrigin(TempDirectory dir, string package, Func<string, string?> change)
    {
        var path = Path.Combine(dir.Path, Path.GetFileName(package));
        File.Copy(Path.Combine(Packages, package), path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var origin = zip.GetEntry("Origin.xml")!;
        string text;
        using (var reader = new StreamReader(origin.Open()))
            text = reader.ReadToEnd();
        origin.Delete();
        if (change(text) is { } changed)
        {
            using var writer = new StreamWriter(zip.CreateEntry("Origin.xml").Open(), new UTF8Encoding(false));
            writer.Write(changed);
        }

        return path;
    }

    [Fact]
    public void A_package_neither_built_nor_extracted_as_far_as_its_origin_says_trusts_no_unmarked_check_and_no_key_constraint()
    {
        // A build marks no check added WITH NOCHECK, so a package not positively an extract must not trust one (ck_kiln_late); an
        // extract marks no key constraint whose index is disabled, so one not positively a build must not trust its keys.
        using var dir = new TempDirectory();
        var path = WithOrigin(dir, Path.Combine("versions", "atelier-extract.dacpac"), text => Regex.Replace(text, "<Server>.*?</Server>", "", RegexOptions.Singleline)
            .Replace("Microsoft.SqlServer.Dac,", "Some.Other.Tool,", StringComparison.Ordinal));
        var read = DacpacReader.Read(path);

        Assert.DoesNotContain(Kiln(read).Checks, c => c.Name == "ck_kiln_late");
        Assert.Contains(Kiln(AtelierExtract.Value).Checks, c => c.Name == "ck_kiln_late");
        Assert.False(KeysHeld(read)["uq_kiln_serial"]);
        Assert.True(KeysHeld(read)["ix_kiln_label"]);
    }

    [Theory]
    [InlineData("Microsoft.Data.Tools.Schema.Tasks.Sql", true)]
    [InlineData("Microsoft.SqlServer.Dac.Extensions", false)]
    [InlineData("Microsoft.SqlServer.Dac", false)]
    [InlineData("Some.Other.Tool", false)]
    [InlineData(null, false)]
    public void A_package_is_a_build_only_when_its_origin_names_no_server_and_the_build_task(string? product, bool built)
    {
        // Microsoft.Build.Sql and SSDT's build task write the first; DacFx's API (DacPackageExtensions.BuildPackage) the second,
        // whatever it wrote, an extract loaded and written again included; an extract the third, with its server (observed,
        // decision 0120). Without an Origin.xml nothing says what made it.
        using var dir = new TempDirectory();
        var path = WithOrigin(dir, Path.Combine("atelier", "atelier.dacpac"), text => product is null ? null
            : text.Replace("Microsoft.Data.Tools.Schema.Tasks.Sql,", product + ",", StringComparison.Ordinal));

        Assert.Equal(built, KeysHeld(DacpacReader.Read(path))["uq_kiln_serial"]);
    }

    [Fact]
    public void A_location_is_rooted_at_the_folder_its_own_package_was_found_in()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "app"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "shared"));
        File.Copy(Path.Combine(Packages, "harbour", "harbour.dacpac"), Path.Combine(dir.Path, "app", "harbour.dacpac"));
        foreach (var name in new[] { "crane", "quay", "tally" })
            File.Copy(Path.Combine(Packages, "harbour", name + ".dacpac"), Path.Combine(dir.Path, "shared", name + ".dacpac"));

        var project = SqlProject.LoadPackage(Path.Combine(dir.Path, "app", "harbour.dacpac"), [Path.Combine(dir.Path, "shared")]);

        Assert.Equal(Path.Combine(dir.Path, "shared"), project.FolderOf("crane.dacpac/[dock].[crane]"));
        Assert.Equal(Path.Combine(dir.Path, "app"), project.FolderOf("harbour.dacpac/[dock].[list_cranes]"));
    }

    [Fact]
    public void A_folder_records_no_settings() =>
        Assert.Null(SqlScriptFolder.Read(Fixture.Named("orders").Database).Model.Settings);

    [Fact]
    public void Elements_not_modelled_are_counted_in_one_note()
    {
        // The CLR extract's assembly is not modelled; WideWorldImporters' table types and sequences are, so it has no note.
        var note = Assert.Single(Clr.Value.Diagnostics, d => d.Code == SourceCodes.NotModelled);

        Assert.Contains("1 SqlAssembly", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(WideWorldImporters.Value.Diagnostics, d => d.Code == SourceCodes.NotModelled);
    }

    [Fact]
    public void A_sequence_is_read_with_its_type_and_bigint_when_it_declares_none()
    {
        // A build writes no type for a sequence without AS, and an alias type by its name. Built from projects/turnstile.
        var model = DacpacReader.Read(Path.Combine(Packages, "turnstile", "turnstile.dacpac")).Model;
        string? Type(string name) => model.FindSequence(new ObjectName("gate", name))?.Type?.ToString();

        Assert.Equal("int", Type("ticket"));
        Assert.Equal("tinyint", Type("lane"));
        Assert.Equal("decimal(10,0)", Type("visit"));
        Assert.Equal("numeric(5,0)", Type("token"));
        Assert.Equal("bigint", Type("pass"));
        Assert.Equal("small_count", Type("booth"));
        Assert.Equal(6, model.Sequences.Count);
    }

    [Fact]
    public void A_folder_its_project_file_and_its_package_give_each_sequence_the_same_type()
    {
        // A folder keeps what was written (numeric(5)), a package what DacFx resolved (numeric(5,0)): the same type once resolved.
        // The project file reads the same scripts as the folder does, through its Build items.
        var folder = SqlScriptFolder.Read(Path.Combine(Packages, "projects", "turnstile")).Model;
        var project = SqlProjectReader.Read(Path.Combine(Packages, "projects", "turnstile", "turnstile.sqlproj")).Model;
        var package = DacpacReader.Read(Path.Combine(Packages, "turnstile", "turnstile.dacpac")).Model;
        static string?[] Types(DatabaseModel model) =>
            [.. model.Sequences.Select(s => $"{s.Name} {SqlServer.Semantics.SchemaRules.ResolveType(model, s.Type)?.Scalar?.Declaration}").Order(StringComparer.Ordinal)];

        Assert.Equal(6, folder.Sequences.Count);
        Assert.Equal(Types(folder), Types(package));
        Assert.Equal(Types(folder), Types(project));
    }

    [Fact]
    public void Next_value_for_a_packages_sequence_is_its_type()
    {
        var doc = SqlProject.LoadPackage(Path.Combine(Packages, "turnstile", "turnstile.dacpac"), []).Infer([new ObjectName("gate", "admit")]).Expected();
        string Sql(string name) => Assert.IsType<IrScalar>(Field(doc, name).Type).Sql!;

        Assert.Equal("tinyint", Sql("lane"));
        Assert.Equal("smallint", Sql("booth"));
        Assert.Equal("bigint", Sql("pass"));
        Assert.Equal("decimal(10,0)", Sql("visit"));
        Assert.Equal("numeric(5,0)", Sql("token"));
        Assert.Empty(Field(doc, "booth").NullableBecause);
    }

    [Fact]
    public void A_table_type_is_read_from_its_own_element_with_its_parts()
    {
        // In a package a table type's column says IsNullable only when nullable, the reverse of a table's; its constraints and
        // defaults are entries of its own element. Built from tests/fixtures/till.
        var model = DacpacReader.Read(Path.Combine(Packages, "fixtures", "till.dacpac")).Model;
        TableType Type(string name) => Assert.IsType<TableType>(model.FindUserType(new ObjectName("till", name)));

        Assert.Equal([DeclaredNullability.NotNull, DeclaredNullability.NotNull, DeclaredNullability.Null], Type("line_list").Columns.Select(c => c.Nullability));
        Assert.Equal("quantity", Assert.Single(Type("line_list").Checks).Column);
        Assert.Equal([false, true], Type("tag_list").Columns.Select(c => c.HasDefault));
        Assert.Equal([["tag"]], Type("tag_list").UniqueKeys.Select(k => k.Columns.ToArray()));
        Assert.True(Assert.Single(Type("echo_list").UniqueKeys).IgnoresDuplicates);
        Assert.Equal([(true, false), (false, false), (false, true)], Type("serial_list").Columns.Select(c => (c.IsIdentity, c.IsComputed)));
        Assert.Equal(["first col", "select"], Type("odd list").Columns.Select(c => c.Name));
    }

    [Fact]
    public void A_package_whose_every_element_is_read_has_no_note_of_what_isn_t()
    {
        // atelier's one element of a kind once passed over, its table type, is read now.
        Assert.DoesNotContain(Atelier.Value.Diagnostics, d => d.Code == SourceCodes.NotModelled);
        Assert.IsType<TableType>(Atelier.Value.Model.FindUserType(new ObjectName("studio", "firing")));
    }

    [Fact]
    public void Packages_of_the_same_database_are_read_through_a_chain_of_references()
    {
        var model = Harbour.Value.Model;

        Assert.NotNull(model.FindTable(new ObjectName("dock", "crane")));
        Assert.NotNull(model.FindTable(new ObjectName("dock", "berth")));
        Assert.Equal("quay.dacpac/[dock].[berth]", model.FindTable(new ObjectName("dock", "berth"))!.Span.File);
    }

    [Fact]
    public void Every_reference_is_recorded_once_with_its_database_and_whether_it_was_found()
    {
        var references = Harbour.Value.References.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(["crane.dacpac", "master.dacpac", "quay.dacpac", "tally.dacpac"], references.Select(r => r.Name));
        Assert.Equal([null, "master", null, "$(Tally)"], references.Select(r => r.Database));
        Assert.Equal([true, false, true, true], references.Select(r => r.Found is not null));
    }

    [Fact]
    public void Another_database_s_package_is_found_but_not_read_into_the_model() =>
        Assert.Null(Harbour.Value.Model.FindTable(new ObjectName("ledger", "entry")));

    [Fact]
    public void A_reference_not_found_is_one_warning_and_no_error()
    {
        var warning = Assert.Single(Harbour.Value.Diagnostics, d => d.Code == SourceCodes.ReferenceNotFound);

        Assert.Contains("master.dacpac", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Harbour.Value.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void A_package_of_the_same_database_not_found_is_listed_as_unread_so_a_missing_name_can_say_where_it_may_be()
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(Packages, "harbour", "harbour.dacpac"), Path.Combine(dir.Path, "harbour.dacpac"));

        var read = DacpacReader.Read(Path.Combine(dir.Path, "harbour.dacpac"));

        Assert.Equal([new UnreadPart("crane.dacpac", "harbour.dacpac"), new UnreadPart("quay.dacpac", "harbour.dacpac")], read.Model.Unread.OrderBy(p => p.Name, StringComparer.Ordinal));
        Assert.Equal(4, read.Diagnostics.Count(d => d.Code == SourceCodes.ReferenceNotFound));
    }

    [Fact]
    public void A_reference_is_found_in_a_folder_the_config_names_when_it_is_not_beside_its_referrer()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "app"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "shared"));
        File.Copy(Path.Combine(Packages, "harbour", "harbour.dacpac"), Path.Combine(dir.Path, "app", "harbour.dacpac"));
        foreach (var name in new[] { "crane", "quay", "tally" })
            File.Copy(Path.Combine(Packages, "harbour", name + ".dacpac"), Path.Combine(dir.Path, "shared", name + ".dacpac"));

        var read = DacpacReader.Read(Path.Combine(dir.Path, "app", "harbour.dacpac"), [Path.Combine(dir.Path, "shared")]);

        Assert.NotNull(read.Model.FindTable(new ObjectName("dock", "berth")));
        Assert.Single(read.Diagnostics, d => d.Code == SourceCodes.ReferenceNotFound);
    }

    [Fact]
    public void A_reference_is_looked_for_beside_its_referrer_then_in_the_config_s_folders_and_only_then_where_it_was_built()
    {
        var reference = new PackageReference("/build/quay/bin/Debug/quay.dacpac", "quay.dacpac", null);

        var candidates = ReferenceFinder.Candidates(reference, "/work/app/harbour.dacpac", ["/shared"]);

        Assert.Equal([Path.GetFullPath("/work/app/quay.dacpac"), Path.GetFullPath("/shared/quay.dacpac"), Path.GetFullPath("/build/quay/bin/Debug/quay.dacpac")], candidates);
    }

    [Theory]
    [InlineData(@"C:\build\bin\Debug\other.dacpac")]
    [InlineData(@"\build\other.dacpac")]
    public void A_path_rooted_the_Windows_way_is_listed_as_recorded_and_never_under_the_referrer_s_folder(string recorded)
    {
        using var dir = new TempDirectory();
        var package = Path.Combine(dir.Path, "app.dacpac");
        TestPackage.Write(package, TestPackage.Procedure("dbo", "p", "SELECT 1 AS one"), references: new TestPackage.Reference("other.dacpac", Recorded: recorded));

        var read = DacpacReader.Read(package);

        var warning = Assert.Single(read.Diagnostics, d => d.Code == SourceCodes.ReferenceNotFound);
        var looked = ReferenceFinder.Candidates(new PackageReference(recorded, "other.dacpac", null), package, []);
        Assert.Equal([Path.Combine(dir.Path, "other.dacpac")], looked.Where(place => place.StartsWith(dir.Path, StringComparison.Ordinal)));
        Assert.EndsWith($"looked for at {DiagnosticFormatter.PathAsShown(Path.Combine(dir.Path, "other.dacpac"))}, {recorded}. Put it beside app.dacpac, or name its folder in \"references\" in the config file.", warning.Message, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(recorded, looked[^1]);
    }

    // On Windows, looking for a file on a share is a connection to its host, which is offered the user's credentials: a package
    // someone else built, or one inside a NuGet package, would choose the host. Each spelling below names a share or a
    // device on Windows (\??\ is the NT object manager's own prefix, which .NET passes through as a device path), and the test is
    // the same on every platform, so a Mac or Linux run catches a share being tried again.
    [Theory]
    [InlineData(@"\\host\share\x.dacpac")]
    [InlineData("//host/share/x.dacpac")]
    [InlineData(@"/\host\share\x.dacpac")]
    [InlineData(@"\\?\UNC\host\share\x.dacpac")]
    [InlineData(@"\\?\C:\build\x.dacpac")]
    [InlineData(@"\\.\pipe\x.dacpac")]
    [InlineData(@"\??\UNC\host\share\x.dacpac")]
    [InlineData(@"\??\GLOBALROOT\Device\Mup\host\share\x.dacpac")]
    public void A_recorded_path_naming_a_share_or_a_device_is_never_looked_for_and_the_warning_says_so(string recorded)
    {
        using var dir = new TempDirectory();
        var app = Path.Combine(dir.Path, "app");
        var shared = Path.Combine(dir.Path, "shared");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(shared);
        var package = Path.Combine(app, "app.dacpac");
        TestPackage.Write(package, TestPackage.Procedure("dbo", "p", "SELECT 1 AS one"), references: new TestPackage.Reference("other.dacpac", Recorded: recorded));

        var looked = ReferenceFinder.Candidates(new PackageReference(recorded, "other.dacpac", null), package, [shared]);
        var read = DacpacReader.Read(package, [shared]);

        Assert.Equal([Path.Combine(app, "x.dacpac"), Path.Combine(app, "other.dacpac"), Path.Combine(shared, "x.dacpac"), Path.Combine(shared, "other.dacpac")], looked);
        var warning = Assert.Single(read.Diagnostics, d => d.Code == SourceCodes.ReferenceNotFound);
        Assert.Contains($"other.dacpac, and not at {recorded}, which names a share or a device, where haruspex never looks. Put it beside", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_defined_in_two_packages_of_one_database_is_reported_as_defined_twice()
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(Packages, "harbour", "harbour.dacpac"), Path.Combine(dir.Path, "harbour.dacpac"));
        File.Copy(Path.Combine(Packages, "harbour", "crane.dacpac"), Path.Combine(dir.Path, "crane.dacpac"));
        File.Copy(Path.Combine(Packages, "harbour", "crane.dacpac"), Path.Combine(dir.Path, "quay.dacpac"));

        var read = DacpacReader.Read(Path.Combine(dir.Path, "harbour.dacpac"));

        Assert.Contains(read.Diagnostics, d => d.Code == SourceCodes.DefinedTwice && d.Message.Contains("dock.crane", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_is_not_a_package_is_one_error_and_an_empty_model()
    {
        using var dir = new TempDirectory();
        var path = dir.File("broken.dacpac", "not a zip");

        var read = DacpacReader.Read(path);

        Assert.Equal(SourceCodes.UnreadablePackage, Assert.Single(read.Diagnostics).Code);
        Assert.Empty(read.Model.Tables);
        Assert.Null(read.Package);
    }

    [Fact]
    public void A_chain_of_a_hundred_packages_of_one_database_is_read_whole_opening_each_once()
    {
        // Each part refers to the next two, and the last to the first, so most packages are met through more than one reference.
        // What is counted is the packages opened, not the time taken, which a loaded machine stretches.
        using var dir = new TempDirectory();
        for (var i = 0; i < 100; i++)
        {
            TestPackage.Reference[] next = i < 98 ? [new($"part{i + 1}.dacpac"), new($"part{i + 2}.dacpac")]
                : i == 98 ? [new("part99.dacpac")]
                : [new("part0.dacpac")];
            TestPackage.Write(Path.Combine(dir.Path, $"part{i}.dacpac"), TestPackage.Table("dbo", $"t{i}", "id int"), references: next);
        }

        var read = DacpacReader.Read(Path.Combine(dir.Path, "part0.dacpac"));

        Assert.Equal(100, read.Model.Tables.Count);
        Assert.Equal(99, read.References.Count);
        Assert.Empty(read.Diagnostics);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => $"part{i}.dacpac"), read.Opened.Select(Path.GetFileName));
    }

    [Fact]
    public void Another_database_s_reference_back_to_the_root_s_file_reuses_the_root_rather_than_opening_it_again()
    {
        // The root is opened through the package set, keyed by its full path, so another database that refers back to its file by a
        // database name finds the package already open.
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "main.dacpac"), TestPackage.Table("dbo", "t", "id int"), references: [new TestPackage.Reference("ledger.dacpac", "Ledger")]);
        TestPackage.Write(Path.Combine(dir.Path, "ledger.dacpac"), TestPackage.Table("dbo", "entry", "id int"), references: [new TestPackage.Reference("main.dacpac", "Main")]);

        var read = DacpacReader.Read(Path.Combine(dir.Path, "main.dacpac"));

        Assert.Equal(["main.dacpac", "ledger.dacpac"], read.Opened.Select(Path.GetFileName));
        Assert.NotNull(read.Model.Databases["Ledger"].FindTable(new ObjectName("dbo", "entry")));
    }

    [Fact]
    public void A_readonly_parameter_is_read_from_its_package_as_a_script_declares_it()
    {
        var parameters = WideWorldImporters.Value.Model.FindProcedure(new ObjectName("Website", "InsertCustomerOrders"))!.Parameters;

        Assert.All(parameters.Where(p => p.Name is "@Orders" or "@OrderLines"), p => Assert.True(p.IsReadOnly));
        Assert.All(parameters.Where(p => p.Name is not ("@Orders" or "@OrderLines")), p => Assert.False(p.IsReadOnly));
    }

    [Fact]
    public void A_deployment_script_that_may_move_a_synonym_to_another_target_is_defined_twice()
    {
        // As a project's deployment script is (decision 0112): which target stands once deployed isn't known.
        using var folder = new TempDirectory();
        var read = Smelter(folder, TestPackage.Synonym("dbo", "bin", "[dbo].[ingot]"), postdeploy: "IF @@SERVERNAME = N'x' BEGIN DROP SYNONYM dbo.bin; CREATE SYNONYM dbo.bin FOR dbo.mould; END\r\n");

        var twice = Assert.Single(read.Diagnostics, d => d.Code == SourceCodes.DefinedTwice);
        Assert.Equal(new SourceLocation("smelter.dacpac/postdeploy.sql", 1, 52), twice.At);
        Assert.Empty(read.Model.Synonyms);
    }
}

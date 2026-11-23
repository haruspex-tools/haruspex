using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Haruspex.Cli;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="DacpacReader"/> on the packages in <c>tests/dacpacs</c>: what each construct of <c>model.xml</c> becomes in the
/// model, how a built package and an extracted one differ, and how references are found, composed and reported. That a package
/// reads as its project's folder does is proved in <c>DacpacEquivalenceTests</c>.
/// </summary>
public class DacpacReaderTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    private static readonly Lazy<DacpacReadResult> Atelier = new(() => DacpacReader.Read(Path.Combine(Packages, "atelier", "atelier.dacpac")));

    private static readonly Lazy<DacpacReadResult> AtelierExtract = new(() => DacpacReader.Read(Path.Combine(Packages, "versions", "atelier-extract.dacpac")));

    private static readonly Lazy<DacpacReadResult> Clr = new(() => DacpacReader.Read(Path.Combine(Packages, "versions", "kiln-clr-extract.dacpac")));

    private static readonly Lazy<DacpacReadResult> Harbour = new(() => DacpacReader.Read(Path.Combine(Packages, "harbour", "harbour.dacpac")));

    private static readonly Lazy<DacpacReadResult> WideWorldImporters = new(() => DacpacReader.Read(Path.Combine(Packages, "corpus", "WideWorldImporters-ssdt.dacpac")));

    private static ObjectName Studio(string name) => new("studio", name);

    private static Table Kiln(DacpacReadResult read) => read.Model.FindTable(Studio("kiln"))!;

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

    [Fact]
    public void A_package_without_a_server_in_its_origin_is_read_as_a_build_whatever_made_it()
    {
        // A build marks no check added WITH NOCHECK, so a package not positively an extract must not trust one (ck_kiln_late).
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "atelier.dacpac");
        File.Copy(Path.Combine(Packages, "versions", "atelier-extract.dacpac"), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var origin = zip.GetEntry("Origin.xml")!;
            string text;
            using (var reader = new StreamReader(origin.Open()))
                text = reader.ReadToEnd();
            origin.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("Origin.xml").Open(), new UTF8Encoding(false));
            writer.Write(Regex.Replace(text, "<Server>.*?</Server>", "", RegexOptions.Singleline)
                .Replace("Microsoft.SqlServer.Dac,", "Some.Other.Tool,", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(Kiln(DacpacReader.Read(path)).Checks, c => c.Name == "ck_kiln_late");
        Assert.Contains(Kiln(AtelierExtract.Value).Checks, c => c.Name == "ck_kiln_late");
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
        var note = Assert.Single(WideWorldImporters.Value.Diagnostics, d => d.Code == SourceCodes.NotModelled);

        Assert.Contains("SqlSequence", note.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlTableType", note.Message, StringComparison.Ordinal);
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
    // someone else built, or one inside a NuGet package, would choose the host (F041). Each spelling below names a share or a
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
    public void A_chain_of_a_hundred_packages_of_one_database_is_read_whole_and_quickly()
    {
        using var dir = new TempDirectory();
        for (var i = 0; i < 100; i++)
            TestPackage.Write(Path.Combine(dir.Path, $"part{i}.dacpac"), TestPackage.Table("dbo", $"t{i}", "id int"), references: i < 99 ? [new TestPackage.Reference($"part{i + 1}.dacpac")] : []);

        var clock = Stopwatch.StartNew();
        var read = DacpacReader.Read(Path.Combine(dir.Path, "part0.dacpac"));

        Assert.Equal(100, read.Model.Tables.Count);
        Assert.Equal(99, read.References.Count);
        Assert.Empty(read.Diagnostics);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public void A_readonly_parameter_is_read_from_its_package_as_a_script_declares_it()
    {
        var parameters = WideWorldImporters.Value.Model.FindProcedure(new ObjectName("Website", "InsertCustomerOrders"))!.Parameters;

        Assert.All(parameters.Where(p => p.Name is "@Orders" or "@OrderLines"), p => Assert.True(p.IsReadOnly));
        Assert.All(parameters.Where(p => p.Name is not ("@Orders" or "@OrderLines")), p => Assert.False(p.IsReadOnly));
    }
}

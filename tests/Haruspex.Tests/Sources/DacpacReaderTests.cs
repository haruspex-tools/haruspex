using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Haruspex.SqlServer.Sources.Dacpacs;

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

    private static readonly Lazy<DacpacReadResult> Harbour = new(() => DacpacReader.Read(Path.Combine(Packages, "harbour", "harbour.dacpac")));

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
        Assert.True(Haruspex.Diagnostics.SourceLocation.TryParse(odd.Span.Start.ToString(), out var parsed));
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
    public void A_folder_records_no_settings() =>
        Assert.Null(SqlScriptFolder.Read(Fixture.Named("orders").Database).Model.Settings);

    [Fact]
    public void Elements_not_modelled_are_counted_in_one_note()
    {
        var note = Assert.Single(Atelier.Value.Diagnostics, d => d.Code == SourceCodes.NotModelled);

        Assert.Contains("1 SqlTableType", note.Message, StringComparison.Ordinal);
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

        Assert.Equal(["crane.dacpac, a package harbour.dacpac refers to that was not found", "quay.dacpac, a package harbour.dacpac refers to that was not found"], read.Model.Unread.Order(StringComparer.Ordinal));
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
            WritePackage(Path.Combine(dir.Path, $"part{i}.dacpac"), $"t{i}", i < 99 ? $"part{i + 1}.dacpac" : null);

        var clock = Stopwatch.StartNew();
        var read = DacpacReader.Read(Path.Combine(dir.Path, "part0.dacpac"));

        Assert.Equal(100, read.Model.Tables.Count);
        Assert.Equal(99, read.References.Count);
        Assert.Empty(read.Diagnostics);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    /// <summary>A package holding one table, referring to the next of the chain: the least a reader needs, made here.</summary>
    private static void WritePackage(string path, string table, string? next)
    {
        var reference = next is null ? "" : $"""<CustomData Category="Reference" Type="SqlSchema"><Metadata Name="FileName" Value="C:\build\{next}" /><Metadata Name="LogicalName" Value="{next}" /></CustomData>""";
        var model = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <DataSchemaModel xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
              <Header>{reference}</Header>
              <Model>
                <Element Type="SqlTable" Name="[dbo].[{table}]">
                  <Relationship Name="Columns"><Entry><Element Type="SqlSimpleColumn" Name="[dbo].[{table}].[id]">
                    <Property Name="IsNullable" Value="False" />
                    <Relationship Name="TypeSpecifier"><Entry><Element Type="SqlTypeSpecifier"><Relationship Name="Type"><Entry><References ExternalSource="BuiltIns" Name="[int]" /></Entry></Relationship></Element></Entry></Relationship>
                  </Element></Entry></Relationship>
                </Element>
              </Model>
            </DataSchemaModel>
            """;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("model.xml").Open(), new UTF8Encoding(false));
        writer.Write(model);
    }
}

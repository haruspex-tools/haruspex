using Haruspex.Cli;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources.Dacpacs;
using Haruspex.Tests.Sources;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// A three-part name read through a DACPAC's reference to another database (<see cref="OtherDatabases"/>), and the system's
/// views through <c>master</c>: what binds, what it carries from its own database, and what stays as it was. Packages built by
/// DacFx are in <c>tests/dacpacs/harbour</c>; the cases they can't show are packages the tests write (<c>TestPackage</c>).
/// </summary>
public class OtherDatabasesTests
{
    private static string Packages => Path.Combine(RepositoryPaths.Root, "tests", "dacpacs");

    /// <summary>The IR of <c>dbo.p</c>, read from app.dacpac, which refers to other.dacpac as <paramref name="database"/>.</summary>
    private static IrDocument Read(string body, string otherElements, string database = "$(Other)", string? collation = "Latin1_General_100_CS_AS", string? more = null)
    {
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "other.dacpac"), otherElements, collation, more is null ? [] : [new TestPackage.Reference("more.dacpac")]);
        if (more is not null)
            TestPackage.Write(Path.Combine(dir.Path, "more.dacpac"), more);
        TestPackage.Write(Path.Combine(dir.Path, "app.dacpac"), TestPackage.Procedure("dbo", "p", body), references: new TestPackage.Reference("other.dacpac", database));
        return SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();
    }

    private static readonly string Entry = TestPackage.Table("dbo", "entry", "id int", "note varchar(20)?");

    private static IrScalar Scalar(IrDocument doc, string field) => Assert.IsType<IrScalar>(Field(doc, field).Type);

    [Fact]
    public void A_table_of_another_database_is_typed_through_the_reference_s_variable()
    {
        var doc = Read("SELECT e.id, e.note FROM [$(Other)].dbo.entry AS e;", Entry);

        Assert.Equal("int", Scalar(doc, "id").Sql);
        Assert.False(Field(doc, "id").Nullable);
        Assert.Empty(doc.Unknowns);
    }

    [Fact]
    public void A_column_of_another_database_is_named_with_its_database_as_the_body_writes_it() =>
        Assert.Equal(
            "[$(Other)].dbo.entry.note",
            Assert.Single(Field(Read("SELECT e.note FROM [$(Other)].dbo.entry AS e;", Entry), "note").NullableBecause).At);

    [Fact]
    public void A_variable_is_not_taken_for_a_database_of_that_literal_name() =>
        Assert.Contains("to another database is not modelled yet", Assert.Single(Read("SELECT e.id FROM [Other].dbo.entry AS e;", Entry).Unknowns).Detail, StringComparison.Ordinal);

    [Fact]
    public void A_literal_database_is_bound_only_to_a_reference_of_that_literal()
    {
        Assert.Empty(Read("SELECT e.id FROM ledger.dbo.entry AS e;", Entry, database: "ledger").Unknowns);
        Assert.Single(Read("SELECT e.id FROM [$(ledger)].dbo.entry AS e;", Entry, database: "ledger").Unknowns);
    }

    [Fact]
    public void A_database_no_reference_names_stays_unknown_as_it_was() =>
        Assert.Equal(
            "The reference nowhere.dbo.entry to another database is not modelled yet.",
            Assert.Single(Read("SELECT e.id FROM nowhere.dbo.entry AS e;", Entry).Unknowns).Detail);

    [Fact]
    public void A_name_of_another_database_without_a_schema_depends_on_the_caller_there_and_is_unknown() =>
        Assert.Contains("names no schema", Assert.Single(Read("SELECT e.id FROM [$(Other)]..entry AS e;", Entry).Unknowns).Detail, StringComparison.Ordinal);

    [Fact]
    public void A_column_of_another_database_s_alias_type_takes_the_type_it_stands_for_and_its_not_null()
    {
        var doc = Read(
            "SELECT k.code FROM [$(Other)].dbo.kind AS k;",
            TestPackage.AliasType("dbo", "code", "varchar", 5, notNull: true) + TestPackage.Table("dbo", "kind", "code dbo.code"));

        Assert.Equal("varchar(5)", Scalar(doc, "code").Sql);
        Assert.False(Field(doc, "code").Nullable);
    }

    [Fact]
    public void A_column_of_another_database_takes_that_database_s_collation_not_this_one_s()
    {
        var read = DacpacReader.Read(Path.Combine(Packages, "harbour", "harbour.dacpac")).Model;
        var tally = read.Databases["$(Tally)"];

        var entry = OtherDatabases.AsSeenHere(tally.FindTable(new ObjectName("ledger", "entry"))!, tally, "$(Tally)")!;

        Assert.Equal("Latin1_General_100_CI_AS_SC_UTF8", read.Settings!.Collation);
        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", entry.Column("state")!.Collation);
        Assert.Equal("Latin1_General_BIN2", OtherDatabases.AsSeenHere(new Table(new ObjectName("dbo", "t"), entry.Span, [entry.Column("state")! with { Collation = "Latin1_General_BIN2" }], [], [], []), tally, "$(Tally)")!.Column("state")!.Collation);
    }

    [Fact]
    public void A_table_of_a_database_whose_package_records_no_collation_is_not_read() =>
        Assert.Contains("records no collation", Assert.Single(Read("SELECT e.id FROM [$(Other)].dbo.entry AS e;", Entry, collation: null).Unknowns).Detail, StringComparison.Ordinal);

    [Fact]
    public void A_table_of_another_database_keeps_its_checks_and_their_value_sets()
    {
        var doc = SqlProject.LoadPackage(Path.Combine(Packages, "harbour", "harbour.dacpac"), []).Infer([new ObjectName("dock", "crane_fees")]).Expected();

        Assert.Equal("decimal(9,2)", Scalar(doc, "fee").Sql);
        Assert.Equal(["paid", "due"], Scalar(doc, "state").Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void A_synonym_here_for_a_table_of_another_database_reads_it()
    {
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "other.dacpac"), Entry);
        TestPackage.Write(
            Path.Combine(dir.Path, "app.dacpac"),
            TestPackage.Synonym("dbo", "entries", "[$(Other)].dbo.entry") + TestPackage.Procedure("dbo", "p", "SELECT e.id FROM dbo.entries AS e;"),
            references: new TestPackage.Reference("other.dacpac", "$(Other)"));

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.Equal("int", Scalar(doc, "id").Sql);
    }

    [Fact]
    public void A_synonym_of_another_database_for_one_of_its_tables_reads_that_table() =>
        Assert.Equal("int", Scalar(Read("SELECT e.id FROM [$(Other)].dbo.entries AS e;", Entry + TestPackage.Synonym("dbo", "entries", "dbo.entry")), "id").Sql);

    [Fact]
    public void A_view_of_another_database_is_unknown_with_the_reason_that_it_is_not_read_across_databases_yet() =>
        Assert.Equal(
            "[$(Other)].dbo.v is a view of the database $(Other), which is not read across databases yet.",
            Assert.Single(Read("SELECT v.id FROM [$(Other)].dbo.v AS v;", Entry + TestPackage.View("dbo", "v", "SELECT e.id FROM dbo.entry AS e;")).Unknowns).Detail);

    [Fact]
    public void Another_database_s_own_packages_of_its_database_are_read_with_it() =>
        Assert.Equal("int", Scalar(Read("SELECT e.id FROM [$(Other)].dbo.entry AS e;", TestPackage.Table("dbo", "unused", "id int"), more: Entry), "id").Sql);

    [Fact]
    public void A_system_view_master_lists_binds_by_its_two_part_name_and_its_columns_are_unknown_for_that_reason()
    {
        using var dir = new TempDirectory();
        foreach (var name in new[] { "harbour", "crane", "quay", "tally" })
            File.Copy(Path.Combine(Packages, "harbour", name + ".dacpac"), Path.Combine(dir.Path, name + ".dacpac"));
        File.Copy(Path.Combine(Packages, "master-shape", "master.dacpac"), Path.Combine(dir.Path, "master.dacpac"));

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "harbour.dacpac"), []).Infer([new ObjectName("dock", "own_tables")]).Expected();

        var unknown = Assert.Single(doc.Unknowns);
        Assert.Equal(IrUnknownReason.UnsupportedSource, unknown.Reason);
        Assert.Equal("sys.objects is a system view, whose columns master.dacpac lists without their types.", unknown.Detail);
    }

    [Fact]
    public void An_information_schema_view_binds_as_a_system_view_does()
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(Packages, "master-shape", "master.dacpac"), Path.Combine(dir.Path, "master.dacpac"));
        TestPackage.Write(Path.Combine(dir.Path, "app.dacpac"), TestPackage.Procedure("dbo", "p", "SELECT t.TABLE_NAME FROM INFORMATION_SCHEMA.TABLES AS t;"), references: new TestPackage.Reference("master.dacpac", "master"));

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.Contains("is a system view", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_master_a_system_view_is_missing_as_it_was() =>
        Assert.StartsWith(
            "There is no table sys.objects in the project.",
            Assert.Single(SqlProject.LoadPackage(Path.Combine(Packages, "harbour", "harbour.dacpac"), []).Infer([new ObjectName("dock", "own_tables")]).Expected().Unknowns).Detail,
            StringComparison.Ordinal);

    [Fact]
    public void Microsoft_s_master_dacpac_has_the_shape_the_stand_in_has()
    {
        // Microsoft's package can't be committed; it is in the NuGet cache wherever a project referring to it was built.
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var master = Directory.Exists(Path.Combine(cache, "microsoft.sqlserver.dacpacs.master"))
            ? Directory.EnumerateFiles(Path.Combine(cache, "microsoft.sqlserver.dacpacs.master"), "master.dacpac", SearchOption.AllDirectories).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (master is null)
        {
            Assert.SkipWhen(Environment.GetEnvironmentVariable("HARUSPEX_REQUIRE_MASTER_DACPAC") != "1", "master.dacpac is not in the NuGet cache (microsoft.sqlserver.dacpacs.master), so its shape isn't checked.");
            Assert.Fail("HARUSPEX_REQUIRE_MASTER_DACPAC is set, and master.dacpac is not in the NuGet cache.");
        }

        var model = DacpacReader.Read(master).Model;
        var objects = Assert.IsType<View>(model.FindReadable(new ObjectName("sys", "objects")));
        Assert.Null(objects.Query);
        Assert.False(objects.IsEncrypted);
        Assert.Contains("name", objects.Columns);
        Assert.NotNull(model.FindReadable(new ObjectName("INFORMATION_SCHEMA", "TABLES")));
    }

    /// <summary>The unknowns of <c>dbo.p</c> read from a folder of the scripts given.</summary>
    private static List<IrUnknown> FromFolder(params string[] scripts) =>
        [.. InferProcedure.Infer("dbo.p", [.. scripts.Select((sql, i) => ($"s{i}.sql", sql))]).Unknowns];

    [Theory]
    [InlineData("CREATE SYNONYM dbo.a FOR dbo.b;", "CREATE SYNONYM dbo.b FOR dbo.a;")]
    [InlineData("CREATE SYNONYM dbo.a FOR dbo.a;", "CREATE TABLE dbo.unused (id int NOT NULL);")]
    [InlineData("CREATE SYNONYM dbo.a FOR dbo.b;", "CREATE SYNONYM dbo.b FOR dbo.t;")]
    public void A_synonym_for_a_synonym_is_unknown_since_sql_server_refuses_a_chain_and_a_cycle_ends(string first, string second)
    {
        // Observed: Synonym_chaining_is_refused_when_read (Msg 470), for a chain, a cycle and a synonym for itself.
        var unknown = Assert.Single(FromFolder("CREATE TABLE dbo.t (id int NOT NULL);", first, second, "CREATE PROCEDURE dbo.p AS SELECT x.id FROM dbo.a AS x;"));

        Assert.Contains("Msg 470", unknown.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_synonym_here_for_a_synonym_of_another_database_is_unknown_as_a_chain()
    {
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "other.dacpac"), Entry + TestPackage.Synonym("dbo", "entries", "dbo.entry"));
        TestPackage.Write(
            Path.Combine(dir.Path, "app.dacpac"),
            TestPackage.Synonym("dbo", "remote", "[$(Other)].dbo.entries") + TestPackage.Procedure("dbo", "p", "SELECT e.id FROM dbo.remote AS e;"),
            references: new TestPackage.Reference("other.dacpac", "$(Other)"));

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.Contains("Msg 470", Assert.Single(doc.Unknowns).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_synonym_of_another_database_for_one_of_its_synonyms_is_unknown_as_a_chain() =>
        Assert.Contains(
            "Msg 470",
            Assert.Single(Read("SELECT e.id FROM [$(Other)].dbo.a AS e;", Entry + TestPackage.Synonym("dbo", "a", "dbo.b") + TestPackage.Synonym("dbo", "b", "dbo.entry")).Unknowns).Detail,
            StringComparison.Ordinal);

    [Fact]
    public void A_column_of_another_database_s_type_that_is_no_alias_there_is_never_taken_for_a_type_here()
    {
        // Here dbo.code is an alias of int; there it is a CLR type. The column must not become an int.
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "other.dacpac"), """<Element Type="SqlUserDefinedType" Name="[dbo].[code]" />""" + TestPackage.Table("dbo", "kind", "code dbo.code"));
        TestPackage.Write(
            Path.Combine(dir.Path, "app.dacpac"),
            TestPackage.AliasType("dbo", "code", "int") + TestPackage.Procedure("dbo", "p", "SELECT k.code FROM [$(Other)].dbo.kind AS k;"),
            references: new TestPackage.Reference("other.dacpac", "$(Other)"));

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.IsType<IrUnknownType>(Field(doc, "code").Type);
    }

    [Fact]
    public void A_package_both_databases_hold_is_read_into_each()
    {
        using var dir = new TempDirectory();
        TestPackage.Write(Path.Combine(dir.Path, "common.dacpac"), TestPackage.Table("common", "cfg", "k int"));
        TestPackage.Write(Path.Combine(dir.Path, "other.dacpac"), Entry, references: new TestPackage.Reference("common.dacpac"));
        TestPackage.Write(
            Path.Combine(dir.Path, "app.dacpac"),
            TestPackage.Procedure("dbo", "p", "SELECT c.k, o.k AS other_k FROM common.cfg AS c CROSS JOIN [$(Other)].common.cfg AS o;"),
            references: [new TestPackage.Reference("common.dacpac"), new TestPackage.Reference("other.dacpac", "$(Other)")]);

        var doc = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []).Infer([new ObjectName("dbo", "p")]).Expected();

        Assert.Empty(doc.Unknowns);
        Assert.Equal("int", Scalar(doc, "other_k").Sql);
    }

    [Fact]
    public void A_fact_from_another_database_s_check_names_the_check_with_its_database()
    {
        var doc = SqlProject.LoadPackage(Path.Combine(Packages, "harbour", "harbour.dacpac"), []).Infer([new ObjectName("dock", "crane_fees")]).Expected();

        Assert.Equal("[$(Tally)].ledger.ck_entry_state", Assert.Single(Scalar(doc, "state").Facets!.Values!.From).Ref);
    }

    [Fact]
    public void Master_s_system_objects_that_are_not_read_are_not_noted_on_every_run()
    {
        using var dir = new TempDirectory();
        File.Copy(Path.Combine(Packages, "master-shape", "master.dacpac"), Path.Combine(dir.Path, "master.dacpac"));
        TestPackage.Write(Path.Combine(dir.Path, "app.dacpac"), TestPackage.Procedure("dbo", "p", "SELECT 1 AS one;"), references: new TestPackage.Reference("master.dacpac", "master"));

        var project = SqlProject.LoadPackage(Path.Combine(dir.Path, "app.dacpac"), []);

        Assert.Empty(project.Diagnostics);
    }
}

using Haruspex.Cli;
using Haruspex.Emit.Zod;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// Canonical value sets: a CHECK's set of strings on a <c>char</c> or <c>varchar</c> column of a collation whose fold is proven,
/// mapped to the member a value equals, and every other set a note
/// (<c>docs/decisions/0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md</c>). That a fold is the server's equality
/// is shown against the server (<c>CollationFoldGridTests</c>, <c>CanonicalValueSetObservationTests</c>); these hold what is built
/// on it.
/// </summary>
public class CanonicalValueSetTests
{
    private const string Status = """
        CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, status varchar(10) NOT NULL CHECK (status IN ('open', 'closed')));
        GO
        CREATE PROCEDURE dbo.p AS SELECT t.status FROM dbo.t AS t;
        GO
        """;

    /// <summary>The project of <paramref name="sql"/>, read as a folder whose database's default collation is <paramref name="collation"/>.</summary>
    private static IrDocument Infer(string sql, string? collation)
    {
        using var dir = new TempDirectory();
        dir.File("db.sql", sql);
        var project = SqlProject.Load(dir.Path, collation);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Procedures).Expected();
    }

    private static IrValueSet? Values(IrDocument doc) =>
        Assert.IsType<IrScalar>(Assert.Single(Assert.Single(doc.Types).Value.Fields).Type).Facets?.Values;

    [Fact]
    public void The_fold_drops_what_is_ignored_then_maps_each_class_then_trims_trailing_spaces()
    {
        var fold = CollationFold.For("Latin1_General_100_CI_AS", StringKind.CodePage)!;

        Assert.Equal("PAID", fold.Fold("Pa­id"));
        Assert.Equal("PAID", fold.Fold("paid ­"));
        Assert.Equal("SS", fold.Fold("ß"));
        Assert.Equal("CAFé", fold.Fold("café"));
    }

    [Fact]
    public void A_collation_is_found_by_its_name_in_any_case_and_only_for_a_code_page() =>
        Assert.Equal(
            (true, false, false),
            (CollationFold.For("latin1_general_100_ci_as", StringKind.CodePage) is not null,
                CollationFold.For("Latin1_General_100_CI_AS", StringKind.Unicode) is not null,
                CollationFold.For("French_CI_AS", StringKind.CodePage) is not null));

    [Theory]
    [InlineData("open", true)]
    [InlineData("on-hold 2.b_x", true)]
    [InlineData("", false)]
    [InlineData("café", false)]
    [InlineData("a/b", false)]
    public void A_plain_string_is_made_of_ascii_letters_digits_space_underscore_hyphen_and_full_stop(string value, bool plain) =>
        Assert.Equal(plain, CollationFold.IsPlain(value));

    [Fact]
    public void A_column_without_collate_takes_the_collation_the_config_declares_for_a_folder()
    {
        var values = Values(Infer(Status, "Latin1_General_100_CI_AS"))!;

        Assert.Equal((IrValueMatch.Canonical, "Latin1_General_100_CI_AS.codepage"), (values.Match, values.Fold));
        Assert.Equal(["closed", "open"], values.Items.Select(i => i.Text));
    }

    [Fact]
    public void Without_a_collation_a_column_without_collate_keeps_its_note() =>
        Assert.Equal(IrValueMatch.Collation, Values(Infer(Status, null))!.Match);

    [Fact]
    public void A_temporary_table_s_column_takes_no_default_since_its_collation_is_tempdb_s()
    {
        var doc = Infer(
            """
            CREATE PROCEDURE dbo.p AS
            CREATE TABLE #t (status varchar(10) NOT NULL CHECK (status IN ('open', 'closed')));
            SELECT t.status FROM #t AS t;
            GO
            """,
            "Latin1_General_100_CI_AS");

        Assert.NotEqual(IrValueMatch.Canonical, Values(doc)?.Match);
    }

    [Fact]
    public void A_source_that_records_its_collation_keeps_it_over_the_one_declared()
    {
        var model = new DatabaseModel([], [], [], [], [], [], [], [], []) { DefaultCollation = "SQL_Latin1_General_CP1_CI_AS" };

        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", model.WithDeclaredCollation("Latin1_General_100_CI_AS").DefaultCollation);
        Assert.Equal("Latin1_General_100_CI_AS", new DatabaseModel([], [], [], [], [], [], [], [], []).WithDeclaredCollation("Latin1_General_100_CI_AS").DefaultCollation);
    }

    [Fact]
    public void The_document_carries_exactly_the_folds_its_sets_name_as_the_server_showed_them()
    {
        var doc = Infer(Status, "Latin1_General_100_CI_AS");
        var fold = CollationFold.For("Latin1_General_100_CI_AS", StringKind.CodePage)!;

        Assert.Equal(new IrFold(fold.Ignored, fold.Classes), Assert.Single(doc.Folds).Value);
        Assert.Empty(Infer(Status, null).Folds);
    }

    [Fact]
    public void The_zod_maps_a_value_to_its_member_before_the_enum()
    {
        var zod = ZodEmitter.Emit(Infer(Status, "Latin1_General_100_CI_AS")).Expected();

        Assert.Contains(
            "z.preprocess(canonicalOf(folds[\"Latin1_General_100_CI_AS.codepage\"], [\"closed\", \"open\"]), z.enum([\"closed\", \"open\"]))",
            zod,
            StringComparison.Ordinal);
        Assert.Contains("ignored: \"\\u0000\\u00ad\"", zod, StringComparison.Ordinal);
        Assert.Contains("[\"AE\", \"\\u00c6\\u00e6\"]", zod, StringComparison.Ordinal);
    }

    [Fact]
    public void A_canonical_set_an_override_makes_exact_leaves_its_fold_out_of_the_document()
    {
        using var dir = new TempDirectory();
        dir.File("db.sql", Status);
        var project = SqlProject.Load(dir.Path, "Latin1_General_100_CI_AS");
        var config = ConfigReader.Read(
            """{ "procedures": { "dbo.p": { "paths": { "[].status": { "values": "exact", "why": "written in lower case only" } } } } }"""u8.ToArray(),
            "haruspex.json",
            dir.Path).Expected();

        var doc = project.Infer(project.Procedures, config).Expected();

        Assert.Equal((IrValueMatch.Exact, null), (Values(doc)!.Match, Values(doc)!.Fold));
        Assert.Empty(doc.Folds);
    }
}

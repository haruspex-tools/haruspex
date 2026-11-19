using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A table-valued parameter, judged by the definitions of its type the two documents carry: what the running application sends,
/// rows of its definition, must be what the database's procedure takes. <c>BaselineFitTests</c> calls each pair through the old
/// function on a server.
/// </summary>
public partial class IrFitTests
{
    private const string Lines = "CREATE TYPE shop.lines AS TABLE (id int NOT NULL, qty smallint NOT NULL, note nvarchar(20) NULL, PRIMARY KEY (id));";

    private const string AddLines = "CREATE PROCEDURE shop.add_lines @lines shop.lines READONLY AS SELECT 1 AS one;";

    /// <summary>The document of <c>shop.add_lines</c> over <c>shop.lines</c> as <paramref name="type"/> defines it.</summary>
    private static IrDocument Taking(string type, string procedure = AddLines) =>
        InferProcedure.Infer("shop.*", ("tables.sql", Orders), ("type.sql", type), ("procedures.sql", procedure));

    public static TheoryData<string, string, string?> TableTypeChanges { get; } = new()
    {
        { "nothing", Lines, null },
        { "a default added", Edit(Lines, "note nvarchar(20) NULL", "note nvarchar(20) NULL DEFAULT N'none'"), null },
        { "a column that became nullable", Edit(Lines, "qty smallint NOT NULL", "qty smallint NULL"), null },
        { "a length widened", Edit(Lines, "nvarchar(20)", "nvarchar(40)"), null },
        { "an integer widened", Edit(Lines, "qty smallint", "qty int"), null },
        { "a key removed", Edit(Lines, ", PRIMARY KEY (id)", ""), null },
        { "a key over more columns", Edit(Lines, "PRIMARY KEY (id)", "PRIMARY KEY (id, qty)"), null },
        { "a column added", Edit(Lines, "note nvarchar(20) NULL,", "note nvarchar(20) NULL, extra int NULL,"), "as many columns as it knew" },
        { "a column removed", Edit(Lines, " note nvarchar(20) NULL,", ""), "as many columns as it knew" },
        { "two columns swapped", Edit(Lines, "id int NOT NULL, qty smallint NOT NULL", "qty smallint NOT NULL, id int NOT NULL"), "matches columns by place" },
        { "a column renamed", Edit(Lines, "note nvarchar", "remark nvarchar"), "matches columns by place" },
        { "a column that became NOT NULL", Edit(Lines, "note nvarchar(20) NULL", "note nvarchar(20) NOT NULL"), "error 515" },
        { "a length narrowed", Edit(Lines, "nvarchar(20)", "nvarchar(10)"), "length changed" },
        { "an integer narrowed", Edit(Lines, "qty smallint", "qty tinyint"), "no longer takes" },
        { "a check added", Edit(Lines, "qty smallint NOT NULL", "qty smallint NOT NULL CHECK (qty > 0)"), "error 547" },
        { "a key added", Edit(Lines, "PRIMARY KEY (id)", "PRIMARY KEY (id), UNIQUE (note)"), "error 2627" },
        { "a key over fewer columns", Edit(Lines, "id int NOT NULL, qty smallint NOT NULL, note nvarchar(20) NULL, PRIMARY KEY (id)", "id int NOT NULL, qty smallint NOT NULL, note nvarchar(20) NULL, PRIMARY KEY (id), UNIQUE (qty)"), "error 2627" },
        { "a key that now ignores duplicates", Edit(Lines, "PRIMARY KEY (id)", "PRIMARY KEY (id) WITH (IGNORE_DUP_KEY = ON)"), null },
        { "a column the server fills added", Edit(Lines, "note nvarchar(20) NULL,", "note nvarchar(20) NULL, serial int IDENTITY,"), "can't be shown to take" },
        { "a collation given", Edit(Lines, "note nvarchar(20) NULL", "note nvarchar(20) COLLATE Latin1_General_100_BIN2 NULL"), "another collation" },
    };

    [Theory]
    [MemberData(nameof(TableTypeChanges))]
    public void A_table_type_changed_under_its_name_fits_exactly_when_what_the_application_sends_is_still_taken(string change, string after, string? refused)
    {
        var findings = IrFit.Of(Taking(Lines), Taking(after), new IrFitOptions());

        if (refused is null)
        {
            Assert.True(findings.Count == 0, $"{change}: {string.Join("; ", findings)}");
        }
        else
        {
            var finding = Assert.Single(findings);
            Assert.Equal(IrFitVerdict.Refused, finding.Verdict);
            Assert.StartsWith("parameters[\"@lines\"]", finding.Change.Path, StringComparison.Ordinal);
            Assert.Contains(refused, finding.Why + " " + finding.Change.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_key_that_ignored_duplicates_and_now_refuses_them_is_refused()
    {
        var ignoring = Edit(Lines, "PRIMARY KEY (id)", "PRIMARY KEY (id) WITH (IGNORE_DUP_KEY = ON)");

        Assert.Contains("error 2627", Assert.Single(IrFit.Of(Taking(ignoring), Taking(Lines), new IrFitOptions())).Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_table_type_of_the_same_columns_is_refused()
    {
        // Observed: the server refuses a table of another type, whatever its columns (206).
        var twin = Lines.Replace("shop.lines", "shop.twin", StringComparison.Ordinal);

        Assert.Equal(
            ["refused: shop.add_lines parameters[\"@lines\"]: table type changed from [shop].[lines] (id int not null, qty smallint not null, note nvarchar(200); key (id)) to [shop].[twin] (id int not null, qty smallint not null, note nvarchar(20); key (id))".Replace("nvarchar(200)", "nvarchar(20)", StringComparison.Ordinal)],
            Fit(Taking(Lines), Taking(Lines + "\n" + twin, AddLines.Replace("shop.lines", "shop.twin", StringComparison.Ordinal))));
    }

    [Fact]
    public void A_parameter_that_became_table_valued_or_stopped_being_is_refused()
    {
        var plain = Taking(Lines, "CREATE PROCEDURE shop.add_lines @lines int = NULL AS SELECT 1 AS one;");

        Assert.Equal(["refused: shop.add_lines parameters[\"@lines\"]: now a table type, [shop].[lines] (id int not null, qty smallint not null, note nvarchar(20); key (id))"], Fit(plain, Taking(Lines)));
        Assert.Single(Fit(Taking(Lines), plain));
    }

    [Fact]
    public void An_unchanged_table_type_no_row_can_be_sent_in_fits_and_a_changed_one_does_not()
    {
        var serials = "CREATE TYPE shop.lines AS TABLE (serial int IDENTITY, note nvarchar(20) NULL);";

        Assert.Empty(Fit(Taking(serials), Taking(serials)));
        Assert.Single(Fit(Taking(serials), Taking(Edit(serials, "nvarchar(20)", "nvarchar(40)"))));
    }
}

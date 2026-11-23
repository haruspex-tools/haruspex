using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Which definition of a procedure, function or view the model keeps (<c>DefinitionsInEffect</c>, after <c>ScriptDefinitions</c>
/// has read each script in batch order): within one script the last stands, as it does when the script runs; across scripts,
/// whose order a folder doesn't give, a second definition stays a doubt
/// (<c>docs/decisions/0080-the-last-definition-in-a-script-stands.md</c>). Observed in <c>ExecObservationTests.Definitions.cs</c>.
/// </summary>
public class DefinitionsInEffectTests
{
    private static readonly ObjectName ArchiveOrders = new("shop", "archive_orders");

    private const string Stub = "create procedure shop.archive_orders as return 0;";

    private const string Altered = "alter procedure shop.archive_orders @before date as select o.order_id from shop.customer_order as o where o.placed_on < @before;";

    private static SchemaReadResult Read(params (string File, string Text)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, text) in files)
            dir.File(file, text);
        return SqlScriptFolder.Read(dir.Path);
    }

    private static Procedure Archive(SchemaReadResult read) => read.Model.FindProcedure(ArchiveOrders) ?? throw new InvalidOperationException("No shop.archive_orders.");

    [Fact]
    public void A_stub_created_and_then_altered_in_one_script_is_its_alter()
    {
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\n{Altered}"));

        var archive = Archive(read);
        Assert.Empty(read.Diagnostics);
        Assert.Null(archive.Redefinition);
        Assert.Equal(3, archive.Span.Start.Line);
        Assert.Equal("@before", Assert.Single(archive.Parameters).Name);
    }

    [Theory]
    [InlineData("drop procedure shop.archive_orders;")]
    [InlineData("drop procedure if exists shop.archive_orders;")]
    [InlineData("if object_id(N'shop.archive_orders') is not null drop procedure shop.archive_orders;")]
    [InlineData("if object_id('shop.archive_orders', 'P') is not null begin drop procedure shop.archive_orders; end")]
    public void A_procedure_dropped_and_created_again_in_one_script_is_its_last_create(string drop)
    {
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\n{drop}\nGO\n{Altered.Replace("alter", "create", StringComparison.Ordinal)}"));

        var archive = Archive(read);
        Assert.Empty(read.Diagnostics);
        Assert.Null(archive.Redefinition);
        Assert.Equal(5, archive.Span.Start.Line);
    }

    [Fact]
    public void A_drop_under_a_condition_that_may_be_false_leaves_which_definition_stands_in_doubt()
    {
        // If the condition is false, the second CREATE fails (2714) and the first stands; if true, the second does.
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\nif @@servername = N'x' drop procedure shop.archive_orders;\nGO\n{Altered.Replace("alter", "create", StringComparison.Ordinal)}"));

        Assert.Equal(RedefiningStatement.DropProcedure, Archive(read).Redefinition?.Statement);
    }

    [Theory]
    [InlineData("if object_id(N'shop.archive_orders', N'V') is not null drop procedure shop.archive_orders;")]
    [InlineData("if object_id(N'shop.archive_orders', N'PC') is not null drop procedure shop.archive_orders;")]
    public void A_drop_under_an_object_id_test_that_may_not_find_the_object_dropped_is_a_doubt(string drop)
    {
        // OBJECT_ID with a type finds only an object of that type: a view's V, a CLR procedure's PC, not a procedure's P.
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\n{drop}\nGO\n{Altered.Replace("alter", "create", StringComparison.Ordinal)}"));

        Assert.Equal(RedefiningStatement.DropProcedure, Archive(read).Redefinition?.Statement);
    }

    [Theory]
    [InlineData("if object_id(N'archive_orders') is not null drop procedure dbo.archive_orders;")]
    [InlineData("if object_id(N'dbo.archive_orders') is not null drop procedure archive_orders;")]
    public void A_drop_whose_guard_names_its_object_with_a_schema_only_on_one_side_is_a_doubt(string drop)
    {
        // An unqualified name means the caller's default schema, which needn't be dbo: the guard may test another object.
        var read = Read(("archive_orders.sql", $"create procedure dbo.archive_orders as return 0;\nGO\n{drop}\nGO\ncreate procedure dbo.archive_orders @before date as select @before as before;"));

        var archive = read.Model.FindProcedure(new ObjectName("dbo", "archive_orders"))!;
        Assert.Equal(RedefiningStatement.DropProcedure, archive.Redefinition?.Statement);
    }

    [Fact]
    public void A_function_dropped_under_an_object_id_test_of_another_kind_is_a_doubt_and_of_its_own_kind_is_not()
    {
        // OBJECT_ID(…, N'FN') is null for an inline function, so the drop doesn't run and the second CREATE is refused (2714).
        static Function Total(string type) => Assert.Single(Read(("order_total.sql", $"""
            create function shop.order_total() returns table as return select 1 as a;
            GO
            if object_id(N'shop.order_total', N'{type}') is not null drop function shop.order_total;
            GO
            create function shop.order_total() returns table as return select 'x' as b;
            """)).Model.Functions);

        Assert.Equal(RedefiningStatement.DropFunction, Total("FN").Redefinition?.Statement);
        Assert.Null(Total("IF").Redefinition);
        Assert.Equal(5, Total("IF").Span.Start.Line);
    }

    [Fact]
    public void Create_or_alter_twice_in_one_script_is_its_second()
    {
        var read = Read(("archive_orders.sql", $"create or alter procedure shop.archive_orders as return 0;\nGO\n{Altered.Replace("alter", "create or alter", StringComparison.Ordinal)}"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal("@before", Assert.Single(Archive(read).Parameters).Name);
    }

    [Fact]
    public void A_second_create_in_one_script_is_refused_by_the_server_so_the_first_stands_and_is_reported()
    {
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\n{Altered.Replace("alter", "create", StringComparison.Ordinal)}"));

        Assert.Equal(SourceCodes.DefinedTwice, Assert.Single(read.Diagnostics).Code);
        Assert.Empty(Archive(read).Parameters);
    }

    [Fact]
    public void A_procedure_created_and_then_dropped_in_one_script_is_not_in_the_model()
    {
        var read = Read(("archive_orders.sql", $"{Stub}\nGO\ndrop procedure shop.archive_orders;"));

        Assert.Null(read.Model.FindProcedure(ArchiveOrders));
    }

    [Fact]
    public void A_procedure_only_altered_whose_stub_is_dynamic_sql_is_its_alter()
    {
        // The stub is made by code that would have to run, so only the ALTER is seen; whatever stood before, the ALTER replaces
        // it whole, and nothing else in the project changes it after.
        var read = Read(("archive_orders.sql", $"if object_id(N'shop.archive_orders') is null exec sp_executesql N'{Stub}';\nGO\n{Altered}"));

        var archive = Archive(read);
        Assert.Empty(read.Diagnostics);
        Assert.Null(archive.Redefinition);
        Assert.Equal("@before", Assert.Single(archive.Parameters).Name);
    }

    [Theory]
    [InlineData("a.sql", Stub, "b.sql", Altered)]
    [InlineData("a.sql", Altered, "b.sql", Altered)]
    [InlineData("a.sql", Altered, "b.sql", "drop procedure shop.archive_orders;")]
    [InlineData("a.sql", Stub, "b.sql", "drop procedure if exists shop.archive_orders;\nGO\n" + Stub)]
    public void Definitions_in_two_scripts_whose_order_a_folder_does_not_give_stay_a_doubt(string first, string one, string second, string other)
    {
        var read = Read((first, one), (second, other));

        Assert.True(Archive(read).Redefinition is not null || read.Diagnostics.Any(d => d.Code == SourceCodes.DefinedTwice));
    }

    [Fact]
    public void A_stub_created_in_one_script_and_altered_in_another_is_a_doubt_as_before()
    {
        var read = Read(("a.sql", Stub), ("b.sql", Altered));

        Assert.Equal(RedefiningStatement.AlterProcedure, Archive(read).Redefinition?.Statement);
    }

    [Fact]
    public void The_definition_that_stands_keeps_the_ansi_nulls_setting_in_force_where_it_is()
    {
        // An ALTER takes the setting in force when it runs, not the stub's (observed: An_alter_keeps_the_ansi_nulls_setting_in_force_when_it_runs).
        var read = Read(("archive_orders.sql", $"set ansi_nulls off;\nGO\n{Stub}\nGO\nset ansi_nulls on;\nGO\n{Altered}"));

        Assert.False(Archive(read).AnsiNullsOff);
        Assert.True(Archive(Read(("archive_orders.sql", $"{Stub}\nGO\nset ansi_nulls off;\nGO\n{Altered}"))).AnsiNullsOff);
    }

    [Fact]
    public void A_function_and_a_view_follow_the_same_rule()
    {
        var read = Read(("objects.sql", """
            create function shop.order_total(@order_id int) returns int as begin return 0; end
            GO
            alter function shop.order_total(@order_id int) returns decimal(9,2) as begin return 1.5; end
            GO
            create view shop.open_orders as select 1 as stub;
            GO
            drop view if exists shop.open_orders;
            GO
            create view shop.open_orders as select 1 as order_id;
            GO
            """));

        Assert.Empty(read.Diagnostics);
        var total = Assert.Single(read.Model.Functions);
        Assert.Null(total.Redefinition);
        Assert.Equal(3, total.Span.Start.Line);
        var view = Assert.Single(read.Model.Views);
        Assert.Null(view.Redefinition);
        Assert.Equal(9, view.Span.Start.Line);
    }

    [Theory]
    [InlineData("alter")]
    [InlineData("create or alter")]
    public void A_function_altered_to_another_kind_is_refused_so_the_earlier_definition_stands(string change)
    {
        // A function's kind can't change in place (2010, observed: A_function_s_kind_cannot_change_by_alter).
        var read = Read(("order_total.sql", $"""
            create function shop.order_total(@order_id int) returns int as begin return 0; end
            GO
            {change} function shop.order_total(@order_id int) returns table as return select 1 as total;
            """));

        var total = Assert.Single(read.Model.Functions);
        Assert.IsType<ScalarFunction>(total);
        Assert.Null(total.Redefinition);
        Assert.Equal(1, total.Span.Start.Line);
    }

    [Fact]
    public void A_function_only_altered_is_a_doubt_since_the_kind_of_what_it_alters_is_unseen()
    {
        // Unlike a procedure's, a function's ALTER may be refused by what stood before it: a stub of another kind (2010).
        var read = Read(("order_total.sql", "if object_id(N'shop.order_total') is null exec sp_executesql N'create function shop.order_total() returns int as begin return 0; end';\nGO\nalter function shop.order_total() returns table as return select 1 as total;"));

        Assert.Equal(RedefiningStatement.AlterFunction, Assert.Single(read.Model.Functions).Redefinition?.Statement);
    }

    [Fact]
    public void A_view_created_and_altered_in_one_script_is_its_alter_and_in_two_scripts_a_doubt()
    {
        var one = Read(("v.sql", "create view shop.open_orders as select 1 as stub;\nGO\nalter view shop.open_orders as select 1 as order_id;"));
        var two = Read(("a.sql", "create view shop.open_orders as select 1 as stub;"), ("b.sql", "alter view shop.open_orders as select 1 as order_id;"));

        Assert.Null(Assert.Single(one.Model.Views).Redefinition);
        Assert.Equal(3, Assert.Single(one.Model.Views).Span.Start.Line);
        Assert.Equal(RedefiningStatement.AlterView, Assert.Single(two.Model.Views).Redefinition?.Statement);
    }

    // ---- synonyms ---------------------------------------------------------------------------------------------------------

    private const string SmallBins = "create synonym shop.bin for shop.bin_small;";

    private const string LargeBins = "create synonym shop.bin for shop.bin_large;";

    private static string Target(SchemaReadResult read) => Assert.Single(read.Model.Synonyms).For.Text;

    [Theory]
    [InlineData("drop synonym shop.bin;")]
    [InlineData("drop synonym if exists shop.bin;")]
    [InlineData("if object_id(N'shop.bin') is not null drop synonym shop.bin;")]
    [InlineData("if object_id('shop.bin', 'SN') is not null begin drop synonym shop.bin; end")]
    public void A_synonym_dropped_and_created_again_in_one_script_stands_for_its_last_target(string drop)
    {
        // Observed: A_synonym_dropped_and_created_again_stands_for_its_last_target.
        var read = Read(("bin.sql", $"{SmallBins}\nGO\n{drop}\nGO\n{LargeBins}"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal("shop.bin_large", Target(read));
        Assert.Equal(5, Assert.Single(read.Model.Synonyms).Span.Start.Line);
    }

    [Theory]
    [InlineData("if @@servername = N'x' drop synonym shop.bin;", 3)]
    [InlineData("if object_id(N'shop.bin', N'U') is not null drop synonym shop.bin;", 3)]
    [InlineData("", 5)]
    public void A_synonym_created_again_after_a_drop_that_may_not_run_is_defined_twice(string drop, int line)
    {
        // If the drop doesn't run, the second CREATE is refused (2714) and the first target stands; if it does, the second. A
        // synonym has no record of a doubt, so the read stops with the definition twice rather than pick one: reported at the drop
        // that may not run, or with none at the second CREATE.
        var read = Read(("bin.sql", $"{SmallBins}\nGO\n{drop}\nGO\n{LargeBins}"));

        var twice = Assert.Single(read.Diagnostics);
        Assert.Equal(SourceCodes.DefinedTwice, twice.Code);
        Assert.Equal(line, Assert.IsType<SourceLocation>(twice.At).Line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("drop synonym shop.bin;\nGO\n")]
    public void A_synonym_created_in_two_scripts_is_defined_twice(string drop)
    {
        // Which runs last a folder doesn't say, so which target stands isn't known, with a drop before the second or without.
        var read = Read(("a.sql", SmallBins), ("b.sql", drop + LargeBins));

        Assert.Equal(SourceCodes.DefinedTwice, Assert.Single(read.Diagnostics).Code);
    }

    [Fact]
    public void A_synonym_dropped_in_another_script_keeps_its_target()
    {
        // Whichever runs first, a reference through it reads its one target or fails because there is none: it never reads another.
        var read = Read(("a.sql", SmallBins), ("b.sql", "drop synonym shop.bin;"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal("shop.bin_small", Target(read));
    }

    [Fact]
    public void A_synonym_created_and_then_dropped_in_one_script_is_not_in_the_model()
    {
        var read = Read(("bin.sql", $"{SmallBins}\nGO\ndrop synonym shop.bin;"));

        Assert.Empty(read.Diagnostics);
        Assert.Empty(read.Model.Synonyms);
    }

    [Theory]
    [InlineData("if @@servername = N'x' begin drop synonym shop.bin; create synonym shop.bin for shop.bin_large; end")]
    [InlineData("create procedure shop.move_bins as drop synonym shop.bin; create synonym shop.bin for shop.bin_large;")]
    [InlineData("begin try create synonym shop.bin for shop.bin_large; end try begin catch select 1 as failed; end catch")]
    public void A_synonym_a_statement_that_may_run_creates_for_another_target_is_left_out(string move)
    {
        // Observed: A_synonym_created_in_a_body_stands_for_the_body_s_target. Which target stands isn't known, and a synonym has no
        // record of a doubt, so a name read through it is unresolved.
        var read = Read(("bin.sql", $"{SmallBins}\nGO\n{move}"));

        Assert.Empty(read.Diagnostics);
        Assert.Empty(read.Model.Synonyms);
    }

    [Theory]
    [InlineData("if @@servername = N'x' begin drop synonym shop.bin; create synonym shop.bin for shop.BIN_SMALL; end")]
    [InlineData("if object_id(N'shop.bin') is null create synonym shop.bin for shop.bin_small;")]
    public void A_synonym_a_statement_that_may_run_creates_for_the_same_target_keeps_it(string again)
    {
        var read = Read(("bin.sql", $"{SmallBins}\nGO\n{again}"));

        Assert.Empty(read.Diagnostics);
        Assert.Equal("shop.bin_small", Target(read));
        Assert.Equal(1, Assert.Single(read.Model.Synonyms).Span.Start.Line);
    }

    [Fact]
    public void A_synonym_only_a_statement_that_may_run_creates_stands_for_its_target()
    {
        // If it doesn't run there is no synonym, and a read through the name fails rather than reads another object.
        var read = Read(("bin.sql", "if object_id(N'shop.bin') is null create synonym shop.bin for shop.bin_small;"));
        var two = Read(("a.sql", "if object_id(N'shop.bin') is null create synonym shop.bin for shop.bin_small;"), ("b.sql", "if object_id(N'shop.bin') is null create synonym shop.bin for shop.bin_large;"));

        Assert.Equal("shop.bin_small", Target(read));
        Assert.Empty(two.Model.Synonyms);
    }
}

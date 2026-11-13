using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Model;

/// <summary>
/// The model's one order: every collection of schema objects in source order, by file in ordinal order and then by where
/// each starts, line and column, whatever order they are given in. And a name in FROM means the first of the views, synonyms
/// and functions of that name in that order.
/// </summary>
public class DatabaseModelOrderTests
{
    /// <summary>Where four objects are, out of order: across files, on lines of one file, and two on one line.</summary>
    private static readonly SourceSpan[] Given = [Span("b.sql", 1, 1), Span("a.sql", 5, 1), Span("a.sql", 2, 9), Span("a.sql", 2, 1)];

    /// <summary>The same four, in source order.</summary>
    private static readonly string[] InSourceOrder = ["a.sql:2:1", "a.sql:2:9", "a.sql:5:1", "b.sql:1:1"];

    [Fact]
    public void Every_collection_of_schema_objects_is_in_source_order_whatever_order_it_is_given_in()
    {
        var model = new DatabaseModel(
            Each((name, span) => new Table(name, span, [], [], [], [])),
            Each((name, span) => new ClrType(name, span)),
            Each((name, span) => new Procedure(name, span, [], new NoBody())),
            Each((name, span) => new ScalarFunction(name, span, [], null, new NoBody())),
            Each((name, span) => new View(name, span, [], new SelectStatement())),
            Each((name, span) => new Synonym(name, span, new SynonymTarget(null, null, "dbo", "t", "dbo.t"))),
            Each((name, span) => new Trigger(name, span, new ObjectName("dbo", "t"), false, DmlEvents.Insert, new NoBody())),
            [],
            []);

        Assert.Equal(InSourceOrder, Spans(model.Tables));
        Assert.Equal(InSourceOrder, Spans(model.UserTypes));
        Assert.Equal(InSourceOrder, Spans(model.Procedures));
        Assert.Equal(InSourceOrder, Spans(model.Functions));
        Assert.Equal(InSourceOrder, Spans(model.Views));
        Assert.Equal(InSourceOrder, Spans(model.Synonyms));
        Assert.Equal(InSourceOrder, Spans(model.Triggers));
    }

    [Theory]
    [InlineData("a.sql", "b.sql", "view")]
    [InlineData("b.sql", "a.sql", "function")]
    public void Of_a_view_and_a_function_of_one_name_a_name_in_from_means_the_first_in_source_order(string view, string function, string first)
    {
        // SQL Server refuses a second object of one name in a schema, so only a script that can't deploy has both.
        var name = new ObjectName("app", "thing");
        var model = new DatabaseModel(
            [], [], [],
            [new ScalarFunction(name, Span(function, 1, 1), [], null, new NoBody())],
            [new View(name, Span(view, 1, 1), [], new SelectStatement())],
            [], [], [], []);

        Assert.Equal(first, model.FindReadable(name) switch { View => "view", Function => "function", _ => "neither" });
        Assert.Single(model.Readable);
    }

    [Fact]
    public void The_objects_a_name_in_from_may_mean_are_in_source_order()
    {
        var model = new DatabaseModel(
            [], [], [],
            [new ScalarFunction(new ObjectName("dbo", "f"), Span("c.sql", 1, 1), [], null, new NoBody())],
            [new View(new ObjectName("dbo", "v"), Span("b.sql", 1, 1), [], new SelectStatement())],
            [new Synonym(new ObjectName("dbo", "s"), Span("a.sql", 1, 1), new SynonymTarget(null, null, "dbo", "t", "dbo.t"))],
            [], [], []);

        Assert.Equal(["dbo.s", "dbo.v", "dbo.f"], model.Readable.Select(o => o.Name.ToString()));
    }

    /// <summary>Four objects at <see cref="Given"/>'s places, in its order, each with a name of its own.</summary>
    private static List<T> Each<T>(Func<ObjectName, SourceSpan, T> make) =>
        [.. Given.Select((span, i) => make(new ObjectName("dbo", $"o{i}"), span))];

    private static List<string> Spans(IEnumerable<SchemaObject> objects) => [.. objects.Select(o => o.Span.ToString())];

    private static SourceSpan Span(string file, int line, int column) => new(file, line, column, line, column + 1);
}

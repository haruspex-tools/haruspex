using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A body's labels, each with the statement list it stands in, and each <c>GOTO</c> with the statement lists around it: what
/// <see cref="Walker"/> needs to know whether it follows a jump, read once before the walk.
/// </summary>
internal sealed class BodyLabels : TSqlFragmentVisitor
{
    /// <summary>A body with no label and no GOTO.</summary>
    public static BodyLabels None { get; } = new();

    private readonly Stack<StatementList> around = new();

    private readonly List<(LabelStatement Label, StatementList? In)> labels = [];

    private readonly Dictionary<GoToStatement, StatementList[]> jumps = new(ReferenceEqualityComparer.Instance);

    private BodyLabels()
    {
    }

    /// <summary>The labels and GOTOs of a body.</summary>
    public static BodyLabels Of(StatementList body)
    {
        var read = new BodyLabels();
        body.Accept(read);
        return read;
    }

    /// <summary>A label's name, without the colon that ends it.</summary>
    public static string Name(LabelStatement label) => label.Value.TrimEnd(':');

    public override void ExplicitVisit(StatementList node)
    {
        around.Push(node);
        base.ExplicitVisit(node);
        around.Pop();
    }

    public override void Visit(LabelStatement node) =>
        labels.Add((node, around.TryPeek(out var list) && list.Statements.Contains(node) ? list : null));

    public override void Visit(GoToStatement node) => jumps[node] = [.. around];

    /// <summary>
    /// Why the walk can't follow <paramref name="jump"/>, or null when it can: to the one label of its name, later in the
    /// body, in a statement list the GOTO is in or one around it.
    /// </summary>
    public string? Unfollowed(GoToStatement jump)
    {
        var name = jump.LabelName.Value;
        var named = labels.Where(l => string.Equals(Name(l.Label), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count != 1)
            return $"GOTO {name} names {named.Count} labels of the body, not one";
        var (label, list) = named[0];
        if (label.StartOffset < jump.StartOffset)
            return $"GOTO {name} jumps back, making a loop, which is not followed";
        if (list is null || !jumps[jump].Contains(list))
            return $"GOTO {name} jumps into a block it is not in, which is not followed";
        return null;
    }
}

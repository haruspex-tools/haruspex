using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>A result column before it is shaped into a rowset or JSON.</summary>
/// <param name="Origin">
/// The table column it is, unchanged, when it is one (a bare column reference without COLLATE, or a column of
/// <c>SELECT *</c>): what a view's keys are found from.
/// </param>
/// <param name="Expression">
/// The select-list expression it was read from, when there is one: a set operation types an integer literal by its digits
/// and makes a closed set of literals.
/// </param>
internal sealed record RowColumn(string? Name, Value Value, SourceLocation At, TableColumn? Origin = null, ScalarExpression? Expression = null)
{
    /// <summary>The table columns its value is read from (<c>Typer.Reads.cs</c>), which the IR writes as the field's <c>from</c>.</summary>
    public EquatableArray<IrSourceColumn> From { get; init; } = [];

    /// <summary>
    /// The database's description of the one column it is, unchanged (<c>Typer.Descriptions.cs</c>); null when it has none or is
    /// anything else, which the IR writes as the field's <c>description</c>.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether its value is JSON the server never folds to a constant (<c>Typer.Folding.cs</c>), which makes a UNION over it
    /// text in every row.
    /// </summary>
    public bool JsonNeverFolded { get; init; }

    /// <summary>
    /// What it reads when it is a bare column reference (in parentheses or not, without <c>COLLATE</c>) or a column of a star, as
    /// binding resolves it; null for any other expression. <c>FOR JSON AUTO</c> nests a bare column under the source it reads,
    /// and any other column where it stands (<c>Typer.ForJsonAuto.cs</c>).
    /// </summary>
    public Resolution? Bare { get; init; }
}

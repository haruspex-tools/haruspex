using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// An object the model knows only by name and kind: a view, synonym or function, whose definition is not
/// modelled yet. Knowing it exists still matters, because a reference to a name can resolve to it.
/// </summary>
/// <param name="Name">The object's name.</param>
/// <param name="Kind">What it is.</param>
public sealed record NamedObject(ObjectName Name, NamedObjectKind Kind)
{
    /// <summary>
    /// A view's definition, as ScriptDom parsed it; null for other objects, and for a view defined more than once, whose
    /// definition depends on the order the scripts run. What it reads matters to DML through it, which changes the tables
    /// underneath and fires their triggers.
    /// </summary>
    public ViewStatementBody? View { get; init; }

    /// <summary>Where a view's <see cref="View"/> is; null for other objects, and for a view defined more than once.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>
    /// Whether a view was created with ANSI_NULLS off, which it keeps (as a procedure does): set by <c>SET ANSI_NULLS OFF</c>
    /// before its definition in the same file.
    /// </summary>
    public bool AnsiNullsOff { get; init; }

    /// <summary>
    /// A synonym's object, as written (possibly in another database or on a linked server); null for other objects. A write
    /// through a synonym writes its object and fires that object's triggers.
    /// </summary>
    public SchemaObjectName? SynonymFor { get; init; }
}

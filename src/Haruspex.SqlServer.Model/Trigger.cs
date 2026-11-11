using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A DML trigger: on a table or a view, for some of <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c>. DDL and logon triggers
/// are not recorded: no statement a procedure's DML runs fires them.
/// </summary>
/// <param name="name">Its name.</param>
/// <param name="parent">The table or view it is on.</param>
/// <param name="insteadOf">Whether it is an <c>INSTEAD OF</c> trigger; otherwise <c>AFTER</c> (or <c>FOR</c>).</param>
/// <param name="events">The statements it fires for.</param>
/// <param name="body">Its statements, or null for a CLR trigger, whose code is not in the project.</param>
/// <param name="at">Where it is defined.</param>
public sealed class Trigger(ObjectName name, ObjectName parent, bool insteadOf, DmlEvents events, StatementList? body, SourceLocation at)
{
    /// <summary>The trigger's name, in its table's schema.</summary>
    public ObjectName Name { get; } = name;

    /// <summary>The table or view it is on.</summary>
    public ObjectName Parent { get; } = parent;

    /// <summary>Whether it runs instead of the statement (<c>INSTEAD OF</c>), rather than after it.</summary>
    public bool InsteadOf { get; } = insteadOf;

    /// <summary>The statements it fires for.</summary>
    public DmlEvents Events { get; } = events;

    /// <summary>Its statements, as ScriptDom parsed them; null for a CLR trigger.</summary>
    public StatementList? Body { get; } = body;

    /// <summary>Where it is defined; the file its body's positions are in.</summary>
    public SourceLocation At { get; } = at ?? throw new ArgumentNullException(nameof(at));

    /// <summary>The trigger's name.</summary>
    public override string ToString() => Name.ToString();
}

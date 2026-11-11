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

/// <summary>The data-modifying statements a trigger can fire for.</summary>
[Flags]
public enum DmlEvents
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary><c>INSERT</c>.</summary>
    Insert = 1,

    /// <summary><c>UPDATE</c>.</summary>
    Update = 2,

    /// <summary><c>DELETE</c>.</summary>
    Delete = 4,
}

/// <summary>
/// A foreign key that changes rows of its table when rows of the table it references are deleted or updated: one with an
/// <c>ON DELETE</c> or <c>ON UPDATE</c> action other than <c>NO ACTION</c>. Such a change fires the referencing table's
/// triggers. Keys without an action change nothing, so they are not recorded.
/// </summary>
/// <param name="Referencing">The table whose rows the action changes.</param>
/// <param name="Referenced">The table whose rows being deleted or updated starts it.</param>
/// <param name="At">Where it is declared.</param>
public sealed record CascadingKey(ObjectName Referencing, ObjectName Referenced, SourceLocation At);

using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A DML trigger: on a table or a view, for some of <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c>. DDL and logon triggers
/// are not recorded: no statement a procedure's DML runs fires them.
/// </summary>
/// <param name="Name">Its name, in its table's schema.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Parent">The table or view it is on.</param>
/// <param name="InsteadOf">Whether it runs instead of the statement (<c>INSTEAD OF</c>), rather than after it (<c>AFTER</c> or <c>FOR</c>).</param>
/// <param name="Events">The statements it fires for.</param>
/// <param name="Body">What it runs.</param>
public sealed record Trigger(ObjectName Name, SourceSpan Span, ObjectName Parent, bool InsteadOf, DmlEvents Events, ModuleBody Body) : Module(Name, Span);

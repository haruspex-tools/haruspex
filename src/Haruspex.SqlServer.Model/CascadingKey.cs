using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A foreign key that changes rows of its table when rows of the table it references are deleted or updated: one with an
/// <c>ON DELETE</c> or <c>ON UPDATE</c> action other than <c>NO ACTION</c>. Such a change fires the referencing table's
/// triggers. Keys without an action change nothing, so they are not recorded.
/// </summary>
/// <param name="Referencing">The table whose rows the action changes.</param>
/// <param name="Referenced">The table whose rows being deleted or updated starts it.</param>
/// <param name="At">Where it is declared.</param>
public sealed record CascadingKey(ObjectName Referencing, ObjectName Referenced, SourceLocation At);

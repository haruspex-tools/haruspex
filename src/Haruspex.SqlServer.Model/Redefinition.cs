using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A statement that changes an object after its definition, in a way the model does not apply: <c>ALTER TABLE</c>
/// that changes columns, <c>DROP TABLE</c>, <c>ALTER PROCEDURE</c>, <c>DROP PROCEDURE</c>, a second definition of a view.
/// </summary>
/// <remarks>
/// A folder of scripts does not say the order they run in, so which definition is in effect is unknown. The model
/// records the doubt rather than guessing an order; readers should not trust the object's definition. Saying so in words
/// is a reader's job (<c>Redefinitions.Why</c> in Haruspex.SqlServer.Semantics).
/// </remarks>
/// <param name="Name">The object changed.</param>
/// <param name="Statement">The kind of statement.</param>
/// <param name="At">Where the statement is.</param>
public sealed record Redefinition(ObjectName Name, RedefiningStatement Statement, SourceLocation At);

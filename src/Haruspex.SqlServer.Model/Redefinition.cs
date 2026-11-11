using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A statement that changes an object after its definition, in a way the model does not apply: <c>ALTER TABLE</c>
/// that changes columns, <c>DROP TABLE</c>, <c>ALTER PROCEDURE</c>, <c>DROP PROCEDURE</c>.
/// </summary>
/// <remarks>
/// A folder of scripts does not say the order they run in, so which definition is in effect is unknown. The model
/// records the doubt rather than guessing an order; readers should not trust the object's definition.
/// </remarks>
/// <param name="Name">The object changed.</param>
/// <param name="Statement">The statement, in capitals: <c>ALTER TABLE</c>, <c>DROP PROCEDURE</c>.</param>
/// <param name="At">Where the statement is.</param>
public sealed record Redefinition(ObjectName Name, string Statement, SourceLocation At)
{
    /// <summary>Why the object's definition cannot be trusted, in a sentence.</summary>
    public string Message =>
        $"{Name} is changed by {Statement} at {At}, and which definition applies depends on the order the scripts run.";
}

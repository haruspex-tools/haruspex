using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A fact proved by a CHECK constraint.</summary>
/// <param name="Table">The table whose constraint it is: one of the schema, or one a body declares.</param>
/// <param name="Check">The constraint.</param>
public sealed record CheckSource(ITable Table, CheckConstraint Check) : FactSource;

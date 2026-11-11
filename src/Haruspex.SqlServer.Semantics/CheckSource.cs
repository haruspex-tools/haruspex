using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A fact proved by a CHECK constraint.</summary>
public sealed record CheckSource(Table Table, CheckConstraint Check) : FactSource;

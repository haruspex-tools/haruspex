using Haruspex.Collections;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>One of the mssql driver's types, which a parameter is sent as: <c>sql.NVarChar(40)</c>, <c>sql.Int</c>.</summary>
/// <param name="Name">Its name on the driver's <c>sql</c> export.</param>
/// <param name="Arguments">What it is called with, written as the printer writes them (a count, or <c>MAX</c> for <c>sql.MAX</c>); none for a type that takes none.</param>
internal sealed record DriverType(string Name, EquatableArray<string> Arguments);

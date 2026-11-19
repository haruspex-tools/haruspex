using Haruspex.Collections;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>
/// How a table-valued parameter's rows are sent: as the driver's <c>sql.Table</c> of the table type, named by its schema and
/// name apart (so no name needs quoting), with every column declared, in the type's order, since the server matches the
/// columns by place and refuses a table that lacks one (observed: <c>ParameterObservationTests</c>).
/// </summary>
/// <param name="Schema">The table type's schema.</param>
/// <param name="Name">The table type's name within its schema.</param>
/// <param name="Columns">Each column's name and the driver's type it is declared as, in the type's order.</param>
internal sealed record TableValue(string Schema, string Name, EquatableArray<(string Name, DriverType Type)> Columns);

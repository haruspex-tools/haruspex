using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A table's column declared to hold JSON of a schema, in the IR's terms: part of the column's type, which inference reads at every
/// read of the column, as it reads its SQL type. Inference takes it as given, as it takes the model; where it comes from (the
/// config file, in Haruspex.Config) is not its concern
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
/// <param name="Table">The table.</param>
/// <param name="Column">The column's name.</param>
/// <param name="Content">What a non-null value of the column parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it, which names the column as the claims do.</param>
public sealed record DeclaredColumn(ObjectName Table, string Column, IrType Content, EquatableMap<string, IrObjectType> Types, IrColumnDeclaration Record);

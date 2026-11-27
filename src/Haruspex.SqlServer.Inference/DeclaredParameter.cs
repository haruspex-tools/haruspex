using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A procedure's or function's parameter declared to hold JSON of a schema, in the IR's terms: a claim about every caller, which the
/// body takes as what the parameter holds until it is assigned, as it takes a declared column's JSON. Inference takes it as given,
/// as it takes the model; where it comes from (the config file or the SQL, in Haruspex.Config) is not its concern
/// (docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
/// <param name="Owner">The procedure or function.</param>
/// <param name="Parameter">The parameter's name, with its <c>@</c>.</param>
/// <param name="Content">What a non-null value of the parameter parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it, which names the parameter as the claims do.</param>
public sealed record DeclaredParameter(ObjectName Owner, string Parameter, IrType Content, EquatableMap<string, IrObjectType> Types, IrParameterDeclaration Record);

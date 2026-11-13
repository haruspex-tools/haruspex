using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The result sets declared for a procedure whose results cannot be inferred from the project, in the IR's terms: what
/// a call of it contributes to its caller. Inference takes them as given, as it takes the model; where they come from
/// (the config file, in Haruspex.Config) is not its concern.
/// </summary>
/// <param name="Results">The result sets it returns, in order; none for a procedure declared to return none.</param>
/// <param name="Types">The object types <paramref name="Results"/> refer to.</param>
/// <param name="Why">Why the declaration holds, as given.</param>
public sealed record DeclaredResults(EquatableArray<IrResult> Results, EquatableMap<string, IrObjectType> Types, string Why);

using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Planning;

/// <summary>One schema the module declares, in the plan's order: a named object type, or a procedure's results.</summary>
internal abstract record ZodPlanEntry
{
    private ZodPlanEntry()
    {
    }

    /// <summary>A named object type, declared before the first procedure whose results reach it.</summary>
    /// <param name="Name">Its name, from the IR's type table.</param>
    /// <param name="Type">The type.</param>
    /// <param name="Encoding">How its values arrive, which decides how its scalars are read (<see cref="IrTypeGraph.EncodingsOf"/>).</param>
    public sealed record NamedType(string Name, IrObjectType Type, IrEncoding Encoding) : ZodPlanEntry;

    /// <summary>
    /// A table type's row (<see cref="IrTypeOwner.TableType"/>), what a client sends in a table-valued parameter, declared before
    /// the first procedure whose parameters take it. It is a value sent, not read, so it has no encoding.
    /// </summary>
    /// <param name="Name">Its name, from the IR's type table.</param>
    /// <param name="Type">The type.</param>
    public sealed record TableRow(string Name, IrObjectType Type) : ZodPlanEntry;

    /// <summary>A procedure: a schema for its result sets.</summary>
    /// <param name="Object">The procedure.</param>
    public sealed record Procedure(IrObject Object) : ZodPlanEntry;
}

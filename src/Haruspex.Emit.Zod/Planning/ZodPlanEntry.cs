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

    /// <summary>A procedure: a schema for its result sets.</summary>
    /// <param name="Object">The procedure.</param>
    public sealed record Procedure(IrObject Object) : ZodPlanEntry;
}

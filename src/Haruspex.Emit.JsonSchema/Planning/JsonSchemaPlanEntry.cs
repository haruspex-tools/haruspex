using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Planning;

/// <summary>One definition the document holds in <c>$defs</c>, in the plan's order: a named object type, or a procedure's results.</summary>
internal abstract record JsonSchemaPlanEntry
{
    private JsonSchemaPlanEntry()
    {
    }

    /// <summary>The definition's name in <c>$defs</c>, the IR's name for it.</summary>
    public abstract string Name { get; }

    /// <summary>A named object type.</summary>
    /// <param name="TypeName">Its name, from the IR's type table.</param>
    /// <param name="Type">The type.</param>
    public sealed record NamedType(string TypeName, IrObjectType Type) : JsonSchemaPlanEntry
    {
        /// <inheritdoc/>
        public override string Name => TypeName;
    }

    /// <summary>A procedure: a schema for its result sets.</summary>
    /// <param name="Object">The procedure.</param>
    public sealed record Procedure(IrObject Object) : JsonSchemaPlanEntry
    {
        /// <inheritdoc/>
        public override string Name => Object.Name;
    }
}

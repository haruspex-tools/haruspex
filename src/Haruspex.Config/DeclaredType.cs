using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// An object type a declaration of results defines (<c>results</c>, <see cref="ResultsDeclaration"/>): its identity, which
/// every declared type has, and its fields. The IR's object type may lack an identity (a document built by hand); this one
/// can't, so applying a declaration never has to assume it.
/// </summary>
/// <param name="Identity">The procedure and path that own it, as the config file's spelling of the procedure gives them.</param>
/// <param name="Fields">Its fields, in order.</param>
public sealed record DeclaredType(IrTypeIdentity Identity, EquatableArray<IrField> Fields)
{
    /// <summary>The type in the IR's terms.</summary>
    public IrObjectType ToIr() => new(Fields) { Identity = Identity };
}

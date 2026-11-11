namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A shape as inference works with it: the IR's type before naming. Objects are anonymous here, and unknowns carry their
/// own location; <see cref="IrBuilder"/> turns shapes into the IR.
/// </summary>
internal abstract record Shape;

namespace Haruspex.SqlServer.Inference;

// The shapes inference works with. They are the IR's types before naming: objects are anonymous here, and
// unknowns carry their own location. IrBuilder turns them into the IR.

internal abstract record Shape;

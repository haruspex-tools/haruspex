using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

internal sealed record ScalarShape(IrScalar Type) : Shape;

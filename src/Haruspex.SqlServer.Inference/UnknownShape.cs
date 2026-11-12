using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

internal sealed record UnknownShape(IrUnknownReason Reason, string Detail, SourceLocation At) : Shape;

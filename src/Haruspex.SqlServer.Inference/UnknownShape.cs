using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Inference;

internal sealed record UnknownShape(string Reason, string Detail, SourceLocation At) : Shape;

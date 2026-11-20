using Haruspex.Diagnostics;
namespace Haruspex.SqlServer.Inference;

/// <summary>Text that may be anything: a parameter, a column, a value no rule here reads as pieces.</summary>
/// <param name="What">The expression, as the SQL writes it, for the unknown it gives.</param>
/// <param name="At">Where it is written.</param>
internal sealed record OpaquePiece(string What, SourceLocation At) : TextPiece;

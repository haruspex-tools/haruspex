using Haruspex.Diagnostics;
namespace Haruspex.SqlServer.Inference;

/// <summary>Text that may be anything: a parameter, a column, a value no rule here reads as pieces.</summary>
/// <param name="What">The expression, as the SQL writes it, for the unknown it gives.</param>
/// <param name="At">Where it is written.</param>
/// <param name="CutShort">
/// Whether it is text that may be cut short with more text after it (<see cref="CutPiece"/>), so what follows comes after a cut
/// that may fall anywhere: its own reason, whatever the place.
/// </param>
internal sealed record OpaquePiece(string What, SourceLocation At, bool CutShort = false) : TextPiece;

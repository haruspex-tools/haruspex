using Haruspex.Diagnostics;
namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Text that never holds a quotation mark, a backslash or a control character, so that inside quotes it is one JSON string, read
/// back as <paramref name="String"/>: <c>string_escape(x, 'json')</c> (read back as x), a date, a <c>uniqueidentifier</c> or a
/// number in a form that is not a JSON number, converted to text, and a value whose value set holds only such text.
/// </summary>
/// <param name="String">The string a JSON reader gets back from it inside quotes.</param>
/// <param name="Unquoted">Why it is not one JSON value outside quotes, for the unknown that gives.</param>
/// <param name="At">Where it is written.</param>
internal sealed record SafeTextPiece(Shape String, string Unquoted, SourceLocation At) : TextPiece;

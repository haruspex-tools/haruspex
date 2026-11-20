namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A number converted to text in a form that is always a JSON number of exactly its value: an integer or a decimal with no
/// style, <c>money</c> with style 2 or 126, a <c>bit</c> (observed: A_number_converted_to_text_is_written_so).
/// </summary>
/// <param name="Number">What the text is as a JSON number.</param>
/// <param name="Text">What it is as a string, for the text inside quotes: the converted value's shape.</param>
internal sealed record NumberPiece(Shape Number, Shape Text) : TextPiece;

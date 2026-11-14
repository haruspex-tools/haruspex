using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// How a collation compares the strings of a column (0.20), as the value sets whose match is <see cref="IrValueMatch.Canonical"/>
/// need it. The canonical form of a string drops each character in <paramref name="Ignored"/>, puts a class's name in place of
/// each character of that class, keeps every other character, and then drops trailing spaces (U+0020), in that order. Two
/// strings the column can hold are equal under its collation exactly when their canonical forms are.
/// </summary>
/// <param name="Ignored">The characters the collation ignores, each a UTF-16 code unit, in ordinal order.</param>
/// <param name="Classes">
/// The characters it holds equal to each class, in ordinal order, by the class's name, which is what they become: <c>"AE"</c> for
/// <c>"Ææ"</c> under a case-insensitive collation.
/// </param>
public sealed record IrFold(string Ignored, EquatableMap<string, string> Classes);

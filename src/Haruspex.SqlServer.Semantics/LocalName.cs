namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A name a body declares or creates for itself, with its sigil: a parameter, variable or table variable (<c>@total</c>), or a
/// local temporary table (<c>#lines</c>). It compares ignoring case, so that <c>@Total</c> and <c>@TOTAL</c> are one variable
/// wherever the walk keys what it holds by name (the state, a call's temporary tables).
/// </summary>
/// <remarks>
/// Ignoring case is an assumption: SQL Server matches a variable's or temporary table's name by the server's collation, and a
/// case-sensitive one tells <c>@Total</c> from <c>@total</c>. The two ways agree for every body that deploys unless it declares
/// two names that differ only in case, which reading refuses (HAR1022, decision 0119), as it does for object names
/// (<c>ObjectName</c>). Brackets are the parser's: a name written <c>[#lines]</c> arrives as <c>#lines</c>.
/// </remarks>
/// <param name="Text">The name as declared or written, with its sigil.</param>
public readonly record struct LocalName(string Text)
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether it names a local temporary table (<c>#t</c>).</summary>
    public bool IsTemporary => Text.StartsWith('#');

    /// <summary>Whether it names a parameter, a variable or a table variable (<c>@v</c>).</summary>
    public bool IsVariable => Text.StartsWith('@');

    /// <summary>Whether both are one name, ignoring case.</summary>
    public bool Equals(LocalName other) => Comparer.Equals(Text, other.Text);

    /// <summary>A hash consistent with the case-insensitive <see cref="Equals(LocalName)"/>.</summary>
    public override int GetHashCode() => Comparer.GetHashCode(Text ?? "");

    /// <summary>The name as declared or written.</summary>
    public override string ToString() => Text;
}

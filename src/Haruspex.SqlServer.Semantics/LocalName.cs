namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A name a body declares or creates for itself, with its sigil: a parameter, variable or table variable (<c>@total</c>), or a
/// local temporary table (<c>#lines</c>). It compares as SQL Server compares them, ignoring case, so that <c>@Total</c> and
/// <c>@total</c> are one variable wherever the walk keys what it holds by name (the state, a call's temporary tables).
/// </summary>
/// <remarks>
/// SQL Server matches a variable's or temporary table's name by the server's collation, which is case-insensitive almost
/// everywhere; haruspex assumes it is, as it does for object names (<c>ObjectName</c>). Brackets are the parser's: a name
/// written <c>[#lines]</c> arrives as <c>#lines</c>.
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

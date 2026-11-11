namespace Haruspex.Ir;

/// <summary>The kinds of <see cref="IrFactSource"/>.</summary>
public static class IrFactSources
{
    /// <summary>The value's type, such as <c>nvarchar(40)</c> or an alias type's name.</summary>
    public const string Type = "type";

    /// <summary>A check constraint.</summary>
    public const string Check = "check";

    /// <summary>
    /// The engine itself: a value it writes from a closed set, such as <c>$action</c> in a <c>MERGE</c>'s <c>OUTPUT</c>. The
    /// ref names the value.
    /// </summary>
    public const string Engine = "engine";

    /// <summary>
    /// An expression of the procedure whose every result is a literal: a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> gives one of
    /// them, exactly as written, and so does a column of a set operation each of whose queries gives a literal (0.13). The ref
    /// is the expression's (or the literal's) location.
    /// </summary>
    public const string Expression = "expression";
}

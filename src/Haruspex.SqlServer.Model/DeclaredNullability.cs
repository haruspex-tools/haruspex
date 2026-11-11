namespace Haruspex.SqlServer.Model;

/// <summary>What a column's declaration says about null, before any key or identity is taken into account.</summary>
public enum DeclaredNullability
{
    /// <summary>
    /// Neither <c>NULL</c> nor <c>NOT NULL</c>. Then the session's ANSI_NULL_DFLT settings decide, which the
    /// source does not record.
    /// </summary>
    NotDeclared,

    /// <summary><c>NULL</c>.</summary>
    Null,

    /// <summary><c>NOT NULL</c>.</summary>
    NotNull,
}

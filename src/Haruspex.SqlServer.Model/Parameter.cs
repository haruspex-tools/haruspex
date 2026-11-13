using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A parameter of a procedure or function as declared.</summary>
/// <param name="Name">Its name, with its <c>@</c>.</param>
/// <param name="Type">Its declared type; null when the declaration names a type ScriptDom gives no name (an untyped <c>xml</c>).</param>
/// <param name="IsOutput">Whether it is declared <c>OUTPUT</c>.</param>
/// <param name="Default">Its default, as ScriptDom parsed it; null when it has none, so an argument must be passed.</param>
/// <param name="At">Where it is declared.</param>
public sealed record Parameter(string Name, SqlType? Type, bool IsOutput, ScalarExpression? Default, SourceLocation At)
{
    /// <summary>The parameter's name.</summary>
    public override string ToString() => Name;
}

using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Whether a procedure's call may end with a return code other than 0, which is what makes the code worth handing to a caller
/// (<c>docs/decisions/0060-a-call-returns-its-one-result-itself.md</c>). Observed through the mssql driver: no <c>RETURN</c>, a bare
/// one, <c>RETURN 0</c> and <c>RETURN NULL</c> each give 0, and <c>RETURN 7</c> gives 7. So a body may return a code exactly when it
/// has a <c>RETURN</c> with some other value, at any depth, reached or not; that only claims less when the path is never taken.
/// </summary>
internal static class ReturnCodes
{
    /// <summary>
    /// Whether a call of <paramref name="procedure"/> may end with a code other than 0. A body that can't be read (CLR, encrypted,
    /// or changed after its definition) may, since nothing says otherwise.
    /// </summary>
    public static bool May(Procedure procedure) =>
        procedure is not { Body: SqlBody body, Redefinition: null } || May(body.Statements);

    /// <summary>Whether <paramref name="statements"/> hold a <c>RETURN</c> whose value isn't the constant 0 or NULL.</summary>
    public static bool May(StatementList statements)
    {
        var finder = new Finder();
        statements.Accept(finder);
        return finder.Found;
    }

    private sealed class Finder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(ReturnStatement node)
        {
            if (node.Expression is { } value && !IsZeroOrNull(value))
                Found = true;
        }

        private static bool IsZeroOrNull(ScalarExpression value) => value switch
        {
            ParenthesisExpression parenthesis => IsZeroOrNull(parenthesis.Expression),
            IntegerLiteral literal => literal.Value.TrimStart('0').Length == 0,
            NullLiteral => true,
            _ => false,
        };
    }
}

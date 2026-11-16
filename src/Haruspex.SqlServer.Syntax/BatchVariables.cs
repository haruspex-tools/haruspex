using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>
/// The variables a batch uses without declaring: in a batch run through <c>sp_executesql</c>, its parameters. SQL Server compiles a
/// batch's declarations rather than running them, so a variable declared at any depth, in a branch never taken too, is declared
/// for the rest of the batch; but only for the rest of it, in the order of the text: a use before the <c>DECLARE</c> is refused
/// (error 137), and with the name passed as a parameter too, the <c>DECLARE</c> is (error 134) (observed). What is never declared
/// is what the call must declare. Names compare ignoring case, as the server compares them.
/// </summary>
public static class BatchVariables
{
    /// <summary>
    /// Each variable the batch uses but doesn't declare (with <c>DECLARE</c>, as a scalar or a table variable), at its first use,
    /// in the order the text first uses them. A parameter's name in an <c>EXEC</c> (<c>@why</c> in <c>EXEC p @why = @w</c>) is
    /// the callee's, not a use, so it isn't one.
    /// </summary>
    /// <param name="statements">The batch.</param>
    public static IReadOnlyList<BatchParameter> Undeclared(StatementList statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        var found = new Collector();
        statements.Accept(found);
        return [.. found.Used
            .Where(v => !found.Declared.Contains(v.Name))
            .OrderBy(v => v.StartOffset)
            .DistinctBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new BatchParameter(v.Name, v))];
    }

    /// <summary>
    /// Each use of a variable that comes, in the text, before the batch's first <c>DECLARE</c> of it, which SQL Server refuses;
    /// in the order of the text.
    /// </summary>
    /// <param name="statements">The batch.</param>
    public static IReadOnlyList<VariableReference> UsedBeforeDeclared(StatementList statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        var found = new Collector();
        statements.Accept(found);
        return [.. found.Used
            .Where(v => found.FirstDeclared.TryGetValue(v.Name, out var declared) && v.StartOffset < declared)
            .OrderBy(v => v.StartOffset)];
    }

    /// <summary>The names the batch declares with <c>DECLARE</c>, scalar and table variables alike, ignoring case.</summary>
    /// <param name="statements">The batch.</param>
    public static IReadOnlySet<string> Declared(StatementList statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        var found = new Collector();
        statements.Accept(found);
        return found.Declared;
    }

    private sealed class Collector : TSqlFragmentVisitor
    {
        public HashSet<string> Declared { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<VariableReference> Used { get; } = [];

        /// <summary>Where, in the text, each name is first declared.</summary>
        public Dictionary<string, int> FirstDeclared { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(DeclareVariableElement node) => Declare(node.VariableName.Value, node.StartOffset);

        public override void Visit(DeclareTableVariableBody node) => Declare(node.VariableName.Value, node.StartOffset);

        private void Declare(string name, int at)
        {
            Declared.Add(name);
            if (!FirstDeclared.TryGetValue(name, out var first) || at < first)
                FirstDeclared[name] = at;
        }

        public override void Visit(VariableReference node) => Used.Add(node);

        /// <summary>Only the value passed: the name before it is the callee's parameter.</summary>
        public override void ExplicitVisit(ExecuteParameter node) => node.ParameterValue?.Accept(this);
    }
}

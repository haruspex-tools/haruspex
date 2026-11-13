using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a body declares for the whole of it: its parameters, its variables, its table variables (a multi-statement
/// function's return table among them) and its cursors. SQL Server compiles declarations rather than running them (observed:
/// A_variable_or_table_variable_declared_in_a_branch_not_taken_still_exists), so they are read from the whole body at once,
/// at any depth, and hold wherever the body reads them. A name can be declared once in a batch; the first declaration of each
/// is kept.
/// </summary>
public sealed class BodyDeclarations
{
    private readonly Dictionary<string, VariableResolution> variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DeclareCursorStatement>> cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DeclareVariableElement, VariableResolution> elements = new(ReferenceEqualityComparer.Instance);

    private BodyDeclarations(IReadOnlyList<Parameter> parameters, IReadOnlyList<LocalVariable> locals, IReadOnlyList<DeclaredTable> tableVariables)
    {
        Parameters = parameters;
        Variables = locals;
        TableVariables = tableVariables;
        foreach (var parameter in parameters)
            variables.TryAdd(parameter.Name, new ParameterVariable(parameter));
        foreach (var local in locals)
            variables.TryAdd(local.Name, local);
    }

    /// <summary>A body that declares nothing and has no parameters: a view's.</summary>
    public static BodyDeclarations None { get; } = new([], [], []);

    /// <summary>Its parameters, in order.</summary>
    public IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>The variables it declares, each once and in the order declared; none of a parameter's name.</summary>
    public IReadOnlyList<LocalVariable> Variables { get; }

    /// <summary>Its table variables as declared, each once: a multi-statement function's return table first.</summary>
    public IReadOnlyList<DeclaredTable> TableVariables { get; }

    /// <summary>The declarations of every body of only parameters: a function's <c>RETURN</c> expression, an inline function's query.</summary>
    /// <param name="parameters">The parameters.</param>
    public static BodyDeclarations Of(IReadOnlyList<Parameter> parameters) => new(parameters ?? throw new ArgumentNullException(nameof(parameters)), [], []);

    /// <summary>Reads what a body declares.</summary>
    /// <param name="parameters">The procedure's or function's parameters.</param>
    /// <param name="statements">Its body.</param>
    /// <param name="returnTable">A multi-statement function's return table, which its body fills as a table variable; null for a procedure.</param>
    /// <param name="source">Where the object whose body it is was read.</param>
    public static BodyDeclarations Read(IReadOnlyList<Parameter> parameters, StatementList statements, DeclaredTable? returnTable, SourceSpan source)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(source);
        var found = new Collector();
        statements.Accept(found);

        var locals = new List<LocalVariable>();
        foreach (var element in found.Variables)
        {
            var name = element.VariableName.Value;
            if (!parameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && !locals.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                locals.Add(new LocalVariable(name, SyntaxReader.Type(element.DataType), source.At(element)));
        }

        var tables = new List<DeclaredTable>();
        var declared = found.Tables.Select(t => SyntaxReader.Declared(t.VariableName.Value, t.Definition, source.Within(t)));
        foreach (var table in returnTable is null ? declared : declared.Prepend(returnTable))
        {
            if (!tables.Any(t => t.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase)))
                tables.Add(table);
        }

        var result = new BodyDeclarations(parameters, locals, tables);
        foreach (var element in found.Variables)
            result.elements[element] = result.Variable(element.VariableName.Value);
        foreach (var cursor in found.Cursors)
        {
            if (!result.cursors.TryGetValue(cursor.Name.Value, out var same))
                result.cursors[cursor.Name.Value] = same = [];
            same.Add(cursor);
        }

        return result;
    }

    /// <summary>What a variable of this name is here.</summary>
    /// <param name="name">The name, with its <c>@</c>.</param>
    public VariableResolution Variable(string name) =>
        variables.TryGetValue(name ?? throw new ArgumentNullException(nameof(name)), out var found) ? found : new UndeclaredVariable(name);

    /// <summary>
    /// What a <c>DECLARE</c> element declares: the variable it names, which may be one declared before it of the same name (a
    /// batch declares a name once) or a parameter; undeclared for an element of another body.
    /// </summary>
    /// <param name="element">The element, <c>@v int = 1</c>.</param>
    public VariableResolution Declared(DeclareVariableElement element) =>
        elements.TryGetValue(element ?? throw new ArgumentNullException(nameof(element)), out var found) ? found : new UndeclaredVariable(element.VariableName.Value);

    /// <summary>Every declaration of the cursor a <c>FETCH</c>, <c>OPEN</c> or the like names, in the order written; empty for none.</summary>
    /// <param name="cursor">The cursor as the statement names it.</param>
    public IReadOnlyList<DeclareCursorStatement> Cursors(CursorId cursor) =>
        cursor?.Name?.Identifier is { } name && cursors.TryGetValue(name.Value, out var found) ? found : [];

    /// <summary>Every DECLARE in a body, at any depth.</summary>
    private sealed class Collector : TSqlFragmentVisitor
    {
        public List<DeclareVariableElement> Variables { get; } = [];

        public List<DeclareTableVariableBody> Tables { get; } = [];

        public List<DeclareCursorStatement> Cursors { get; } = [];

        public override void Visit(DeclareVariableElement node) => Variables.Add(node);

        public override void Visit(DeclareCursorStatement node) => Cursors.Add(node);

        public override void Visit(DeclareTableVariableBody node) => Tables.Add(node);
    }
}

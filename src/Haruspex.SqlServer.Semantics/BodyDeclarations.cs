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
/// <remarks>
/// A table-valued parameter (<c>@lines sales.line_list READONLY</c>) and a variable declared with a table type
/// (<c>DECLARE @t sales.line_list</c>) are table variables of the type's definition (<see cref="TableType.As"/>), read as one
/// declared with its own columns is; one whose type isn't a table type the model defines stays a parameter or variable, and a
/// query that reads it is unknown.
/// </remarks>
public sealed class BodyDeclarations
{
    private readonly Dictionary<LocalName, VariableResolution> variables = [];
    private readonly Dictionary<string, List<DeclareCursorStatement>> cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DeclareVariableElement, VariableResolution> elements = new(ReferenceEqualityComparer.Instance);

    /// <param name="parameters">The parameters.</param>
    /// <param name="locals">The variables, each once.</param>
    /// <param name="tableVariables">The table variables, each once.</param>
    /// <param name="declared">Every name declared, as written, each time it is: a parameter's, a variable's or a table variable's.</param>
    private BodyDeclarations(IReadOnlyList<Parameter> parameters, IReadOnlyList<LocalVariable> locals, IReadOnlyList<DeclaredTable> tableVariables, IEnumerable<string> declared)
    {
        Parameters = parameters;
        Variables = locals;
        TableVariables = tableVariables;
        foreach (var parameter in parameters)
            variables.TryAdd(new LocalName(parameter.Name), new ParameterVariable(parameter));
        foreach (var local in locals)
            variables.TryAdd(local.Name, local);

        // Names that differ only in case are one name here; declared both ways, they prove the server compares names by case.
        foreach (var same in declared.GroupBy(n => new LocalName(n)))
        {
            if (same.Distinct(StringComparer.Ordinal).ToList() is { Count: > 1 } variants)
                variables[same.Key] = new CaseVariantVariable(same.Key, [.. variants]);
        }
    }

    /// <summary>A body that declares nothing and has no parameters: a view's.</summary>
    public static BodyDeclarations None { get; } = new([], [], [], []);

    /// <summary>Its parameters, in order.</summary>
    public IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>The variables it declares, each once and in the order declared; none of a parameter's name.</summary>
    public IReadOnlyList<LocalVariable> Variables { get; }

    /// <summary>Its table variables as declared, each once: a multi-statement function's return table first.</summary>
    public IReadOnlyList<DeclaredTable> TableVariables { get; }

    /// <summary>The declarations of every body of only parameters: a function's <c>RETURN</c> expression, an inline function's query.</summary>
    /// <param name="model">The model a parameter's table type is found in.</param>
    /// <param name="parameters">The parameters.</param>
    public static BodyDeclarations Of(DatabaseModel model, IReadOnlyList<Parameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new(parameters, [], TableParameters(model, parameters), parameters.Select(p => p.Name));
    }

    /// <summary>Each table-valued parameter whose type the model defines, as the table it is.</summary>
    private static List<DeclaredTable> TableParameters(DatabaseModel model, IReadOnlyList<Parameter> parameters) =>
        [.. parameters.Where(p => p.IsReadOnly).Select(p => SchemaRules.ResolveTableType(model, p.Type)?.As(p.Name)).OfType<DeclaredTable>()];

    /// <summary>Reads what a body declares.</summary>
    /// <param name="model">The model a table type a parameter or variable is declared with is found in.</param>
    /// <param name="parameters">The procedure's or function's parameters.</param>
    /// <param name="statements">Its body.</param>
    /// <param name="returnTable">A multi-statement function's return table, which its body fills as a table variable; null for a procedure.</param>
    /// <param name="source">Where the object whose body it is was read.</param>
    public static BodyDeclarations Read(DatabaseModel model, IReadOnlyList<Parameter> parameters, StatementList statements, DeclaredTable? returnTable, SourceSpan source)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(source);
        var found = new Collector();
        statements.Accept(found);

        // A table-valued parameter first, then a table variable: a batch declares a name once, so either is the name's.
        var tables = TableParameters(model, parameters);
        var locals = new List<LocalVariable>();
        foreach (var element in found.Variables)
        {
            var name = new LocalName(element.VariableName.Value);
            if (parameters.Any(p => new LocalName(p.Name) == name) || locals.Any(v => v.Name == name) || tables.Any(t => new LocalName(t.Name) == name))
                continue;
            var type = SyntaxReader.Type(element.DataType);
            if (SchemaRules.ResolveTableType(model, type) is { } tableType)
                tables.Add(tableType.As(element.VariableName.Value));
            else
                locals.Add(new LocalVariable(name, type, source.At(element)));
        }

        var declared = found.Tables.Select(t => SyntaxReader.Declared(t.VariableName.Value, t.Definition, source.Within(t)));
        foreach (var table in returnTable is null ? declared : declared.Prepend(returnTable))
        {
            if (!tables.Any(t => new LocalName(t.Name) == new LocalName(table.Name)))
                tables.Add(table);
        }

        var declaredNames = parameters.Select(p => p.Name)
            .Concat(found.Variables.Select(e => e.VariableName.Value))
            .Concat(found.Tables.Select(t => t.VariableName.Value))
            .Concat(returnTable is null ? [] : [returnTable.Name]);
        var result = new BodyDeclarations(parameters, locals, tables, declaredNames);
        foreach (var element in found.Variables)
            result.elements[element] = result.Variable(new LocalName(element.VariableName.Value));
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
    public VariableResolution Variable(LocalName name) =>
        variables.TryGetValue(name, out var found) ? found : new UndeclaredVariable(name);

    /// <summary>
    /// What a <c>DECLARE</c> element declares: the variable it names, which may be one declared before it of the same name (a
    /// batch declares a name once) or a parameter; undeclared for an element of another body.
    /// </summary>
    /// <param name="element">The element, <c>@v int = 1</c>.</param>
    public VariableResolution Declared(DeclareVariableElement element) =>
        elements.TryGetValue(element ?? throw new ArgumentNullException(nameof(element)), out var found) ? found : new UndeclaredVariable(new LocalName(element.VariableName.Value));

    /// <summary>Every declaration of the cursor a <c>FETCH</c>, <c>OPEN</c> or the like names, in the order written; empty for none.</summary>
    /// <param name="cursor">The cursor as the statement names it.</param>
    public IReadOnlyList<DeclareCursorStatement> Cursors(CursorId cursor) =>
        cursor?.Name?.Identifier is { } name && cursors.TryGetValue(name.Value, out var found) ? found : [];

    /// <summary>The table variables a fragment names, each once, in the order first named; one the body doesn't declare is left out.</summary>
    /// <param name="fragment">The fragment: a write's target, a WITH.</param>
    public IReadOnlyList<DeclaredTable> TableVariablesIn(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var found = new TableVariableFinder();
        fragment.Accept(found);
        return [.. found.Names.Select(n => TableVariables.FirstOrDefault(t => new LocalName(t.Name) == new LocalName(n))).OfType<DeclaredTable>().Distinct()];
    }

    private sealed class TableVariableFinder : TSqlFragmentVisitor
    {
        public List<string> Names { get; } = [];

        public override void Visit(VariableTableReference node) => Names.Add(node.Variable.Name);
    }

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

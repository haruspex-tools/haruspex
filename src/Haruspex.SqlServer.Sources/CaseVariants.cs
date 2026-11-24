using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Finds two names in one namespace that differ only in case (<c>Code</c> and <c>code</c>), each an error (HAR1022,
/// <see href="https://github.com/sharpblue/haruspex/blob/main/docs/decisions/0119-names-that-differ-only-in-case-are-an-error.md">decision 0119</see>).
/// </summary>
/// <remarks>
/// Every layer above matches names without case (<c>ObjectName</c>, <c>LocalName</c>, <c>Table.Column</c>). That is an
/// assumption, not SQL Server's rule: a database's collation decides how its columns and objects are compared, and the
/// server's how variables and temporary tables are. Where names are compared without case, two such names can't be made (Msg
/// 2705, 134, 2714), so matching without case gives what matching by case gives for any script that deploys. Two such names
/// in one namespace are the one case where it doesn't, so this check is what the assumption rests on, made once where each
/// namespace is read rather than at each comparison: a table's and a table type's columns, a schema's objects, and in each body
/// its parameters and variables, its temporary tables, and the columns of each table it declares. An object of one kind named
/// twice is HAR1002, reported where it is read, so the model holds one of them and the objects' namespace here finds a pair
/// of two kinds.
/// </remarks>
internal static class CaseVariants
{
    /// <summary>Every pair in the model's namespaces, and in those of the other databases it refers to.</summary>
    /// <param name="model">The model, once read.</param>
    public static IEnumerable<Diagnostic> In(DatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return [.. Of(model), .. model.Databases.Values.SelectMany(Of)];
    }

    /// <summary>
    /// Every pair in a query file: its declared parameters and the names its batch declares, as a body's, and the parameters it
    /// only uses, every spelling of them (<c>@Id</c> and <c>@id</c>), which reading would otherwise take for one.
    /// </summary>
    /// <param name="file">The query file's path, which names it and every location in it.</param>
    /// <param name="parameters">Its declared parameters.</param>
    /// <param name="statements">Its batch.</param>
    public static IEnumerable<Diagnostic> InQuery(string file, IReadOnlyList<Parameter> parameters, StatementList statements)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(statements);
        var source = new SourceSpan(file, 1, 1, 1, 1);
        var found = new Collector();
        statements.Accept(found);
        var declared = parameters.Select(p => p.Name).Concat(found.Variables.Select(v => v.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = found.Uses.Where(u => !declared.Contains(u.Name)).Select(u => (u.Name, source.At(u)));
        return [.. Body(file, source, parameters, statements, null), .. Pairs(used, names => $"{file} uses the parameters {names}")];
    }

    private static IEnumerable<Diagnostic> Of(DatabaseModel model)
    {
        // A schema's tables, views, synonyms, functions and procedures share one namespace (Msg 2714 for a second of either case).
        IEnumerable<SchemaObject> objects = [.. model.Tables, .. model.Views, .. model.Synonyms, .. model.Functions, .. model.Procedures];
        foreach (var found in Pairs(objects.Order(InSourceOrder.Instance).Select(o => (o.Name.ToString(), o.Span.Start)), names => $"The project defines {names}"))
            yield return found;

        foreach (var table in model.Tables)
        {
            foreach (var found in Columns(table.Name.ToString(), table.Columns))
                yield return found;
        }

        foreach (var type in model.UserTypes.OfType<TableType>())
        {
            foreach (var found in Columns(type.Name.ToString(), type.Columns))
                yield return found;
        }

        foreach (var module in model.Procedures.Concat<Module>(model.Functions).Concat(model.Triggers))
        {
            List<Parameter> parameters = module switch
            {
                Procedure p => [.. p.Parameters],
                Function f => [.. f.Parameters],
                _ => [],
            };
            var body = module switch
            {
                Procedure p => p.Body,
                ScalarFunction f => f.Body,
                TableFunction f => f.Body,
                Trigger t => t.Body,
                _ => null,
            };
            var returns = (module as TableFunction)?.Returns;
            foreach (var found in Body(module.Name.ToString(), module.Span, parameters, (body as SqlBody)?.Statements, returns))
                yield return found;
        }
    }

    /// <summary>
    /// A body's pairs: its parameters, variables and table variables (one namespace, Msg 134), its temporary tables (Msg 2714),
    /// and the columns of each table it declares or creates (Msg 2705).
    /// </summary>
    private static IEnumerable<Diagnostic> Body(string owner, SourceSpan source, IReadOnlyList<Parameter> parameters, StatementList? statements, DeclaredTable? returns)
    {
        var found = new Collector();
        statements?.Accept(found);

        List<(string, SourceLocation)> variables = [.. parameters.Select(p => (p.Name, p.At))];
        if (returns is not null)
            variables.Add((returns.Name, returns.Span.Start));
        variables.AddRange(found.Variables.Select(v => (v.Value, source.At(v))));
        foreach (var pair in Pairs(variables, names => $"{owner} declares the variables {names}"))
            yield return pair;

        foreach (var pair in Pairs(found.Temporary.Select(t => (t.Value, source.At(t))), names => $"{owner} creates the temporary tables {names}"))
            yield return pair;

        if (returns is not null)
        {
            foreach (var pair in Columns($"{returns.Name}, in {owner},", returns.Columns))
                yield return pair;
        }

        foreach (var (name, definition) in found.Tables)
        {
            var columns = definition.ColumnDefinitions.Select(c => (c.ColumnIdentifier.Value, source.At(c.ColumnIdentifier)));
            foreach (var pair in Pairs(columns, names => $"{name}, in {owner}, declares the columns {names}"))
                yield return pair;
        }
    }

    private static IEnumerable<Diagnostic> Columns(string owner, IEnumerable<Column> columns) =>
        Pairs(columns.Select(c => (c.Name, c.At)), names => $"{owner} declares the columns {names}");

    /// <summary>
    /// One error for each name written in more than one case, naming each spelling where it is first written, and placed at the
    /// second spelling's. One spelling written twice is no pair: the server refuses or accepts that whatever its collation.
    /// </summary>
    /// <param name="names">The namespace's names, in the order written.</param>
    /// <param name="says">The message's start, given the spellings and their places.</param>
    private static IEnumerable<Diagnostic> Pairs(IEnumerable<(string Name, SourceLocation At)> names, Func<string, string> says)
    {
        foreach (var same in names.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (same.DistinctBy(n => n.Name, StringComparer.Ordinal).ToList() is not { Count: > 1 } spellings)
                continue;
            var listed = spellings.Select(s => $"{s.Name} ({s.At})").ToList();
            var listing = string.Join(", ", listed[..^1]) + " and " + listed[^1];
            yield return SourceCodes.CaseVariants.At(
                spellings[1].At,
                $"{says(listing)}, whose names differ only in case: haruspex matches names without case, so it can't tell them apart.");
        }
    }

    /// <summary>Objects in the order their definitions are read, so the spelling read first is named first.</summary>
    private sealed class InSourceOrder : IComparer<SchemaObject>
    {
        public static InSourceOrder Instance { get; } = new();

        public int Compare(SchemaObject? x, SchemaObject? y) => SourceSpan.InSourceOrder.Compare(x?.Span, y?.Span);
    }

    /// <summary>What a body declares or creates, at any depth: SQL Server compiles a batch's declarations whether they run or not.</summary>
    private sealed class Collector : TSqlFragmentVisitor
    {
        /// <summary>Each variable's and table variable's name, as written where it is declared.</summary>
        public List<Identifier> Variables { get; } = [];

        /// <summary>Each temporary table's name, as written where it is created (<c>CREATE TABLE</c>, <c>SELECT … INTO</c>).</summary>
        public List<Identifier> Temporary { get; } = [];

        /// <summary>Each table it declares with its columns: a table variable or a temporary table.</summary>
        public List<(string Name, TableDefinition Definition)> Tables { get; } = [];

        /// <summary>Each variable a statement reads or sets, as written there.</summary>
        public List<VariableReference> Uses { get; } = [];

        public override void Visit(DeclareVariableElement node) => Variables.Add(node.VariableName);

        public override void Visit(VariableReference node) => Uses.Add(node);

        public override void Visit(DeclareTableVariableBody node)
        {
            Variables.Add(node.VariableName);
            if (node.Definition is { } definition)
                Tables.Add((node.VariableName.Value, definition));
        }

        public override void Visit(CreateTableStatement node)
        {
            if (TemporaryName(node.SchemaObjectName) is not { } name)
                return;
            Temporary.Add(name);
            if (node.Definition is { } definition)
                Tables.Add((name.Value, definition));
        }

        public override void Visit(SelectStatement node)
        {
            if (TemporaryName(node.Into) is { } name)
                Temporary.Add(name);
        }

        private static Identifier? TemporaryName(SchemaObjectName? name) =>
            name?.BaseIdentifier is { Value: ['#', ..] } identifier ? identifier : null;
    }
}

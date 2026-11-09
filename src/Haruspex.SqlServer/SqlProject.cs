using System.Text;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer;

/// <summary>
/// A database project read from a folder of <c>.sql</c> files: its tables and procedures, parsed once.
/// </summary>
/// <remarks>
/// <para>
/// Tables are typed from <c>CREATE TABLE</c> and procedures from <c>CREATE PROCEDURE</c>. A statement that
/// changes either afterwards is not applied, because which state wins depends on the order the scripts run,
/// which a folder does not say. Instead it withdraws the claim: a table changed by <c>ALTER TABLE</c> (other
/// than adding or checking constraints, which can only tighten it) or by <c>DROP TABLE</c> becomes unknown,
/// and a procedure named by <c>ALTER PROCEDURE</c> or <c>DROP PROCEDURE</c> has unknown results.
/// </para>
/// <para>
/// Every other statement is ignored. Most cannot change a table's columns or a procedure's body (indexes,
/// views, permissions, data). Those that can only do so through code haruspex does not run, such as
/// <c>EXEC sp_rename</c> or dynamic SQL, are a known limit: a database project defines its objects
/// declaratively, and such scripts belong in deployment, not in the project.
/// </para>
/// </remarks>
public sealed class SqlProject
{
    private readonly Catalog catalog;
    private readonly Dictionary<ObjectName, ProcedureSource> procedures;

    private SqlProject(Catalog catalog, Dictionary<ObjectName, ProcedureSource> procedures, IReadOnlyList<Diagnostic> diagnostics)
    {
        this.catalog = catalog;
        this.procedures = procedures;
        Diagnostics = diagnostics;
        Procedures = [.. procedures.Keys.OrderBy(n => n.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>Errors found while reading the project. Results inferred despite them are not trustworthy.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Every procedure in the project, in a stable order.</summary>
    public IReadOnlyList<ObjectName> Procedures { get; }

    /// <summary>Reads every <c>*.sql</c> file under <paramref name="directory"/>.</summary>
    public static SqlProject Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var diagnostics = new List<Diagnostic>();
        var tables = new Dictionary<ObjectName, TableSource>();
        var procedures = new Dictionary<ObjectName, ProcedureSource>();
        var tableChanges = new Dictionary<ObjectName, UnknownShape>();
        var others = new Dictionary<ObjectName, string>();
        var procedureChanges = new Dictionary<ObjectName, (UnknownShape Why, ProcedureSource? Alter)>();

        // Sorted by relative path so that which of two duplicate definitions is reported does not depend on
        // the file system's enumeration order.
        var files = Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(directory, path).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal);

        foreach (var (path, relative) in files)
        {
            if (Parse(path, relative, diagnostics) is not { } script)
                continue;

            foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            {
                switch (statement)
                {
                    case CreateTableStatement table when !table.SchemaObjectName.BaseIdentifier.Value.StartsWith('#'):
                        Add(tables, ObjectName.From(table.SchemaObjectName), new TableSource(ObjectName.From(table.SchemaObjectName), table.Definition, relative), table, relative, diagnostics);
                        break;
                    case CreateProcedureStatement or CreateOrAlterProcedureStatement:
                        var body = (ProcedureStatementBody)statement;
                        var name = ObjectName.From(body.ProcedureReference.Name);
                        Add(procedures, name, new ProcedureSource(name, body, relative), body, relative, diagnostics);
                        break;
                    case AlterProcedureStatement alter:
                        var altered = ObjectName.From(alter.ProcedureReference.Name);
                        procedureChanges.TryAdd(altered, (Changed(altered, "ALTER PROCEDURE", alter, relative), new ProcedureSource(altered, alter, relative)));
                        break;
                    case DropProcedureStatement drop:
                        foreach (var dropped in drop.Objects.Select(ObjectName.From))
                            procedureChanges.TryAdd(dropped, (Changed(dropped, "DROP PROCEDURE", drop, relative), null));
                        break;
                    // Objects a FROM clause can read besides tables. They are not modelled, but they must be known,
                    // because an unqualified name may resolve to one of them instead of to a table.
                    case ViewStatementBody view:
                        others.TryAdd(ObjectName.From(view.SchemaObjectName), "view");
                        break;
                    case CreateSynonymStatement synonym:
                        others.TryAdd(ObjectName.From(synonym.Name), "synonym");
                        break;
                    case FunctionStatementBody function:
                        others.TryAdd(ObjectName.From(function.Name), "function");
                        break;
                    case AlterTableStatement alterTable when !OnlyTightens(alterTable):
                        var changedTable = ObjectName.From(alterTable.SchemaObjectName);
                        tableChanges.TryAdd(changedTable, Changed(changedTable, "ALTER TABLE", alterTable, relative));
                        break;
                    case DropTableStatement dropTable:
                        foreach (var dropped in dropTable.Objects.Select(ObjectName.From))
                            tableChanges.TryAdd(dropped, Changed(dropped, "DROP TABLE", dropTable, relative));
                        break;
                }
            }
        }

        // A procedure that is also altered or dropped keeps its CREATE for its parameters, but its results are
        // withdrawn. One that is only altered is listed, so that selecting it reports why it is not typed.
        foreach (var (name, (why, alter)) in procedureChanges)
        {
            if (procedures.TryGetValue(name, out var created))
                procedures[name] = created with { Redefined = why };
            else if (alter is not null)
                procedures[name] = alter with { Redefined = why };
        }

        return new SqlProject(new Catalog(tables, tableChanges, others), procedures, diagnostics);
    }

    /// <summary>
    /// ALTER TABLE forms that cannot make a column nullable, change its type, or add or remove one: adding a
    /// constraint (a primary key can only add NOT NULL), checking or disabling constraints, rebuilding, and
    /// switching triggers or change tracking. Ignoring them can only leave a column more nullable than it is.
    /// </summary>
    private static bool OnlyTightens(AlterTableStatement statement) => statement switch
    {
        AlterTableAddTableElementStatement add => add.Definition.ColumnDefinitions.Count == 0,
        AlterTableConstraintModificationStatement or AlterTableRebuildStatement or AlterTableTriggerModificationStatement
            or AlterTableChangeTrackingModificationStatement => true,
        _ => false,
    };

    private static UnknownShape Changed(ObjectName name, string statement, TSqlFragment at, string file)
    {
        var location = new SourceLocation(file, at.StartLine, at.StartColumn);
        var reason = statement.EndsWith("PROCEDURE", StringComparison.Ordinal) ? IrUnknownReasons.Redefined : IrUnknownReasons.UnsupportedSource;
        return new UnknownShape(
            reason,
            $"{name} is changed by {statement} at {location}, and which definition applies depends on the order the scripts run.",
            location);
    }

    /// <summary>
    /// The procedures that match any of the patterns, and the patterns that match none. A pattern that matches
    /// nothing is reported, not ignored: a typo would otherwise quietly drop an interface from the generated code.
    /// </summary>
    public Selection Select(IReadOnlyList<ObjectPattern> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        var selected = Procedures.Where(p => include.Any(i => i.Matches(p))).ToList();
        var unmatched = include.Where(i => !Procedures.Any(i.Matches)).ToList();
        return new Selection(selected, unmatched);
    }

    /// <summary>Infers the IR for the given procedures.</summary>
    public IrDocument Infer(IReadOnlyList<ObjectName> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var analysed = selected
            .Distinct()
            .Select(name => procedures.TryGetValue(name, out var source)
                ? source
                : throw new ArgumentException($"No procedure named {name} in the project.", nameof(selected)))
            .Select(source => (source, Analyse(source)))
            .ToList();
        return new IrBuilder().Build(analysed);
    }

    private ProcedureAnalysis Analyse(ProcedureSource source)
    {
        var analysis = new ProcedureAnalyzer(catalog, source.File).Analyse(source.Body);
        return source.Redefined is { } why ? analysis with { Results = null, ResultsUnknown = why } : analysis;
    }

    private static TSqlScript? Parse(string path, string relative, List<Diagnostic> diagnostics)
    {
        // UTF-8 unless a byte order mark says otherwise, which covers the UTF-16 files SSMS writes.
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // The SQL Server 2025 grammar. Later the target platform will come from the project file.
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out var errors);
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(error.Message, new SourceLocation(relative, error.Line, error.Column)));
        return errors.Count == 0 ? fragment as TSqlScript : null;
    }

    private static void Add<T>(Dictionary<ObjectName, T> into, ObjectName name, T value, TSqlFragment at, string file, List<Diagnostic> diagnostics)
    {
        if (!into.TryAdd(name, value))
            diagnostics.Add(new Diagnostic($"{name} is defined more than once.", new SourceLocation(file, at.StartLine, at.StartColumn)));
    }
}

/// <summary>The outcome of <see cref="SqlProject.Select"/>.</summary>
public sealed record Selection(IReadOnlyList<ObjectName> Procedures, IReadOnlyList<ObjectPattern> Unmatched);

internal sealed record TableSource(ObjectName Name, TableDefinition Definition, string File);

/// <param name="Redefined">Set when another statement also changes the procedure, so its results are not inferred.</param>
internal sealed record ProcedureSource(ObjectName Name, ProcedureStatementBody Body, string File, UnknownShape? Redefined = null);

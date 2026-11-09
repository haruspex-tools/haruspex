using System.Text;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads a folder of <c>.sql</c> scripts, such as a SQL Server database project's, into a
/// <see cref="DatabaseModel"/>. It parses with ScriptDom, Microsoft's T-SQL parser, and needs neither DacFx nor a
/// database.
/// </summary>
/// <remarks>
/// <para>
/// Tables are read from <c>CREATE TABLE</c> and procedures from <c>CREATE PROCEDURE</c>. A statement that
/// changes either afterwards is not applied, because which state wins depends on the order the scripts run,
/// which a folder does not say. Instead it is recorded as a <see cref="Redefinition"/>: on a table for
/// <c>ALTER TABLE</c> (other than adding or checking constraints, which can only tighten it) or
/// <c>DROP TABLE</c>, and on a procedure for <c>ALTER PROCEDURE</c> or <c>DROP PROCEDURE</c>.
/// </para>
/// <para>
/// Views, synonyms and functions are recorded by name only, as <see cref="NamedObject"/>s. Every other statement
/// is ignored. Most cannot change a table's columns or a procedure's body (indexes, permissions, data). Those
/// that can only do so through code that is not run here, such as <c>EXEC sp_rename</c> or dynamic SQL, are a
/// known limit: a database project defines its objects declaratively, and such scripts belong in deployment,
/// not in the project.
/// </para>
/// </remarks>
public static class SqlScriptFolder
{
    /// <summary>Reads every <c>*.sql</c> file under <paramref name="directory"/>, including subfolders.</summary>
    /// <remarks>
    /// Files are read in order of their relative path, so the outcome does not depend on the file system. Each is
    /// UTF-8 unless a byte order mark says otherwise, and is parsed with the SQL Server 2025 grammar.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public static SchemaReadResult Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var diagnostics = new List<Diagnostic>();
        var tables = new Dictionary<ObjectName, Table>();
        var procedures = new Dictionary<ObjectName, Procedure>();
        var tableChanges = new Dictionary<ObjectName, Redefinition>();
        var others = new Dictionary<ObjectName, NamedObject>();
        var procedureChanges = new Dictionary<ObjectName, (Redefinition Why, Procedure? Alter)>();

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
                        Add(tables, ObjectName.From(table.SchemaObjectName), ReadTable(table, relative), table, relative, diagnostics);
                        break;
                    case CreateProcedureStatement or CreateOrAlterProcedureStatement:
                        var body = (ProcedureStatementBody)statement;
                        var name = ObjectName.From(body.ProcedureReference.Name);
                        Add(procedures, name, new Procedure(name, body, relative), body, relative, diagnostics);
                        break;
                    case AlterProcedureStatement alter:
                        var altered = ObjectName.From(alter.ProcedureReference.Name);
                        procedureChanges.TryAdd(altered, (Changed(altered, "ALTER PROCEDURE", alter, relative), new Procedure(altered, alter, relative)));
                        break;
                    case DropProcedureStatement drop:
                        foreach (var dropped in drop.Objects.Select(ObjectName.From))
                            procedureChanges.TryAdd(dropped, (Changed(dropped, "DROP PROCEDURE", drop, relative), null));
                        break;
                    // Objects a FROM clause can read besides tables. They are not modelled, but they must be known,
                    // because an unqualified name may resolve to one of them instead of to a table.
                    case ViewStatementBody view:
                        TryAddNamed(others, ObjectName.From(view.SchemaObjectName), NamedObjectKind.View);
                        break;
                    case CreateSynonymStatement synonym:
                        TryAddNamed(others, ObjectName.From(synonym.Name), NamedObjectKind.Synonym);
                        break;
                    case FunctionStatementBody function:
                        TryAddNamed(others, ObjectName.From(function.Name), NamedObjectKind.Function);
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

        // A procedure that is also altered or dropped keeps its CREATE for its parameters, with the first change
        // recorded. One that is only altered is listed with its ALTER, so that selecting it can report why its
        // definition is not trusted. One that is only dropped is not listed.
        foreach (var (name, (why, alter)) in procedureChanges)
        {
            if (procedures.TryGetValue(name, out var created))
                procedures[name] = new Procedure(created.Name, created.Body, created.File, why);
            else if (alter is not null)
                procedures[name] = new Procedure(alter.Name, alter.Body, alter.File, why);
        }

        var model = new DatabaseModel(tables.Values, procedures.Values, others.Values, tableChanges.Values);
        return new SchemaReadResult(model, diagnostics);
    }

    private static Table ReadTable(CreateTableStatement statement, string file)
    {
        var definition = statement.Definition;

        // Declared on the table, or on a column. SQL Server refuses a table with two primary keys, so collecting
        // every one only matters for a script that would not deploy.
        var primaryKey = definition.TableConstraints
            .OfType<UniqueConstraintDefinition>()
            .Where(u => u.IsPrimaryKey)
            .SelectMany(u => u.Columns)
            .Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)
            .Concat(definition.ColumnDefinitions
                .Where(c => c.Constraints.OfType<UniqueConstraintDefinition>().Any(u => u.IsPrimaryKey))
                .Select(c => c.ColumnIdentifier.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = new List<Column>();
        foreach (var column in definition.ColumnDefinitions)
        {
            var nullable = column.Constraints.OfType<NullableConstraintDefinition>().ToList();

            // A column that says both NULL and NOT NULL is recorded as NOT NULL, which is how inference read it
            // before the model existed (slice 2b changed no behaviour).
            var nullability = nullable.Any(n => !n.Nullable) ? DeclaredNullability.NotNull
                : nullable.Count > 0 ? DeclaredNullability.Null
                : DeclaredNullability.NotDeclared;

            columns.Add(new Column(
                column.ColumnIdentifier.Value,
                SqlType.From(column.DataType),
                nullability,
                isIdentity: column.IdentityOptions is not null,
                column.ComputedColumnExpression,
                new SourceLocation(file, column.StartLine, column.StartColumn)));
        }

        return new Table(ObjectName.From(statement.SchemaObjectName), columns, primaryKey, new SourceLocation(file, statement.StartLine, statement.StartColumn));
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

    private static Redefinition Changed(ObjectName name, string statement, TSqlFragment at, string file) =>
        new(name, statement, new SourceLocation(file, at.StartLine, at.StartColumn));

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

    private static void TryAddNamed(Dictionary<ObjectName, NamedObject> into, ObjectName name, NamedObjectKind kind) =>
        into.TryAdd(name, new NamedObject(name, kind));

    private static void Add<T>(Dictionary<ObjectName, T> into, ObjectName name, T value, TSqlFragment at, string file, List<Diagnostic> diagnostics)
    {
        if (!into.TryAdd(name, value))
            diagnostics.Add(new Diagnostic($"{name} is defined more than once.", new SourceLocation(file, at.StartLine, at.StartColumn)));
    }
}

/// <summary>What <see cref="SqlScriptFolder.Read"/> found.</summary>
/// <param name="Model">The schema. When there are diagnostics it is incomplete: a file that does not parse adds nothing.</param>
/// <param name="Diagnostics">Errors found while reading, such as parse errors and duplicate definitions.</param>
public sealed record SchemaReadResult(DatabaseModel Model, IReadOnlyList<Diagnostic> Diagnostics);

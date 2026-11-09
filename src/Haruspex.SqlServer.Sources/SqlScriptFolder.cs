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
/// Alias and CLR types are read from <c>CREATE TYPE</c>, and CHECK constraints from <c>CREATE TABLE</c>. A check that
/// <c>ALTER TABLE … NOCHECK CONSTRAINT</c> names (or <c>ALL</c>) is recorded as possibly disabled, unless
/// <c>WITH CHECK CHECK CONSTRAINT</c> validates it again later in the same script (see Untrusted).
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
        var userTypes = new Dictionary<ObjectName, UserType>();

        // ALTER TABLE … NOCHECK CONSTRAINT and … WITH CHECK CHECK CONSTRAINT, by table, in the order read.
        var enforcement = new Dictionary<ObjectName, List<Enforcement>>();

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
                    case CreateTypeUddtStatement alias when SqlType.From(alias.DataType) is { } baseType:
                        var aliasName = ObjectName.From(alias.Name);
                        var aliasNullability = alias.NullableConstraint is null ? DeclaredNullability.NotDeclared
                            : alias.NullableConstraint.Nullable ? DeclaredNullability.Null : DeclaredNullability.NotNull;
                        Add(userTypes, aliasName, new UserType(aliasName, baseType, aliasNullability, new SourceLocation(relative, alias.StartLine, alias.StartColumn)), alias, relative, diagnostics);
                        break;
                    // Recorded so that an unqualified type name that could mean it is not resolved to an alias type
                    // of the same name in another schema.
                    case CreateTypeUdtStatement clr:
                        var clrName = ObjectName.From(clr.Name);
                        Add(userTypes, clrName, new UserType(clrName, null, DeclaredNullability.NotDeclared, new SourceLocation(relative, clr.StartLine, clr.StartColumn)), clr, relative, diagnostics);
                        break;
                    case AlterTableConstraintModificationStatement change
                        when change.ConstraintEnforcement == ConstraintEnforcement.NoCheck
                        || (change.ConstraintEnforcement == ConstraintEnforcement.Check && change.ExistingRowsCheckEnforcement == ConstraintEnforcement.Check):
                        // NOCHECK disables the named constraints (or ALL); WITH CHECK CHECK enables them and validates
                        // every row, failing if any breaks them, so afterwards they hold again. A plain CHECK
                        // CONSTRAINT enables without validating, leaving the rows written meanwhile (the constraint
                        // stays not trusted), so it restores nothing and isn't recorded. See Untrusted.
                        var changedChecks = ObjectName.From(change.SchemaObjectName);
                        if (!enforcement.TryGetValue(changedChecks, out var list))
                            enforcement[changedChecks] = list = [];
                        list.Add(new Enforcement(
                            change.ConstraintEnforcement == ConstraintEnforcement.Check,
                            change.All ? null : [.. change.ConstraintNames.Select(n => n.Value)],
                            new SourceLocation(relative, change.StartLine, change.StartColumn),
                            list.Count));
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

        foreach (var (name, changes) in enforcement)
        {
            if (tables.TryGetValue(name, out var table))
                tables[name] = Untrusted(table, changes);
        }

        var model = new DatabaseModel(tables.Values, procedures.Values, others.Values, tableChanges.Values, userTypes.Values);
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
                new SourceLocation(file, column.StartLine, column.StartColumn),
                column.Collation?.Value));
        }

        // Column constraints first, then table constraints, each in declaration order. ALTER TABLE … ADD CONSTRAINT
        // is not read (see OnlyTightens): a check added later is left out, which only claims less.
        var checks = definition.ColumnDefinitions
            .SelectMany(c => c.Constraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, c.ColumnIdentifier.Value, file)))
            .Concat(definition.TableConstraints.OfType<CheckConstraintDefinition>().Select(k => Check(k, null, file)))
            .ToList();

        return new Table(ObjectName.From(statement.SchemaObjectName), columns, primaryKey, new SourceLocation(file, statement.StartLine, statement.StartColumn), checks);
    }

    private static CheckConstraint Check(CheckConstraintDefinition check, string? column, string file) =>
        new(check.ConstraintIdentifier?.Value, check.CheckCondition, column, check.NotForReplication, new SourceLocation(file, check.StartLine, check.StartColumn));

    /// <summary>ALTER TABLE … NOCHECK CONSTRAINT, or … WITH CHECK CHECK CONSTRAINT.</summary>
    /// <param name="Validates">True for WITH CHECK CHECK CONSTRAINT; false for NOCHECK.</param>
    /// <param name="At">Where it is.</param>
    /// <param name="Constraints">The constraints named, or null for ALL.</param>
    /// <param name="Order">Its position among the table's enforcement statements, in reading order.</param>
    private sealed record Enforcement(bool Validates, IReadOnlyList<string>? Constraints, SourceLocation At, int Order)
    {
        public bool Covers(CheckConstraint check) =>
            Constraints is null || (check.Name is not null && Constraints.Contains(check.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The table with each check that a NOCHECK statement may have left untrusted marked as possibly disabled.
    /// </summary>
    /// <remarks>
    /// A constraint guarantees nothing unless SQL Server trusts it: while disabled it checks no row, and rows written
    /// then stay after a plain re-enable. Only <c>WITH CHECK CHECK CONSTRAINT</c> validates every row, so a disable is
    /// undone only by one that runs after it. Within one script statements run in order; across scripts the order is
    /// not known, so a re-enable in another file doesn't count. (A constraint added later by ALTER TABLE, with or
    /// without WITH NOCHECK, is not read at all; see OnlyTightens.)
    /// </remarks>
    private static Table Untrusted(Table table, List<Enforcement> changes)
    {
        var checks = table.Checks.Select(check =>
        {
            var disabled = changes.FirstOrDefault(d => !d.Validates && d.Covers(check)
                && !changes.Any(v => v.Validates && v.Covers(check) && v.At.File == d.At.File && v.Order > d.Order));
            return disabled is null ? check : check.WithDisabledAt(disabled.At);
        }).ToList();
        return new Table(table.Name, table.Columns, table.PrimaryKey, table.At, checks);
    }

    /// <summary>
    /// ALTER TABLE forms that cannot make a column nullable, change its type, or add or remove one: adding a
    /// constraint (a primary key can only add NOT NULL), checking or disabling constraints, rebuilding, and
    /// switching triggers or change tracking. Ignoring them can only leave a column more nullable than it is.
    /// Disabling a CHECK constraint does matter to what values a column holds; that is recorded on the constraint
    /// (see Read), not as a redefinition of the table. A CHECK added this way is not read, which only claims less.
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

using System.Text;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
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
/// <c>ALTER TABLE … NOCHECK CONSTRAINT</c> may name (or <c>ALL</c>), anywhere in a script, procedure body included,
/// is recorded as possibly disabled, unless <c>WITH CHECK CHECK CONSTRAINT</c> validates it again later in the
/// same script (see Untrusted).
/// </para>
/// <para>
/// Functions are read from <c>CREATE FUNCTION</c>, with the same rule as procedures for <c>ALTER FUNCTION</c> and
/// <c>DROP FUNCTION</c>, and are also recorded by name, as views and synonyms are, as <see cref="NamedObject"/>s.
/// Every other statement
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

        return Read(directory, Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path)));
    }

    /// <summary>
    /// Reads the <c>.sql</c> files named, relative to <paramref name="directory"/>: a chosen set of a folder's scripts, such as
    /// the ones a project file builds.
    /// </summary>
    /// <remarks>As <see cref="Read(string)"/> does, it reads them in order of their relative path.</remarks>
    /// <param name="directory">The project's folder, which positions in the model are relative to.</param>
    /// <param name="files">The scripts, relative to <paramref name="directory"/>, with either kind of slash.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">A file named does not exist.</exception>
    public static SchemaReadResult Read(string directory, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var diagnostics = new List<Diagnostic>();
        var tables = new Dictionary<ObjectName, Table>();
        var procedures = new Dictionary<ObjectName, Procedure>();
        var tableChanges = new Dictionary<ObjectName, Redefinition>();
        var others = new Dictionary<ObjectName, NamedObject>();
        var procedureChanges = new Dictionary<ObjectName, (Redefinition Why, Procedure? Alter)>();
        var userTypes = new Dictionary<ObjectName, UserType>();
        var functions = new Dictionary<ObjectName, UserFunction>();
        var functionChanges = new Dictionary<ObjectName, (Redefinition Why, UserFunction? Alter)>();
        var triggers = new List<Trigger>();
        var cascadingKeys = new List<CascadingKey>();
        var views = new Dictionary<ObjectName, List<(ViewStatementBody Body, string File, bool AnsiNullsOff)>>();

        // ALTER TABLE … NOCHECK CONSTRAINT and … WITH CHECK CHECK CONSTRAINT, wherever they are, in the order read.
        var enforcement = new List<Enforcement>();

        // ALTER INDEX … DISABLE, wherever it is.
        var disables = new List<IndexDisable>();

        // Sorted by relative path so that which of two duplicate definitions is reported does not depend on
        // the file system's enumeration order.
        var scripts = files
            .Select(file => file.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Select(relative => (Path: Path.Combine(directory, relative), Relative: relative))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();
        if (scripts.FirstOrDefault(f => !File.Exists(f.Path)) is { Path: not null } missing)
            throw new FileNotFoundException($"File not found: {missing.Relative}", missing.Path);

        foreach (var (path, relative) in scripts)
        {
            if (Parse(path, relative, diagnostics) is not { } script)
                continue;

            ReadEnforcement(script, relative, enforcement);
            ReadIndexDisables(script, relative, disables);

            // ANSI_NULLS is kept with a procedure or function as it was when it was created (observed), so a SET before its
            // definition in the same file is the one it runs with. Another file's SET may run before it too, in an order
            // the folder does not say; that is not followed, and the default, on, is assumed.
            var ansiNullsOff = false;
            foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            {
                switch (statement)
                {
                    case PredicateSetStatement set when (set.Options & (SetOptions.AnsiNulls | SetOptions.AnsiDefaults)) != 0:
                        // ANSI_DEFAULTS ON sets ANSI_NULLS on, and OFF off, as SET ANSI_NULLS does.
                        ansiNullsOff = !set.IsOn;
                        break;
                    case CreateTableStatement table when !table.SchemaObjectName.BaseIdentifier.Value.StartsWith('#'):
                        Add(tables, SyntaxReader.Name(table.SchemaObjectName), ReadTable(table, relative), table, relative, diagnostics);
                        CascadingKeys(SyntaxReader.Name(table.SchemaObjectName), table.Definition, relative, cascadingKeys);
                        break;
                    case TriggerStatementBody trigger when ReadTrigger(trigger, relative) is { } read:
                        triggers.Add(read);
                        break;
                    case CreateProcedureStatement or CreateOrAlterProcedureStatement:
                        var body = (ProcedureStatementBody)statement;
                        var name = SyntaxReader.Name(body.ProcedureReference.Name);
                        Add(procedures, name, new Procedure(name, body, relative) { AnsiNullsOff = ansiNullsOff }, body, relative, diagnostics);
                        break;
                    case AlterProcedureStatement alter:
                        var altered = SyntaxReader.Name(alter.ProcedureReference.Name);
                        procedureChanges.TryAdd(altered, (Changed(altered, "ALTER PROCEDURE", alter, relative), new Procedure(altered, alter, relative) { AnsiNullsOff = ansiNullsOff }));
                        break;
                    case DropProcedureStatement drop:
                        foreach (var dropped in drop.Objects.Select(SyntaxReader.Name))
                            procedureChanges.TryAdd(dropped, (Changed(dropped, "DROP PROCEDURE", drop, relative), null));
                        break;
                    // Objects a FROM clause can read besides tables. They are not modelled, but they must be known,
                    // because an unqualified name may resolve to one of them instead of to a table.
                    case ViewStatementBody view:
                        TryAddNamed(others, SyntaxReader.Name(view.SchemaObjectName), NamedObjectKind.View);
                        if (!views.TryGetValue(SyntaxReader.Name(view.SchemaObjectName), out var definitions))
                            views[SyntaxReader.Name(view.SchemaObjectName)] = definitions = [];
                        definitions.Add((view, relative, ansiNullsOff));
                        break;
                    case CreateSynonymStatement synonym:
                        others.TryAdd(SyntaxReader.Name(synonym.Name), new NamedObject(SyntaxReader.Name(synonym.Name), NamedObjectKind.Synonym) { SynonymFor = synonym.ForName });
                        break;
                    case CreateFunctionStatement or CreateOrAlterFunctionStatement:
                        var function = (FunctionStatementBody)statement;
                        var functionName = SyntaxReader.Name(function.Name);
                        TryAddNamed(others, functionName, NamedObjectKind.Function);
                        Add(functions, functionName, new UserFunction(functionName, function, relative) { AnsiNullsOff = ansiNullsOff }, function, relative, diagnostics);
                        break;
                    case AlterFunctionStatement alterFunction:
                        var alteredFunction = SyntaxReader.Name(alterFunction.Name);
                        TryAddNamed(others, alteredFunction, NamedObjectKind.Function);
                        functionChanges.TryAdd(alteredFunction, (Changed(alteredFunction, "ALTER FUNCTION", alterFunction, relative), new UserFunction(alteredFunction, alterFunction, relative) { AnsiNullsOff = ansiNullsOff }));
                        break;
                    case DropFunctionStatement dropFunction:
                        foreach (var dropped in dropFunction.Objects.Select(SyntaxReader.Name))
                            functionChanges.TryAdd(dropped, (Changed(dropped, "DROP FUNCTION", dropFunction, relative), null));
                        break;
                    case CreateTypeUddtStatement alias when SyntaxReader.Type(alias.DataType) is { } baseType:
                        var aliasName = SyntaxReader.Name(alias.Name);
                        var aliasNullability = alias.NullableConstraint is null ? DeclaredNullability.NotDeclared
                            : alias.NullableConstraint.Nullable ? DeclaredNullability.Null : DeclaredNullability.NotNull;
                        Add(userTypes, aliasName, new UserType(aliasName, baseType, aliasNullability, new SourceLocation(relative, alias.StartLine, alias.StartColumn)), alias, relative, diagnostics);
                        break;
                    // Recorded so that an unqualified type name that could mean it is not resolved to an alias type
                    // of the same name in another schema.
                    case CreateTypeUdtStatement clr:
                        var clrName = SyntaxReader.Name(clr.Name);
                        Add(userTypes, clrName, new UserType(clrName, null, DeclaredNullability.NotDeclared, new SourceLocation(relative, clr.StartLine, clr.StartColumn)), clr, relative, diagnostics);
                        break;
                    case AlterTableAddTableElementStatement addKey when OnlyTightens(addKey):
                        CascadingKeys(SyntaxReader.Name(addKey.SchemaObjectName), addKey.Definition, relative, cascadingKeys);
                        break;
                    case AlterTableStatement alterTable when !OnlyTightens(alterTable):
                        var changedTable = SyntaxReader.Name(alterTable.SchemaObjectName);
                        tableChanges.TryAdd(changedTable, Changed(changedTable, "ALTER TABLE", alterTable, relative));
                        if (alterTable is AlterTableAddTableElementStatement addColumn)
                            CascadingKeys(changedTable, addColumn.Definition, relative, cascadingKeys);
                        break;
                    case DropTableStatement dropTable:
                        foreach (var dropped in dropTable.Objects.Select(SyntaxReader.Name))
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
                procedures[name] = new Procedure(created.Name, created.Body, created.File, why) { AnsiNullsOff = created.AnsiNullsOff };
            else if (alter is not null)
                procedures[name] = new Procedure(alter.Name, alter.Body, alter.File, why) { AnsiNullsOff = alter.AnsiNullsOff };
        }

        // Functions follow the same rule as procedures.
        foreach (var (name, (why, alter)) in functionChanges)
        {
            if (functions.TryGetValue(name, out var created))
                functions[name] = new UserFunction(created.Name, created.Body, created.File, why) { AnsiNullsOff = created.AnsiNullsOff };
            else if (alter is not null)
                functions[name] = new UserFunction(alter.Name, alter.Body, alter.File, why) { AnsiNullsOff = alter.AnsiNullsOff };
        }

        foreach (var name in tables.Keys.ToList())
            tables[name] = Untrusted(tables[name], enforcement, disables);

        // A view defined once keeps its definition; one created and altered depends on the order the scripts run.
        foreach (var (name, definitions) in views)
        {
            if (definitions.Count == 1 && others.TryGetValue(name, out var view) && view.Kind == NamedObjectKind.View)
                others[name] = view with { View = definitions[0].Body, File = definitions[0].File, AnsiNullsOff = definitions[0].AnsiNullsOff };
        }

        var model = new DatabaseModel(tables.Values, procedures.Values, others.Values, tableChanges.Values, userTypes.Values, functions.Values, triggers, cascadingKeys);
        return new SchemaReadResult(model, diagnostics);
    }

    /// <summary>
    /// A DML trigger (<c>CREATE</c>, <c>CREATE OR ALTER</c> or <c>ALTER TRIGGER</c>); null for a DDL or logon trigger, which no
    /// DML fires. Every definition is kept, an <c>ALTER</c> included, since which applies depends on the order the scripts run
    /// and a reader asking what triggers may do needs them all.
    /// </summary>
    private static Trigger? ReadTrigger(TriggerStatementBody trigger, string file)
    {
        if (trigger.TriggerObject is not { TriggerScope: TriggerScope.Normal, Name: { } on })
            return null;
        var events = DmlEvents.None;
        foreach (var action in trigger.TriggerActions)
        {
            events |= action.TriggerActionType switch
            {
                TriggerActionType.Insert => DmlEvents.Insert,
                TriggerActionType.Update => DmlEvents.Update,
                TriggerActionType.Delete => DmlEvents.Delete,
                _ => DmlEvents.None,
            };
        }

        var parent = SyntaxReader.Name(on);

        // A trigger is in its table's schema, whatever its own name says.
        var name = new ObjectName(parent.Schema, trigger.Name.BaseIdentifier.Value);
        return new Trigger(name, parent, trigger.TriggerType == TriggerType.InsteadOf, events, trigger.MethodSpecifier is null ? trigger.StatementList : null, new SourceLocation(file, trigger.StartLine, trigger.StartColumn));
    }

    /// <summary>
    /// The foreign keys of a table definition (of <c>CREATE TABLE</c> or <c>ALTER TABLE … ADD</c>) that have an <c>ON DELETE</c>
    /// or <c>ON UPDATE</c> action: a change to the referenced table's rows changes this table's, and fires its triggers.
    /// </summary>
    private static void CascadingKeys(ObjectName table, TableDefinition? definition, string file, List<CascadingKey> into)
    {
        if (definition is null)
            return;
        var keys = definition.TableConstraints.OfType<ForeignKeyConstraintDefinition>()
            .Concat(definition.ColumnDefinitions.SelectMany(c => c.Constraints.OfType<ForeignKeyConstraintDefinition>()));
        foreach (var key in keys)
        {
            if (key.DeleteAction is DeleteUpdateAction.NotSpecified or DeleteUpdateAction.NoAction
                && key.UpdateAction is DeleteUpdateAction.NotSpecified or DeleteUpdateAction.NoAction)
            {
                continue;
            }

            into.Add(new CascadingKey(table, SyntaxReader.Name(key.ReferenceTableName), new SourceLocation(file, key.StartLine, key.StartColumn)));
        }
    }

    private static Table ReadTable(CreateTableStatement statement, string file) =>
        SyntaxReader.Table(SyntaxReader.Name(statement.SchemaObjectName), statement.Definition, file, new SourceLocation(file, statement.StartLine, statement.StartColumn), statement.AsNode || statement.AsEdge);

    /// <summary>ALTER TABLE … NOCHECK CONSTRAINT, or … WITH CHECK CHECK CONSTRAINT.</summary>
    /// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
    /// <param name="Qualified">Whether the table's name has its schema.</param>
    /// <param name="Validates">True for WITH CHECK CHECK CONSTRAINT; false for NOCHECK.</param>
    /// <param name="Constraints">The constraints named, or null for ALL.</param>
    /// <param name="At">Where it is.</param>
    /// <param name="Order">Its position among all such statements, in reading order.</param>
    /// <param name="TopLevel">Whether it is a statement of a batch, rather than inside IF, a block, or a body.</param>
    private sealed record Enforcement(ObjectName Table, bool Qualified, bool Validates, IReadOnlyList<string>? Constraints, SourceLocation At, int Order, bool TopLevel)
    {
        /// <summary>
        /// Whether it may be about this table: the one it names, or, when its name has no schema, any table of that
        /// name, since SQL Server looks in the caller's default schema first and the source does not say whose.
        /// </summary>
        public bool May(Table table) => Qualified
            ? Table == table.Name
            : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether it may name <paramref name="check"/>: ALL, its name, or, for a check declared without a name, a
        /// name the table doesn't declare. SQL Server generates a name for an unnamed constraint (CK__t__c__3A81B327),
        /// which a script copied from a live database can use.
        /// </summary>
        public bool MayName(CheckConstraint check, Table table) =>
            Constraints is null
            || (check.Name is not null && Constraints.Contains(check.Name, StringComparer.OrdinalIgnoreCase))
            || (check.Name is null && Constraints.Any(n => !table.Checks.Any(c => n.Equals(c.Name, StringComparison.OrdinalIgnoreCase))));

        /// <summary>Whether it certainly names <paramref name="check"/>, which a re-enable must, to count.</summary>
        public bool Names(CheckConstraint check) =>
            Constraints is null || (check.Name is not null && Constraints.Contains(check.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every NOCHECK CONSTRAINT and WITH CHECK CHECK CONSTRAINT in a script, at any depth: a batch's statements,
    /// and those under IF, in blocks, TRY/CATCH and loops, and in procedure, trigger and function bodies. A plain
    /// CHECK CONSTRAINT enables without validating, leaving the rows written meanwhile (the constraint stays not
    /// trusted), so it restores nothing and isn't recorded. NOCHECK in dynamic SQL (EXEC of a string) is not seen.
    /// </summary>
    private static void ReadEnforcement(TSqlScript script, string file, List<Enforcement> into)
    {
        var topLevel = script.Batches.SelectMany(b => b.Statements).ToHashSet(ReferenceEqualityComparer.Instance);
        var visitor = new EnforcementVisitor();
        script.Accept(visitor);
        foreach (var change in visitor.Found)
        {
            var validates = change.ConstraintEnforcement == ConstraintEnforcement.Check && change.ExistingRowsCheckEnforcement == ConstraintEnforcement.Check;
            if (change.ConstraintEnforcement != ConstraintEnforcement.NoCheck && !validates)
                continue;
            into.Add(new Enforcement(
                SyntaxReader.Name(change.SchemaObjectName),
                change.SchemaObjectName.SchemaIdentifier is not null,
                validates,
                change.All ? null : [.. change.ConstraintNames.Select(n => n.Value)],
                new SourceLocation(file, change.StartLine, change.StartColumn),
                into.Count,
                topLevel.Contains(change)));
        }
    }

    private sealed class EnforcementVisitor : TSqlFragmentVisitor
    {
        public List<AlterTableConstraintModificationStatement> Found { get; } = [];

        public override void Visit(AlterTableConstraintModificationStatement node) => Found.Add(node);
    }

    /// <summary>
    /// The table with each check that a NOCHECK may have left untrusted marked as possibly disabled.
    /// </summary>
    /// <remarks>
    /// A constraint guarantees nothing unless SQL Server trusts it: while disabled it checks no row, and rows written
    /// then stay after a plain re-enable. So a NOCHECK that may be about the check disables it, wherever it is, since
    /// whether a statement in a body or under IF runs is not knowable. Only <c>WITH CHECK CHECK CONSTRAINT</c>
    /// validates every row, and it undoes a disable only when both are statements of batches in the same script, the
    /// re-enable after it, naming the table with its schema and the check by name (or ALL): then it certainly runs
    /// after it on that check. Across scripts the order isn't known. (A constraint added later by ALTER TABLE, with or
    /// without WITH NOCHECK, is not read at all; see OnlyTightens.)
    /// </remarks>
    private static Table Untrusted(Table table, List<Enforcement> changes, List<IndexDisable> disables)
    {
        var keys = Disabled(table, disables);
        var relevant = changes.Where(c => c.May(table)).ToList();
        if (relevant.Count == 0)
            return keys == table.UniqueKeys ? table : new Table(table.Name, table.Columns, table.PrimaryKey, table.At, table.Checks, keys) { IsGraph = table.IsGraph };

        var checks = table.Checks.Select(check =>
        {
            var disabled = relevant.FirstOrDefault(d => !d.Validates && d.MayName(check, table)
                && !(d.TopLevel && relevant.Any(v => v.Validates && v.TopLevel && v.Qualified && v.Names(check)
                    && v.At.File == d.At.File && v.Order > d.Order)));
            return disabled is null ? check : check.WithDisabledAt(disabled.At);
        }).ToList();
        return new Table(table.Name, table.Columns, table.PrimaryKey, table.At, checks, keys) { IsGraph = table.IsGraph };
    }

    /// <summary><c>ALTER INDEX name ON t DISABLE</c> or <c>ALTER INDEX ALL ON t DISABLE</c>.</summary>
    /// <param name="Table">The table it names; in <c>dbo</c> when <paramref name="Qualified"/> is false.</param>
    /// <param name="Qualified">Whether the table's name has its schema.</param>
    /// <param name="Index">The index named, or null for ALL.</param>
    /// <param name="At">Where it is.</param>
    private sealed record IndexDisable(ObjectName Table, bool Qualified, string? Index, SourceLocation At)
    {
        public bool May(Table table) => Qualified
            ? Table == table.Name
            : Table.Name.Equals(table.Name.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every <c>ALTER INDEX … DISABLE</c> in a script, at any depth, as for NOCHECK: whether a statement in a body or under IF
    /// runs is not knowable. A disabled unique index enforces nothing, so rows written meanwhile may share a key.
    /// </summary>
    private static void ReadIndexDisables(TSqlScript script, string file, List<IndexDisable> into)
    {
        var visitor = new IndexVisitor();
        script.Accept(visitor);
        foreach (var alter in visitor.Found.Where(a => a.AlterIndexType == AlterIndexType.Disable))
        {
            into.Add(new IndexDisable(
                SyntaxReader.Name(alter.OnName),
                alter.OnName.SchemaIdentifier is not null,
                alter.All ? null : alter.Name?.Value,
                new SourceLocation(file, alter.StartLine, alter.StartColumn)));
        }
    }

    private sealed class IndexVisitor : TSqlFragmentVisitor
    {
        public List<AlterIndexStatement> Found { get; } = [];

        public override void Visit(AlterIndexStatement node) => Found.Add(node);
    }

    /// <summary>
    /// The table's keys, each one a disable may name marked as possibly disabled: ALL, its name (a key's index has the
    /// constraint's name), or, for a key declared without a name, any name the table doesn't declare, since SQL Server
    /// generates one. Re-enabling (a rebuild) is not followed, which only claims less.
    /// </summary>
    private static IReadOnlyList<UniqueKey> Disabled(Table table, List<IndexDisable> disables)
    {
        var relevant = disables.Where(d => d.May(table)).ToList();
        if (relevant.Count == 0)
            return table.UniqueKeys;

        bool Names(IndexDisable d, UniqueKey key) =>
            d.Index is null
            || (key.Name is not null && key.Name.Equals(d.Index, StringComparison.OrdinalIgnoreCase))
            || (key.Name is null && !table.UniqueKeys.Any(k => d.Index.Equals(k.Name, StringComparison.OrdinalIgnoreCase)));

        return [.. table.UniqueKeys.Select(key => relevant.FirstOrDefault(d => Names(d, key)) is { } disable ? key with { DisabledAt = disable.At } : key)];
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
        string text;
        using (var file = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = file.ReadToEnd();

        // What the server would receive from sqlcmd, which an install script written for it needs.
        using var reader = new StringReader(SqlcmdVariables.Substitute(text));

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

using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads one parsed script into what the folder's scripts declare: each definition it makes, each statement that changes
/// one later, and each statement that may disable or drop a constraint. It applies only its own order, to the procedures,
/// functions and views it names (<see cref="ScriptDefinitions{T}"/>); the passes after every script is read
/// (<see cref="DefinitionsInEffect"/>, <see cref="ConstraintTrust"/>) settle what the model says.
/// </summary>
/// <remarks>
/// Every other statement is passed over. Most cannot change a table's columns or a procedure's body (indexes, permissions,
/// data). Those that can only do so through code that is not run here, such as <c>EXEC sp_rename</c> or dynamic SQL, are a
/// known limit: a database project defines its objects declaratively, and such scripts belong in deployment, not in the
/// project.
/// </remarks>
/// <param name="file">The script's path relative to the folder, which every location in it names.</param>
/// <param name="into">What the folder's scripts declare, which this script adds to.</param>
/// <param name="settings">The settings the script starts with (<see cref="ScriptSettings"/>).</param>
internal sealed class ScriptReader(string file, SchemaDeclarations into, ScriptSettings settings)
{
    /// <summary>
    /// Whether <c>ANSI_NULLS</c> is off at this point of the script. A procedure, function or view keeps the setting in force
    /// when it was created (observed), so a SET before its definition in the same file is the one it runs with. Another
    /// file's SET may run before it too, in an order the folder does not say; that is not followed, and the script starts with
    /// its own setting: a folder's with the default, on, a project's with the project's.
    /// </summary>
    private bool ansiNullsOff = !settings.AnsiNulls;

    /// <summary>The procedures this script defines, alters or drops, played in its order (<see cref="ScriptDefinitions{T}"/>).</summary>
    private readonly ScriptDefinitions<Procedure> procedures = new(into.Diagnostics);

    /// <summary>The functions this script defines, alters or drops, played in its order; one never replaces another of another kind.</summary>
    private readonly ScriptDefinitions<Function> functions = new(into.Diagnostics, (standing, replacing) => standing.GetType() == replacing.GetType());

    /// <summary>The views this script defines, alters or drops, played in its order.</summary>
    private readonly ScriptDefinitions<ViewDefinition> views = new(into.Diagnostics);

    /// <summary>A script that starts with the default settings, as a folder's does.</summary>
    public ScriptReader(string file, SchemaDeclarations into)
        : this(file, into, ScriptSettings.Default)
    {
    }

    /// <summary>Reads the script's statements, in order, and every statement at any depth that may disable a constraint.</summary>
    public void Read(TSqlScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ReadEnforcement(script);
        ReadKeyDisables(script);
        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            Read(statement);

        // The script has run: what it leaves of each procedure, function and view is settled against the other scripts' later.
        into.Settle(procedures.Outcomes, into.Procedures, into.ProcedureChanges);
        into.Settle(functions.Outcomes, into.Functions, into.FunctionChanges);
        into.Settle(views.Outcomes);
    }

    /// <summary>One statement of a batch: what it defines or changes, by kind, one reader each.</summary>
    private void Read(TSqlStatement statement)
    {
        switch (statement)
        {
            case PredicateSetStatement set when (set.Options & (SetOptions.AnsiNulls | SetOptions.AnsiDefaults)) != 0:
                // ANSI_DEFAULTS ON sets ANSI_NULLS on, and OFF off, as SET ANSI_NULLS does.
                ansiNullsOff = !set.IsOn;
                break;
            case CreateTableStatement table when !table.SchemaObjectName.BaseIdentifier.Value.StartsWith('#'):
                ReadTable(table);
                break;
            case AlterTableStatement alterTable:
                ReadAlterTable(alterTable);
                break;
            case CreateIndexStatement { Unique: true, FilterPredicate: null } index when NamesThisDatabasesTable(index.OnName):
                // As a key ALTER TABLE adds (AddedKey), and passed over in the same places.
                into.AddedKeys.Add(new AddedKey(SyntaxReader.Name(index.OnName), SyntaxReader.Key(index, SyntaxReader.Span(file, index))));
                break;
            case DropTableStatement drop:
                ReadDrops(drop.Objects, RedefiningStatement.DropTable, drop, into.TableChanges.TryAdd);
                break;
            case CreateTypeUddtStatement alias when SyntaxReader.Type(alias.DataType) is { } baseType:
                ReadAliasType(alias, baseType);
                break;
            case CreateTypeUdtStatement clr:
                ReadClrType(clr);
                break;
            case CreateTypeTableStatement table:
                var tableType = SyntaxReader.TableType(SyntaxReader.Name(table.Name), table.Definition, SyntaxReader.Span(file, table));
                into.Define(into.UserTypes, tableType.Name, tableType, At(table));
                break;
            case CreateProcedureStatement or CreateOrAlterProcedureStatement:
                var procedure = ModuleReader.Procedure((ProcedureStatementBody)statement, file) with { AnsiNullsOff = ansiNullsOff };
                procedures.Create(procedure.Name, procedure, At(statement), statement is CreateOrAlterProcedureStatement);
                break;
            case AlterProcedureStatement alter:
                var altered = ModuleReader.Procedure(alter, file) with { AnsiNullsOff = ansiNullsOff };
                procedures.Alter(altered.Name, altered, At(alter), Changed(altered.Name, RedefiningStatement.AlterProcedure, alter));
                break;
            case CreateFunctionStatement or CreateOrAlterFunctionStatement:
                var function = ModuleReader.Function((FunctionStatementBody)statement, file) with { AnsiNullsOff = ansiNullsOff };
                functions.Create(function.Name, function, At(statement), statement is CreateOrAlterFunctionStatement);
                break;
            case AlterFunctionStatement alter:
                var alteredFunction = ModuleReader.Function(alter, file) with { AnsiNullsOff = ansiNullsOff };
                functions.Alter(alteredFunction.Name, alteredFunction, At(alter), Changed(alteredFunction.Name, RedefiningStatement.AlterFunction, alter));
                break;
            case DropProcedureStatement or DropFunctionStatement or DropViewStatement:
                ReadModuleDrop((DropObjectsStatement)statement, certain: true);
                break;
            case IfStatement { ElseStatement: null } guarded:
                ReadGuardedDrops(guarded);
                break;
            case ViewStatementBody view:
                ReadView(view);
                break;
            case CreateSynonymStatement synonym:
                var standsFor = ModuleReader.Synonym(synonym, file);
                into.Synonyms.TryAdd(standsFor.Name, standsFor);
                break;
            case TriggerStatementBody trigger when ModuleReader.Trigger(trigger, file) is { } dml:
                into.Triggers.Add(dml);
                break;
        }
    }

    /// <summary>
    /// A <c>DROP PROCEDURE</c>, <c>DROP FUNCTION</c> or <c>DROP VIEW</c> of one or more objects, with or without <c>IF EXISTS</c>:
    /// <paramref name="certain"/> when it runs whenever each exists.
    /// </summary>
    /// <param name="drop">The statement.</param>
    /// <param name="certain">Whether it runs whenever each object exists.</param>
    /// <param name="type">The type an <c>OBJECT_ID</c> guarding it names, which what stands must have for the drop to run; null for none.</param>
    private void ReadModuleDrop(DropObjectsStatement drop, bool certain, string? type = null)
    {
        bool Is(string code) => code.Equals(type?.Trim(), StringComparison.OrdinalIgnoreCase);
        foreach (var name in drop.Objects.Select(SyntaxReader.Name))
        {
            switch (drop)
            {
                case DropProcedureStatement:
                    procedures.Drop(name, Changed(name, RedefiningStatement.DropProcedure, drop), certain, type is null ? null : p => Is(p.Body is ClrBody ? "PC" : "P"));
                    break;
                case DropFunctionStatement:
                    functions.Drop(name, Changed(name, RedefiningStatement.DropFunction, drop), certain, type is null ? null : f => Is(TypeOf(f)));
                    break;
                case DropViewStatement:
                    views.Drop(name, Changed(name, RedefiningStatement.DropView, drop), certain, type is null ? null : _ => Is("V"));
                    break;
            }
        }
    }

    /// <summary>
    /// A function's type as <c>sys.objects</c> and <c>OBJECT_ID</c> name it: <c>FN</c> scalar, <c>IF</c> inline, <c>TF</c>
    /// multi-statement, and <c>FS</c> and <c>FT</c> for CLR scalar and table-valued.
    /// </summary>
    private static string TypeOf(Function function) => function switch
    {
        ScalarFunction { Body: ClrBody } => "FS",
        ScalarFunction => "FN",
        InlineFunction => "IF",
        TableFunction { Body: ClrBody } => "FT",
        TableFunction => "TF",
    };

    /// <summary>
    /// The drops of procedures, functions and views an <c>IF</c> with no <c>ELSE</c> makes, alone or in a block. Under
    /// <c>IF OBJECT_ID(N'name') IS NOT NULL</c> naming the object it drops, the drop runs whenever the object exists, as an
    /// unconditional one does (observed: A_procedure_dropped_and_created_again_runs_its_last_create); under any other condition
    /// it may not run. Nothing else under an <c>IF</c> is read, as anywhere.
    /// </summary>
    private void ReadGuardedDrops(IfStatement guarded)
    {
        IEnumerable<TSqlStatement> then = guarded.ThenStatement is BeginEndBlockStatement block ? block.StatementList.Statements : [guarded.ThenStatement];
        var exists = Existence(guarded.Predicate);
        foreach (var drop in then.OfType<DropObjectsStatement>().Where(d => d is DropProcedureStatement or DropFunctionStatement or DropViewStatement))
        {
            // An unqualified name means the caller's default schema in both, which needn't be dbo, so both must say the same.
            var certain = exists is { } tested && drop.Objects is [var target]
                && SyntaxReader.Name(target) == tested.Name && (target.SchemaIdentifier is null) == (tested.Schema is null);
            ReadModuleDrop(drop, certain, exists?.Type);
        }
    }

    /// <summary>
    /// The object an <c>OBJECT_ID(N'name'[, N'type']) IS NOT NULL</c> tests for, whether its name has a schema, and the type it
    /// names; null for any other condition, a name in another database, or a type that isn't a literal. With a type,
    /// <c>OBJECT_ID</c> finds only an object of that type (observed: Object_id_with_a_type_finds_only_an_object_of_that_type_so_a_drop_it_guards_may_not_run).
    /// </summary>
    private static (ObjectName Name, string? Schema, string? Type)? Existence(BooleanExpression predicate)
    {
        if (predicate is not BooleanIsNullExpression { IsNot: true, Expression: FunctionCall { CallTarget: null } call }
            || !call.FunctionName.Value.Equals("OBJECT_ID", StringComparison.OrdinalIgnoreCase)
            || call.Parameters is not [StringLiteral literal, ..] || call.Parameters.Count > 2
            || (call.Parameters.Count == 2 && call.Parameters[1] is not StringLiteral))
        {
            return null;
        }

        var type = call.Parameters is [_, StringLiteral named] ? named.Value : null;

        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).ParseSchemaObjectName(new StringReader(literal.Value), out var errors);
        return errors.Count == 0 && parsed is { DatabaseIdentifier: null, ServerIdentifier: null }
            ? (SyntaxReader.Name(parsed), parsed.SchemaIdentifier?.Value, type)
            : null;
    }

    /// <summary>A <c>DROP</c> of one or more objects: each dropped object's first change, if it has none yet.</summary>
    private void ReadDrops(IEnumerable<SchemaObjectName> dropped, RedefiningStatement statement, TSqlFragment at, Func<ObjectName, Redefinition, bool> record)
    {
        foreach (var name in dropped.Select(SyntaxReader.Name))
            record(name, Changed(name, statement, at));
    }

    /// <summary>
    /// A CLR type, recorded so that an unqualified type name that could mean it is not resolved to an alias type of the same
    /// name in another schema.
    /// </summary>
    private void ReadClrType(CreateTypeUdtStatement clr)
    {
        var name = SyntaxReader.Name(clr.Name);
        into.Define(into.UserTypes, name, new ClrType(name, SyntaxReader.Span(file, clr)), At(clr));
    }

    /// <summary>
    /// A <c>CREATE TABLE</c>. ScriptDom gives no definition when the statement lists no columns: an edge table may declare none
    /// of its own (observed: An_edge_table_may_declare_no_columns_and_its_star_is_its_pseudo_columns), and is read as a graph
    /// table with none listed, as its package reads it; a FileTable's columns are fixed by the server and one made from a query
    /// (<c>AS SELECT</c>) has its query's, neither of which is read here, so either is passed over with a note.
    /// </summary>
    private void ReadTable(CreateTableStatement statement)
    {
        var name = SyntaxReader.Name(statement.SchemaObjectName);
        if (statement.Definition is null && !statement.AsEdge)
        {
            var kind = statement.AsFileTable ? "a FileTable, whose columns the server fixes" : "made from a query, whose columns are the query's";
            into.Diagnostics.Add(SourceCodes.ColumnsNotListed.At(At(statement), $"{name} is {kind}; haruspex doesn't read them, so a name in it is unresolved."));
            return;
        }

        var read = SyntaxReader.Table(name, statement.Definition ?? new TableDefinition(), SyntaxReader.Span(file, statement), statement.AsNode || statement.AsEdge);

        // Its computed columns compare with NULL by the setting in force when it is created, as a module's body does.
        var table = read with { AnsiNullsOff = ansiNullsOff };
        into.Define(into.Tables, name, table, At(statement));
        ReadCascadingKeys(name, statement.Definition);
    }

    /// <summary>
    /// A definition of a view: the objects a FROM clause can read besides tables, which an unqualified name may resolve to
    /// instead of a table. It is played in the script's order, as a procedure's is.
    /// </summary>
    private void ReadView(ViewStatementBody statement)
    {
        var view = ModuleReader.View(statement, file) with { AnsiNullsOff = ansiNullsOff };
        var kind = statement switch
        {
            AlterViewStatement => RedefiningStatement.AlterView,
            CreateOrAlterViewStatement => RedefiningStatement.CreateOrAlterView,
            _ => RedefiningStatement.CreateView,
        };
        var definition = new ViewDefinition(view, kind);
        if (kind == RedefiningStatement.AlterView)
            views.Alter(view.Name, definition, At(statement), Changed(view.Name, kind, statement));
        else
            views.Create(view.Name, definition, At(statement), kind == RedefiningStatement.CreateOrAlterView);
    }

    private void ReadAliasType(CreateTypeUddtStatement alias, SqlType baseType)
    {
        var name = SyntaxReader.Name(alias.Name);
        var nullability = alias.NullableConstraint is null ? DeclaredNullability.NotDeclared
            : alias.NullableConstraint.Nullable ? DeclaredNullability.Null : DeclaredNullability.NotNull;
        into.Define(into.UserTypes, name, new AliasType(name, SyntaxReader.Span(file, alias), baseType, nullability), At(alias));
    }

    /// <summary>
    /// An <c>ALTER TABLE</c>: one that only tightens the table adds the foreign keys it declares, if any, and the primary key and
    /// unique constraints, when it names the table with its schema (<see cref="AddedKey"/>); any other changes the table in a way
    /// the model doesn't apply, and is recorded, with the foreign keys a column it adds declares.
    /// </summary>
    private void ReadAlterTable(AlterTableStatement statement)
    {
        var name = SyntaxReader.Name(statement.SchemaObjectName);
        if (!OnlyTightens(statement))
            into.TableChanges.TryAdd(name, Changed(name, RedefiningStatement.AlterTable, statement));
        if (statement is not AlterTableAddTableElementStatement add)
            return;
        ReadCascadingKeys(name, add.Definition);
        if (add.Definition.ColumnDefinitions.Count == 0 && NamesThisDatabasesTable(statement.SchemaObjectName))
        {
            var span = SyntaxReader.Span(file, statement);
            foreach (var key in add.Definition.TableConstraints.OfType<UniqueConstraintDefinition>())
                into.AddedKeys.Add(new AddedKey(name, SyntaxReader.Key(key, span)));
        }
    }

    /// <summary>
    /// Whether a key added to the table <paramref name="name"/> names a table of this database for certain: with its schema (without
    /// it, SQL Server looks in the caller's default schema first) and without a database or server, which would name another
    /// database's table of the same schema and name. Not a temporary table, which is no table of the database: a body's temporary
    /// tables have their keys in the walk (Inference's <c>KnownTable.Keys</c>).
    /// </summary>
    private static bool NamesThisDatabasesTable(SchemaObjectName name) =>
        name is { SchemaIdentifier: not null, DatabaseIdentifier: null, ServerIdentifier: null } && !name.BaseIdentifier.Value.StartsWith('#');

    /// <summary>
    /// ALTER TABLE forms that cannot make a column nullable, change its type, or add or remove one: adding a
    /// constraint (a primary key can only add NOT NULL), checking or disabling constraints, rebuilding, and
    /// switching triggers or change tracking. Ignoring them can only leave a column more nullable than it is.
    /// Disabling a CHECK constraint does matter to what values a column holds; that is recorded on the constraint
    /// (<see cref="ConstraintTrust"/>), not as a redefinition of the table. A primary key or unique constraint added this way
    /// is read (<see cref="AddedKey"/>); a CHECK is not, which only claims less.
    /// </summary>
    private static bool OnlyTightens(AlterTableStatement statement) => statement switch
    {
        AlterTableAddTableElementStatement add => add.Definition.ColumnDefinitions.Count == 0,
        AlterTableConstraintModificationStatement or AlterTableRebuildStatement or AlterTableTriggerModificationStatement
            or AlterTableChangeTrackingModificationStatement => true,
        _ => false,
    };

    /// <summary>
    /// The foreign keys of a table definition (of <c>CREATE TABLE</c> or <c>ALTER TABLE … ADD</c>) that have an <c>ON DELETE</c>
    /// or <c>ON UPDATE</c> action: a change to the referenced table's rows changes this table's, and fires its triggers.
    /// </summary>
    private void ReadCascadingKeys(ObjectName table, TableDefinition? definition)
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

            into.CascadingKeys.Add(new CascadingKey(table, SyntaxReader.Name(key.ReferenceTableName), At(key)));
        }
    }

    /// <summary>
    /// Every NOCHECK CONSTRAINT and WITH CHECK CHECK CONSTRAINT in the script, at any depth: a batch's statements, and those
    /// under IF, in blocks, TRY/CATCH and loops, and in procedure, trigger and function bodies. A plain CHECK CONSTRAINT enables
    /// without validating, leaving the rows written meanwhile (the constraint stays not trusted), so it restores nothing and
    /// isn't recorded. NOCHECK in dynamic SQL (EXEC of a string) is not seen.
    /// </summary>
    private void ReadEnforcement(TSqlScript script)
    {
        var topLevel = script.Batches.SelectMany(b => b.Statements).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var change in Everywhere<AlterTableConstraintModificationStatement>(script))
        {
            var validates = change.ConstraintEnforcement == ConstraintEnforcement.Check && change.ExistingRowsCheckEnforcement == ConstraintEnforcement.Check;
            if (change.ConstraintEnforcement != ConstraintEnforcement.NoCheck && !validates)
                continue;
            into.Enforcement.Add(new Enforcement(
                SyntaxReader.Name(change.SchemaObjectName),
                change.SchemaObjectName.SchemaIdentifier is not null,
                validates,
                change.All ? null : [.. change.ConstraintNames.Select(n => n.Value)],
                At(change),
                into.Enforcement.Count,
                topLevel.Contains(change)));
        }
    }

    /// <summary>
    /// Every statement in the script that may stop a key being enforced (<see cref="KeyDisable"/>), at any depth, as for
    /// NOCHECK: <c>ALTER INDEX … DISABLE</c>, <c>ALTER TABLE … DROP CONSTRAINT</c>, <c>DROP INDEX</c> and <c>CREATE INDEX … WITH
    /// (DROP_EXISTING = ON)</c>, since whether a
    /// statement in a body or under IF runs is not knowable. A disabled key enforces nothing and a dropped one is gone, so rows
    /// written meanwhile may share a key. A top-level <c>DROP CONSTRAINT</c> also changes the table (<see cref="OnlyTightens"/>).
    /// </summary>
    private void ReadKeyDisables(TSqlScript script)
    {
        foreach (var alter in Everywhere<AlterIndexStatement>(script).Where(a => a.AlterIndexType == AlterIndexType.Disable))
            into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(alter.OnName), alter.OnName.SchemaIdentifier is not null, alter.All ? null : alter.Name?.Value, At(alter)));
        foreach (var drop in Everywhere<AlterTableDropTableElementStatement>(script))
        {
            // DROP name, without CONSTRAINT, drops a constraint too (the keyword is optional; observed), and ScriptDom gives it no
            // type. A column, index or period is named as one.
            foreach (var element in drop.AlterTableDropTableElements.Where(e => e.TableElementType is TableElementType.Constraint or TableElementType.NotSpecified))
                into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(drop.SchemaObjectName), drop.SchemaObjectName.SchemaIdentifier is not null, element.Name.Value, At(drop)));
        }

        // An index created WITH (DROP_EXISTING = ON) replaces the one of its name, unique or not, so the key that was is gone.
        foreach (var replace in Everywhere<CreateIndexStatement>(script).Where(SyntaxReader.DropsExisting))
            into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(replace.OnName), replace.OnName.SchemaIdentifier is not null, replace.Name.Value, At(replace)));

        foreach (var drop in Everywhere<DropIndexStatement>(script))
        {
            foreach (var (table, index) in SyntaxReader.DroppedIndexes(drop))
                into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(table), table.SchemaIdentifier is not null, index, At(drop)));
        }
    }

    /// <summary>Every fragment of a kind in the script, at any depth, in the order they are written.</summary>
    private static List<T> Everywhere<T>(TSqlScript script)
        where T : TSqlFragment
    {
        var found = new Found<T>();
        script.Accept(found);
        return found.Fragments;
    }

    private Redefinition Changed(ObjectName name, RedefiningStatement statement, TSqlFragment at) => new(name, statement, At(at));

    /// <summary>Where a fragment of this script starts.</summary>
    private SourceLocation At(TSqlFragment fragment) => SyntaxReader.Span(file, fragment).Start;

    /// <summary>Collects the fragments of one kind a walk of the tree meets.</summary>
    private sealed class Found<T> : TSqlFragmentVisitor
        where T : TSqlFragment
    {
        public List<T> Fragments { get; } = [];

        public override void Visit(TSqlFragment fragment)
        {
            if (fragment is T wanted)
                Fragments.Add(wanted);
        }
    }
}

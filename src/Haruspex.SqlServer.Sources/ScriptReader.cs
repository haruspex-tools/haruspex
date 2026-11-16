using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads one parsed script into what the folder's scripts declare: each definition it makes, each statement that changes
/// one later, and each statement that may disable a constraint. It applies nothing; the passes after every script is read
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
        ReadIndexDisables(script);
        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            Read(statement);
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
            case DropTableStatement drop:
                ReadDrops(drop.Objects, RedefiningStatement.DropTable, drop, into.TableChanges.TryAdd);
                break;
            case CreateTypeUddtStatement alias when SyntaxReader.Type(alias.DataType) is { } baseType:
                ReadAliasType(alias, baseType);
                break;
            case CreateTypeUdtStatement clr:
                ReadClrType(clr);
                break;
            case CreateProcedureStatement or CreateOrAlterProcedureStatement:
                var procedure = ModuleReader.Procedure((ProcedureStatementBody)statement, file) with { AnsiNullsOff = ansiNullsOff };
                into.Define(into.Procedures, procedure.Name, procedure, At(statement));
                break;
            case AlterProcedureStatement alter:
                var altered = ModuleReader.Procedure(alter, file) with { AnsiNullsOff = ansiNullsOff };
                into.ProcedureChanges.TryAdd(altered.Name, new(Changed(altered.Name, RedefiningStatement.AlterProcedure, alter), altered));
                break;
            case DropProcedureStatement drop:
                ReadDrops(drop.Objects, RedefiningStatement.DropProcedure, drop, (name, why) => into.ProcedureChanges.TryAdd(name, new(why, null)));
                break;
            case CreateFunctionStatement or CreateOrAlterFunctionStatement:
                var function = ModuleReader.Function((FunctionStatementBody)statement, file) with { AnsiNullsOff = ansiNullsOff };
                into.Define(into.Functions, function.Name, function, At(statement));
                break;
            case AlterFunctionStatement alter:
                var alteredFunction = ModuleReader.Function(alter, file) with { AnsiNullsOff = ansiNullsOff };
                into.FunctionChanges.TryAdd(alteredFunction.Name, new(Changed(alteredFunction.Name, RedefiningStatement.AlterFunction, alter), alteredFunction));
                break;
            case DropFunctionStatement drop:
                ReadDrops(drop.Objects, RedefiningStatement.DropFunction, drop, (name, why) => into.FunctionChanges.TryAdd(name, new(why, null)));
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

        var table = SyntaxReader.Table(name, statement.Definition ?? new TableDefinition(), SyntaxReader.Span(file, statement), statement.AsNode || statement.AsEdge);
        into.Define(into.Tables, name, table, At(statement));
        ReadCascadingKeys(name, statement.Definition);
    }

    /// <summary>
    /// Every definition of a view, kept in the order read: the objects a FROM clause can read besides tables, which an
    /// unqualified name may resolve to instead of a table.
    /// </summary>
    private void ReadView(ViewStatementBody statement)
    {
        var view = ModuleReader.View(statement, file) with { AnsiNullsOff = ansiNullsOff };
        if (!into.Views.TryGetValue(view.Name, out var definitions))
            into.Views[view.Name] = definitions = [];
        definitions.Add(new ViewDefinition(view, statement switch
        {
            AlterViewStatement => RedefiningStatement.AlterView,
            CreateOrAlterViewStatement => RedefiningStatement.CreateOrAlterView,
            _ => RedefiningStatement.CreateView,
        }));
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
        if (add.Definition.ColumnDefinitions.Count == 0 && statement.SchemaObjectName.SchemaIdentifier is not null)
        {
            var span = SyntaxReader.Span(file, statement);
            foreach (var key in add.Definition.TableConstraints.OfType<UniqueConstraintDefinition>())
                into.AddedKeys.Add(new AddedKey(name, SyntaxReader.Key(key, span)));
        }
    }

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
    /// Every <c>ALTER INDEX … DISABLE</c> in the script, at any depth, as for NOCHECK: whether a statement in a body or under IF
    /// runs is not knowable. A disabled unique index enforces nothing, so rows written meanwhile may share a key.
    /// </summary>
    private void ReadIndexDisables(TSqlScript script)
    {
        foreach (var alter in Everywhere<AlterIndexStatement>(script).Where(a => a.AlterIndexType == AlterIndexType.Disable))
            into.IndexDisables.Add(new IndexDisable(SyntaxReader.Name(alter.OnName), alter.OnName.SchemaIdentifier is not null, alter.All ? null : alter.Name?.Value, At(alter)));
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

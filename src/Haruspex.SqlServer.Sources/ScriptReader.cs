using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads one parsed script into what the folder's scripts declare: each definition it makes, each statement that changes
/// one later, and each statement that may disable or drop a constraint. It applies only its own order, to the procedures,
/// functions, views and synonyms it names (<see cref="ScriptDefinitions{T}"/>); the passes after every script is read
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

    /// <summary>The synonyms this script creates or drops, played in its order; there is no <c>ALTER SYNONYM</c>.</summary>
    private readonly ScriptDefinitions<Synonym> synonyms = new(into.Diagnostics);

    /// <summary>A script that starts with the default settings, as a folder's does.</summary>
    public ScriptReader(string file, SchemaDeclarations into)
        : this(file, into, ScriptSettings.Default)
    {
    }

    /// <summary>
    /// Reads the script's statements, in order, and every statement at any depth that weakens a claim
    /// (<see cref="WeakeningStatements"/>).
    /// </summary>
    public void Read(TSqlScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        WeakeningStatements.Read(script, file, into, deployment: false);
        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            Read(statement);

        // The script has run: what it leaves of each procedure, function and view is settled against the other scripts' later.
        into.Settle(procedures.Outcomes, into.Procedures, into.ProcedureChanges);
        into.Settle(functions.Outcomes, into.Functions, into.FunctionChanges);
        into.Settle(views.Outcomes);
        SettleSynonyms();
    }

    /// <summary>
    /// What the script leaves of each synonym it names. One it creates is defined, and a second from another script is reported,
    /// as which of the two runs last a folder doesn't say. One it creates again after a drop that may not run is reported too: a
    /// synonym has no record of a doubt (<see cref="Redefinition"/> is a module's or a table's), so the read stops rather than pick
    /// one of the two targets. A drop of one another script creates is passed over: whichever runs first, a reference through the
    /// synonym reads that script's target or fails because there is none, and never reads another.
    /// </summary>
    private void SettleSynonyms()
    {
        foreach (var outcome in synonyms.Outcomes)
        {
            if (outcome is not { Standing: { } made, Established: true, At: { } at })
                continue;
            into.Define(into.Synonyms, outcome.Name, made, at);
            if (outcome.Why is { } doubt)
                into.Diagnostics.Add(new Diagnostic(SourceCodes.DefinedTwice, $"{outcome.Name} is created again after a drop that may not run, so which definition stands isn't known.", doubt.At));
        }
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
            case ExecuteStatement execute when ExtendedPropertyCalls.Read(execute, file) is { } property:
                into.ExtendedProperties.Add(property);
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
            case CreateFullTextIndexStatement { KeyIndexName: { } key } fullText when NamesThisDatabasesTable(fullText.OnName):
                into.FullTextKeys.Add((SyntaxReader.Name(fullText.OnName), new FullTextKey(key.Value, At(fullText))));
                break;
            case CreateTypeUddtStatement alias when SyntaxReader.Type(alias.DataType) is { } baseType:
                ReadAliasType(alias, baseType);
                break;
            case CreateTypeUdtStatement clr:
                ReadClrType(clr);
                break;
            case CreateXmlSchemaCollectionStatement collection:
                var collectionName = SyntaxReader.Name(collection.Name);
                into.Define(
                    into.XmlSchemaCollections,
                    collectionName,
                    new XmlSchemaCollection(collectionName, SyntaxReader.Span(file, collection), collection.Expression is StringLiteral { Value: var schema } ? [schema] : []),
                    At(collection));
                break;
            case CreateSequenceStatement sequence:
                var sequenceName = SyntaxReader.Name(sequence.Name);
                var declared = sequence.SequenceOptions.OfType<DataTypeSequenceOption>().FirstOrDefault()?.DataType;
                into.Define(
                    into.Sequences,
                    sequenceName,
                    new Sequence(sequenceName, SyntaxReader.Span(file, sequence), declared is null ? Sequence.Undeclared : SyntaxReader.Type(declared)),
                    At(sequence));
                break;
            case AlterXmlSchemaCollectionStatement addition:
                into.XmlSchemaAdditions.Add((SyntaxReader.Name(addition.Name), addition.Expression is StringLiteral { Value: var added } ? added : null));
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
            case DropProcedureStatement or DropFunctionStatement or DropViewStatement or DropSynonymStatement:
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
                synonyms.Create(standsFor.Name, standsFor, At(synonym), orAlter: false);
                break;
            case TriggerStatementBody trigger when ModuleReader.Trigger(trigger, file) is { } dml:
                into.Triggers.Add(dml);
                break;
        }
    }

    /// <summary>
    /// A <c>DROP PROCEDURE</c>, <c>DROP FUNCTION</c>, <c>DROP VIEW</c> or <c>DROP SYNONYM</c> of one or more objects, with or without <c>IF EXISTS</c>:
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
                case DropSynonymStatement:
                    synonyms.Drop(name, Changed(name, RedefiningStatement.DropSynonym, drop), certain, type is null ? null : _ => Is("SN"));
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
    /// The drops of procedures, functions, views and synonyms an <c>IF</c> with no <c>ELSE</c> makes, alone or in a block. Under
    /// <c>IF OBJECT_ID(N'name') IS NOT NULL</c> naming the object it drops, the drop runs whenever the object exists, as an
    /// unconditional one does (observed: A_procedure_dropped_and_created_again_runs_its_last_create); under any other condition
    /// it may not run. What else under an <c>IF</c> weakens a claim is <see cref="WeakeningStatements"/>'s.
    /// </summary>
    private void ReadGuardedDrops(IfStatement guarded)
    {
        IEnumerable<TSqlStatement> then = guarded.ThenStatement is BeginEndBlockStatement block ? block.StatementList.Statements : [guarded.ThenStatement];
        var exists = Existence(guarded.Predicate);
        foreach (var drop in then.OfType<DropObjectsStatement>().Where(d => d is DropProcedureStatement or DropFunctionStatement or DropViewStatement or DropSynonymStatement))
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
    /// An <c>ALTER TABLE</c> of a batch: one that only tightens the table adds the foreign keys it declares, if any, and the primary
    /// key and unique constraints, when it names the table with its schema (<see cref="AddedKey"/>); a column it adds may declare
    /// foreign keys too. Any that does more than tighten is recorded as a change by <see cref="WeakeningStatements"/>, wherever it is.
    /// </summary>
    private void ReadAlterTable(AlterTableStatement statement)
    {
        var name = SyntaxReader.Name(statement.SchemaObjectName);
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

    private Redefinition Changed(ObjectName name, RedefiningStatement statement, TSqlFragment at) => new(name, statement, At(at));

    /// <summary>Where a fragment of this script starts.</summary>
    private SourceLocation At(TSqlFragment fragment) => SyntaxReader.Span(file, fragment).Start;
}

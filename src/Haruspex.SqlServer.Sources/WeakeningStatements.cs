using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// One walk over a parsed script, at any depth, for every statement that weakens what the model may claim of a table: one that
/// may disable a CHECK constraint or a key, and one that may change, drop or create a table again. It is run on every script a
/// source holds, wherever haruspex reads one: a folder's or a project's scripts, each module's text in a DACPAC, and the pre- and
/// post-deployment scripts of a project or a package. Each records only what it finds; the passes after reading
/// (<see cref="ConstraintTrust"/>, <see cref="DefinitionsInEffect"/>) settle what the model says.
/// </summary>
/// <remarks>
/// <para>
/// <b>Any depth.</b> A statement under <c>IF</c>, in a block, in <c>TRY … CATCH</c>, in a loop, or in a procedure, function or
/// trigger body may run, and whether it does isn't knowable, so it counts as one that runs. The guarded migration
/// (<c>IF COL_LENGTH(…) IS NOT NULL ALTER TABLE …</c>) and the load procedure that turns checks off are where a project meets
/// these, so a walk that stopped at a batch's own statements would claim what the engine doesn't keep.
/// </para>
/// <para>
/// <b>What changes a table.</b> An <c>ALTER TABLE</c> that can do more than tighten it (<see cref="OnlyTightens"/>), a
/// <c>DROP TABLE</c>, and a <c>CREATE TABLE</c> that isn't the table's definition: one nested in another statement, which is a
/// second definition that may run in place of the first, or any in a deployment script, which defines nothing the model takes. A
/// temporary table (<c>#t</c>) is no table of the database. A foreign key or a key that a nested statement adds is not read: it
/// only tightens, so leaving it out claims less.
/// </para>
/// <para>
/// <b>A synonym created again.</b> A <c>CREATE SYNONYM</c> under <c>IF</c>, in a block or in a body, or in a deployment script,
/// moves the synonym when it runs (observed: A_synonym_created_in_a_body_stands_for_the_body_s_target), which the model's
/// definition doesn't follow (<see cref="SchemaDeclarations.ConditionalSynonyms"/>).
/// </para>
/// <para>
/// <b>What disables a check.</b> <c>ALTER TABLE … NOCHECK CONSTRAINT</c>, and a bulk load that doesn't check them: <c>BULK
/// INSERT</c> and <c>INSERT BULK</c> without <c>CHECK_CONSTRAINTS</c>, and <c>INSERT … WITH (IGNORE_CONSTRAINTS)</c>, which only
/// an <c>OPENROWSET(BULK …)</c> source allows. Each writes rows no check has seen and leaves every check of the table not trusted
/// (observed: A_bulk_load_without_check_constraints_writes_rows_no_check_has_seen), as <c>NOCHECK CONSTRAINT ALL</c> does.
/// </para>
/// </remarks>
internal static class WeakeningStatements
{
    /// <summary>Records each statement of <paramref name="script"/> that weakens a claim, wherever it is.</summary>
    /// <param name="script">The script, or a module's text.</param>
    /// <param name="file">Its file, which every location names.</param>
    /// <param name="into">What the source declares, which this adds to.</param>
    /// <param name="deployment">
    /// Whether it is a deployment script, whose own <c>CREATE TABLE</c> statements define nothing the model takes; in any other
    /// script a batch's <c>CREATE TABLE</c> is the table's definition, which <see cref="ScriptReader"/> reads.
    /// </param>
    public static void Read(TSqlScript script, string file, SchemaDeclarations into, bool deployment)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(into);
        var topLevel = script.Batches.SelectMany(b => b.Statements).ToHashSet(ReferenceEqualityComparer.Instance);
        var found = new Found();
        script.Accept(found);
        SourceLocation At(TSqlFragment fragment) => SyntaxReader.Span(file, fragment).Start;

        // The tables this script defines, each with where its CREATE TABLE ends; and the module bodies, which may run at any time.
        var defined = topLevel.OfType<CreateTableStatement>().Where(_ => !deployment)
            .GroupBy(c => SyntaxReader.Name(c.SchemaObjectName))
            .ToDictionary(g => g.Key, g => g.Max(c => c.StartOffset));
        var bodies = topLevel.OfType<TSqlStatement>().Where(s => s is ProcedureStatementBody or FunctionStatementBody or TriggerStatementBody).ToList();

        foreach (var statement in found.Statements)
        {
            switch (statement)
            {
                case AlterTableConstraintModificationStatement change:
                    ReadEnforcement(change, At(change), topLevel.Contains(change), into);
                    break;
                case BulkInsertBase bulk when !bulk.Options.Any(o => o.OptionKind == BulkInsertOptionKind.CheckConstraints):
                    Unchecked(bulk.To, At(bulk), topLevel.Contains(bulk), into);
                    break;
                case InsertStatement { InsertSpecification.Target: NamedTableReference target } insert
                    when target.TableHints.Any(h => h.HintKind == TableHintKind.IgnoreConstraints):
                    Unchecked(target.SchemaObject, At(insert), topLevel.Contains(insert), into);
                    break;
            }

            // A synonym created where it may run, though not as a batch's own statement, may stand for another target.
            if (statement is CreateSynonymStatement synonym && (deployment || !topLevel.Contains(synonym)))
                into.ConditionalSynonyms.Add(ModuleReader.Synonym(synonym, file));

            ReadKeyDisables(statement, At(statement), into);
            if (!DroppedBeforeItsCreate(statement, defined, bodies))
                ReadTableChange(statement, At(statement), deployment || !topLevel.Contains(statement), into);
            if (deployment)
                ReadDeployedModule(statement, file, At(statement), into);
        }
    }

    /// <summary>
    /// A deployment script's statement about a procedure, function or view: whatever it does (create, alter or drop), what the
    /// deployment leaves is not the definition the model has, which is recorded as its change (<see cref="SchemaDeclarations.Deployed"/>).
    /// A DML trigger it creates or alters is one more definition of it, as every definition a folder holds is.
    /// </summary>
    private static void ReadDeployedModule(TSqlStatement statement, string file, SourceLocation at, SchemaDeclarations into)
    {
        switch (statement)
        {
            case ProcedureStatementBody procedure:
                Deployed(procedure.ProcedureReference.Name, procedure switch
                {
                    AlterProcedureStatement => RedefiningStatement.AlterProcedure,
                    CreateOrAlterProcedureStatement => RedefiningStatement.CreateOrAlterProcedure,
                    _ => RedefiningStatement.CreateProcedure,
                });
                break;
            case FunctionStatementBody function:
                Deployed(function.Name, function switch
                {
                    AlterFunctionStatement => RedefiningStatement.AlterFunction,
                    CreateOrAlterFunctionStatement => RedefiningStatement.CreateOrAlterFunction,
                    _ => RedefiningStatement.CreateFunction,
                });
                break;
            case ViewStatementBody view:
                Deployed(view.SchemaObjectName, view switch
                {
                    AlterViewStatement => RedefiningStatement.AlterView,
                    CreateOrAlterViewStatement => RedefiningStatement.CreateOrAlterView,
                    _ => RedefiningStatement.CreateView,
                });
                break;
            case DropProcedureStatement or DropFunctionStatement or DropViewStatement:
                var kind = statement switch
                {
                    DropProcedureStatement => RedefiningStatement.DropProcedure,
                    DropFunctionStatement => RedefiningStatement.DropFunction,
                    _ => RedefiningStatement.DropView,
                };
                foreach (var dropped in ((DropObjectsStatement)statement).Objects)
                    Deployed(dropped, kind);
                break;
            case TriggerStatementBody trigger when ModuleReader.Trigger(trigger, file) is { } dml:
                into.Triggers.Add(dml);
                break;
        }

        void Deployed(SchemaObjectName module, RedefiningStatement kind)
        {
            var name = SyntaxReader.Name(module);
            into.Deployed.TryAdd(name, new Redefinition(name, kind, at));
        }
    }

    /// <summary>
    /// <c>ALTER TABLE</c> forms that cannot make a column nullable, change its type, or add or remove one: adding a
    /// constraint (a primary key can only add NOT NULL), checking or disabling constraints, rebuilding, switching triggers or
    /// change tracking, and turning system versioning on or off, which leaves every column as it was, hidden ones hidden
    /// (observed: Turning_system_versioning_off_changes_no_column). Ignoring them can only leave a column more nullable than it is.
    /// Disabling a CHECK constraint does matter to what values a column holds; that is recorded on the constraint
    /// (<see cref="ConstraintTrust"/>), not as a redefinition of the table. A primary key or unique constraint added this way
    /// is read (<see cref="AddedKey"/>); a CHECK is not, which only claims less.
    /// </summary>
    public static bool OnlyTightens(AlterTableStatement statement) => statement switch
    {
        AlterTableAddTableElementStatement add => add.Definition.ColumnDefinitions.Count == 0,
        AlterTableConstraintModificationStatement or AlterTableRebuildStatement or AlterTableTriggerModificationStatement
            or AlterTableChangeTrackingModificationStatement => true,
        AlterTableSetStatement set => set.Options.All(o => o is SystemVersioningTableOption),
        _ => false,
    };

    /// <summary>
    /// <c>ALTER TABLE … DROP PERIOD FOR SYSTEM_TIME</c>, and nothing else: it keeps each column's type and nullability, and makes a
    /// hidden period column visible (observed: Dropping_a_period_shows_its_hidden_columns_and_keeps_their_types), so it changes
    /// what <c>SELECT *</c> gives only of a table with a hidden column, which the pass after reading decides
    /// (<see cref="SchemaDeclarations.PeriodDrops"/>).
    /// </summary>
    private static bool DropsPeriodOnly(AlterTableStatement statement) =>
        statement is AlterTableDropTableElementStatement drop && drop.AlterTableDropTableElements.All(e => e.TableElementType == TableElementType.Period);

    /// <summary>
    /// A NOCHECK CONSTRAINT or WITH CHECK CHECK CONSTRAINT. A plain CHECK CONSTRAINT enables without validating, leaving the rows
    /// written meanwhile (the constraint stays not trusted), so it restores nothing and isn't recorded. NOCHECK in dynamic SQL
    /// (EXEC of a string) is not seen.
    /// </summary>
    private static void ReadEnforcement(AlterTableConstraintModificationStatement change, SourceLocation at, bool topLevel, SchemaDeclarations into)
    {
        var validates = change.ConstraintEnforcement == ConstraintEnforcement.Check && change.ExistingRowsCheckEnforcement == ConstraintEnforcement.Check;
        if (change.ConstraintEnforcement != ConstraintEnforcement.NoCheck && !validates)
            return;
        into.Enforcement.Add(new Enforcement(
            SyntaxReader.Name(change.SchemaObjectName),
            change.SchemaObjectName.SchemaIdentifier is not null,
            validates,
            change.All ? null : [.. change.ConstraintNames.Select(n => n.Value)],
            at,
            into.Enforcement.Count,
            topLevel));
    }

    /// <summary>A load that writes rows without checking them, which leaves every check of its table as NOCHECK CONSTRAINT ALL does.</summary>
    private static void Unchecked(SchemaObjectName table, SourceLocation at, bool topLevel, SchemaDeclarations into) =>
        into.Enforcement.Add(new Enforcement(SyntaxReader.Name(table), table.SchemaIdentifier is not null, false, null, at, into.Enforcement.Count, topLevel));

    /// <summary>
    /// A statement that may stop a key being enforced (<see cref="KeyDisable"/>): <c>ALTER INDEX … DISABLE</c>, <c>ALTER TABLE …
    /// DROP CONSTRAINT</c>, <c>DROP INDEX</c> and <c>CREATE INDEX … WITH (DROP_EXISTING = ON)</c>. A disabled key enforces nothing
    /// and a dropped one is gone, so rows written meanwhile may share a key.
    /// </summary>
    private static void ReadKeyDisables(TSqlStatement statement, SourceLocation at, SchemaDeclarations into)
    {
        switch (statement)
        {
            case AlterIndexStatement { AlterIndexType: AlterIndexType.Disable } alter:
                into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(alter.OnName), alter.OnName.SchemaIdentifier is not null, alter.All ? null : alter.Name?.Value, at));
                break;
            case AlterTableDropTableElementStatement drop:
                // DROP name, without CONSTRAINT, drops a constraint too (the keyword is optional; observed), and ScriptDom gives it no
                // type. A column, index or period is named as one.
                foreach (var element in drop.AlterTableDropTableElements.Where(e => e.TableElementType is TableElementType.Constraint or TableElementType.NotSpecified))
                    into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(drop.SchemaObjectName), drop.SchemaObjectName.SchemaIdentifier is not null, element.Name.Value, at));
                break;
            case CreateIndexStatement replace when SyntaxReader.DropsExisting(replace):
                // An index created WITH (DROP_EXISTING = ON) replaces the one of its name, unique or not, so the key that was is gone.
                into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(replace.OnName), replace.OnName.SchemaIdentifier is not null, replace.Name.Value, at));
                break;
            case DropIndexStatement dropIndex:
                foreach (var (table, index) in SyntaxReader.DroppedIndexes(dropIndex))
                    into.KeyDisables.Add(new KeyDisable(SyntaxReader.Name(table), table.SchemaIdentifier is not null, index, at));
                break;
        }
    }

    /// <summary>
    /// A statement that changes a table in a way the model doesn't apply, recorded as the table's first change. A
    /// <c>CREATE TABLE</c> counts only when it is not the table's definition (<paramref name="notADefinition"/>).
    /// </summary>
    private static void ReadTableChange(TSqlStatement statement, SourceLocation at, bool notADefinition, SchemaDeclarations into)
    {
        switch (statement)
        {
            case AlterTableStatement alter when DropsPeriodOnly(alter) && !IsTemporary(alter.SchemaObjectName):
                var period = SyntaxReader.Name(alter.SchemaObjectName);
                into.PeriodDrops.Add(new Redefinition(period, RedefiningStatement.AlterTable, at));
                break;
            case AlterTableStatement alter when !OnlyTightens(alter) && !IsTemporary(alter.SchemaObjectName):
                Change(alter.SchemaObjectName, RedefiningStatement.AlterTable);
                break;
            case DropTableStatement drop:
                foreach (var table in drop.Objects.Where(o => !IsTemporary(o)))
                    Change(table, RedefiningStatement.DropTable);
                break;
            case CreateTableStatement create when notADefinition && !IsTemporary(create.SchemaObjectName):
                Change(create.SchemaObjectName, RedefiningStatement.CreateTable);
                break;
        }

        void Change(SchemaObjectName table, RedefiningStatement kind)
        {
            var name = SyntaxReader.Name(table);
            into.TableChanges.TryAdd(name, new Redefinition(name, kind, at));
        }
    }

    /// <summary>
    /// Whether <paramref name="statement"/> is a <c>DROP TABLE</c> of tables this script creates after it, outside any module body:
    /// the drop-and-create idiom of an install script (<c>IF OBJECT_ID(…) IS NOT NULL DROP TABLE …</c> then <c>CREATE TABLE</c>).
    /// The script runs in its order, so the <c>CREATE TABLE</c> after the drop is the table that stands, as the last definition
    /// of a procedure in its script is (decision 0080); one in a body may run after it, and stays a change.
    /// </summary>
    private static bool DroppedBeforeItsCreate(TSqlStatement statement, Dictionary<ObjectName, int> defined, List<TSqlStatement> bodies) =>
        statement is DropTableStatement drop
        && !bodies.Any(b => drop.StartOffset >= b.StartOffset && drop.StartOffset < b.StartOffset + b.FragmentLength)
        && drop.Objects.All(o => defined.TryGetValue(SyntaxReader.Name(o), out var created) && created > drop.StartOffset);

    private static bool IsTemporary(SchemaObjectName name) => name.BaseIdentifier.Value.StartsWith('#');

    /// <summary>Collects every statement a walk of the tree meets, in the order they are written.</summary>
    private sealed class Found : TSqlFragmentVisitor
    {
        public List<TSqlStatement> Statements { get; } = [];

        public override void Visit(TSqlStatement node) => Statements.Add(node);
    }
}

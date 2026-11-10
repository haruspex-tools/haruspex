namespace Haruspex.SqlServer.Model;

/// <summary>
/// A database's schema as data: what its source declares, and nothing about where that source came from or
/// what the references in its bodies point to.
/// </summary>
/// <remarks>
/// <para>Each name is unique within its collection; the constructor refuses duplicates. A reader that meets a
/// duplicate in its source reports it and keeps one.</para>
/// <para>Read-only, and only a description: nothing here builds, compares or deploys a database.</para>
/// </remarks>
public sealed class DatabaseModel
{
    private readonly Dictionary<ObjectName, Table> tables = [];
    private readonly Dictionary<ObjectName, Procedure> procedures = [];
    private readonly Dictionary<ObjectName, NamedObject> namedObjects = [];
    private readonly Dictionary<ObjectName, Redefinition> tableRedefinitions = [];
    private readonly Dictionary<ObjectName, UserType> userTypes = [];
    private readonly Dictionary<ObjectName, UserFunction> functions = [];
    private readonly ILookup<ObjectName, Trigger> triggersOn;
    private readonly ILookup<ObjectName, CascadingKey> keysTo;

    /// <summary>Creates a model.</summary>
    /// <param name="tables">The tables.</param>
    /// <param name="procedures">The procedures.</param>
    /// <param name="namedObjects">The views, synonyms and functions, known by name only.</param>
    /// <param name="tableRedefinitions">
    /// Statements that change a table after its definition in a way the model does not apply, at most one per
    /// table name. A name may have no table: a table dropped and never created is still worth knowing about.
    /// </param>
    /// <param name="userTypes">The alias and CLR types (<c>CREATE TYPE</c>).</param>
    /// <param name="functions">The user-defined functions with their definitions. Each is also among the named objects.</param>
    /// <param name="triggers">
    /// The DML triggers. A name may appear more than once: a source that creates and alters one has two definitions, and
    /// which applies depends on the order the scripts run, so a reader should take every one.
    /// </param>
    /// <param name="cascadingKeys">The foreign keys whose actions change their table's rows.</param>
    /// <exception cref="ArgumentException">A name appears twice in one collection.</exception>
    public DatabaseModel(
        IEnumerable<Table> tables,
        IEnumerable<Procedure> procedures,
        IEnumerable<NamedObject> namedObjects,
        IEnumerable<Redefinition> tableRedefinitions,
        IEnumerable<UserType>? userTypes = null,
        IEnumerable<UserFunction>? functions = null,
        IEnumerable<Trigger>? triggers = null,
        IEnumerable<CascadingKey>? cascadingKeys = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(namedObjects);
        ArgumentNullException.ThrowIfNull(tableRedefinitions);

        foreach (var table in tables)
            this.tables.Add(table.Name, table);
        foreach (var procedure in procedures)
            this.procedures.Add(procedure.Name, procedure);
        foreach (var named in namedObjects)
            this.namedObjects.Add(named.Name, named);
        foreach (var redefinition in tableRedefinitions)
            this.tableRedefinitions.Add(redefinition.Name, redefinition);

        foreach (var type in userTypes ?? [])
            this.userTypes.Add(type.Name, type);
        foreach (var function in functions ?? [])
            this.functions.Add(function.Name, function);

        Tables = [.. this.tables.Values];
        UserTypes = [.. this.userTypes.Values];
        Procedures = [.. this.procedures.Values.OrderBy(p => p.Name.ToString(), StringComparer.Ordinal)];
        NamedObjects = [.. this.namedObjects.Values];
        TableRedefinitions = [.. this.tableRedefinitions.Values];
        Functions = [.. this.functions.Values.OrderBy(f => f.Name.ToString(), StringComparer.Ordinal)];
        Triggers = [.. triggers ?? []];
        CascadingKeys = [.. cascadingKeys ?? []];
        triggersOn = Triggers.ToLookup(t => t.Parent);
        keysTo = CascadingKeys.ToLookup(k => k.Referenced);
    }

    /// <summary>The DML triggers, in the order given; a name may appear more than once (see the constructor).</summary>
    public IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>The DML triggers on this table or view, in the order given.</summary>
    public IEnumerable<Trigger> TriggersOn(ObjectName parent) => triggersOn[parent];

    /// <summary>The foreign keys whose actions change their table's rows, in the order given.</summary>
    public IReadOnlyList<CascadingKey> CascadingKeys { get; }

    /// <summary>The foreign keys with actions that reference this table: deleting or updating its rows changes theirs.</summary>
    public IEnumerable<CascadingKey> CascadingKeysTo(ObjectName referenced) => keysTo[referenced];

    /// <summary>The user-defined functions, in a stable order: by name, ordinally.</summary>
    public IReadOnlyList<UserFunction> Functions { get; }

    /// <summary>The function with this name, or null.</summary>
    public UserFunction? FindFunction(ObjectName name) => functions.GetValueOrDefault(name);

    /// <summary>The tables, in the order given.</summary>
    public IReadOnlyList<Table> Tables { get; }

    /// <summary>The procedures, in a stable order: by name, ordinally.</summary>
    public IReadOnlyList<Procedure> Procedures { get; }

    /// <summary>The views, synonyms and functions, known by name only, in the order given.</summary>
    public IReadOnlyList<NamedObject> NamedObjects { get; }

    /// <summary>The statements that change a table in a way the model does not apply, in the order given.</summary>
    public IReadOnlyList<Redefinition> TableRedefinitions { get; }

    /// <summary>The alias and CLR types, in the order given.</summary>
    public IReadOnlyList<UserType> UserTypes { get; }

    /// <summary>The alias or CLR type with this name, or null.</summary>
    public UserType? FindUserType(ObjectName name) => userTypes.GetValueOrDefault(name);

    /// <summary>The table with this name, or null.</summary>
    public Table? FindTable(ObjectName name) => tables.GetValueOrDefault(name);

    /// <summary>The procedure with this name, or null.</summary>
    public Procedure? FindProcedure(ObjectName name) => procedures.GetValueOrDefault(name);

    /// <summary>The view, synonym or function with this name, or null.</summary>
    public NamedObject? FindNamedObject(ObjectName name) => namedObjects.GetValueOrDefault(name);

    /// <summary>The statement that changes the table with this name after its definition, or null.</summary>
    public Redefinition? FindTableRedefinition(ObjectName name) => tableRedefinitions.GetValueOrDefault(name);
}

using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A database's schema as data: what its source declares, and nothing about where that source came from or
/// what the references in its bodies point to.
/// </summary>
/// <remarks>
/// <para>Each name is unique within its collection; the constructor refuses duplicates. A reader that meets a
/// duplicate in its source reports it and keeps one.</para>
/// <para>
/// <b>One order.</b> Every collection of schema objects is in source order: by file, in ordinal order of the files' relative
/// paths, then by where the object starts (<see cref="SourceSpan.InSourceOrder"/>). The constructor sorts them, so the order
/// is the model's, whatever order a reader adds them in, and the same folder gives the same model however its files are
/// listed. Order is never a name's order: a reader that lists names in a message, or wants them sorted, sorts them itself.
/// The parts of an object (columns, constraints, keys, parameters) are in the order they are declared, and the foreign keys
/// and table redefinitions are in the order their reader read them.
/// </para>
/// <para>Read-only, and only a description: nothing here builds, compares or deploys a database.</para>
/// </remarks>
public sealed class DatabaseModel
{
    private readonly Dictionary<ObjectName, Table> tables = [];
    private readonly Dictionary<ObjectName, UserType> userTypes = [];
    private readonly Dictionary<ObjectName, Procedure> procedures = [];
    private readonly Dictionary<ObjectName, Function> functions = [];
    private readonly Dictionary<ObjectName, View> views = [];
    private readonly Dictionary<ObjectName, Synonym> synonyms = [];
    private readonly Dictionary<ObjectName, SchemaObject> readable = [];
    private readonly Dictionary<ObjectName, Redefinition> tableRedefinitions = [];
    private readonly ILookup<ObjectName, Trigger> triggersOn;
    private readonly ILookup<ObjectName, CascadingKey> keysTo;

    /// <summary>Creates a model.</summary>
    /// <param name="tables">The tables.</param>
    /// <param name="userTypes">The alias, CLR and table types (<c>CREATE TYPE</c>).</param>
    /// <param name="procedures">The procedures.</param>
    /// <param name="functions">The user-defined functions.</param>
    /// <param name="views">The views.</param>
    /// <param name="synonyms">The synonyms.</param>
    /// <param name="triggers">
    /// The DML triggers. A name may appear more than once: a source that creates and alters one has two definitions, and
    /// which applies depends on the order the scripts run, so a reader should take every one.
    /// </param>
    /// <param name="cascadingKeys">The foreign keys whose actions change their table's rows, in the order they were read.</param>
    /// <param name="tableRedefinitions">
    /// Statements that change a table after its definition in a way the model does not apply, at most one per table name, in
    /// the order they were read. A name may have no table: a table dropped and never created is still worth knowing about.
    /// </param>
    /// <exception cref="ArgumentException">A name appears twice in one collection.</exception>
    public DatabaseModel(
        IEnumerable<Table> tables,
        IEnumerable<UserType> userTypes,
        IEnumerable<Procedure> procedures,
        IEnumerable<Function> functions,
        IEnumerable<View> views,
        IEnumerable<Synonym> synonyms,
        IEnumerable<Trigger> triggers,
        IEnumerable<CascadingKey> cascadingKeys,
        IEnumerable<Redefinition> tableRedefinitions)
    {
        Tables = Keyed(tables, this.tables);
        UserTypes = Keyed(userTypes, this.userTypes);
        Procedures = Keyed(procedures, this.procedures);
        Functions = Keyed(functions, this.functions);
        Views = Keyed(views, this.views);
        Synonyms = Keyed(synonyms, this.synonyms);
        Triggers = InSourceOrder(triggers ?? throw new ArgumentNullException(nameof(triggers)));
        CascadingKeys = [.. cascadingKeys ?? throw new ArgumentNullException(nameof(cascadingKeys))];
        foreach (var redefinition in tableRedefinitions ?? throw new ArgumentNullException(nameof(tableRedefinitions)))
            this.tableRedefinitions.Add(redefinition.Name, redefinition);
        TableRedefinitions = [.. this.tableRedefinitions.Values];

        // SQL Server gives a schema one namespace for its tables, views, synonyms, functions and procedures, so a script that
        // deploys has one object of a name. One that doesn't may have two; the first read is the one a name in FROM means.
        foreach (var named in InSourceOrder<SchemaObject>([.. Views, .. Synonyms, .. Functions]))
            readable.TryAdd(named.Name, named);

        triggersOn = Triggers.ToLookup(t => t.Parent);
        keysTo = CascadingKeys.ToLookup(k => k.Referenced);
    }

    /// <summary>The database's own settings, when the source records them (a DACPAC does); null when it doesn't (a folder of scripts).</summary>
    public DatabaseSettings? Settings { get; init; }

    /// <summary>
    /// The collation a column without <c>COLLATE</c> takes: the database's own, as its source records it (<see cref="Settings"/>),
    /// or else the one the config file declares (<see cref="WithDeclaredCollation"/>); null when neither says, and then nothing
    /// that depends on how a column compares strings is claimed for such a column.
    /// </summary>
    public string? DefaultCollation { get; init; }

    /// <summary>
    /// This model, with <paramref name="collation"/> as its <see cref="DefaultCollation"/> if its source records none: a folder of
    /// scripts doesn't say what database it deploys to, so the config file may. A source that records one keeps its own.
    /// </summary>
    public DatabaseModel WithDeclaredCollation(string? collation) => collation is null || DefaultCollation is not null
        ? this
        : new(Tables, UserTypes, Procedures, Functions, Views, Synonyms, Triggers, CascadingKeys, TableRedefinitions)
        {
            Settings = Settings,
            Unread = Unread,
            Databases = Databases,
            DefaultCollation = collation,
            ExtendedProperties = ExtendedProperties,
            Descriptions = Descriptions,
            XmlSchemaCollections = XmlSchemaCollections,
        };

    /// <summary>The XML schema collections, in source order: what a typed <c>xml</c> column, variable or parameter is bound to.</summary>
    public IReadOnlyList<XmlSchemaCollection> XmlSchemaCollections
    {
        get;
        init
        {
            var keyed = new Dictionary<ObjectName, XmlSchemaCollection>();
            field = Keyed(value ?? throw new ArgumentNullException(nameof(value)), keyed);
            xmlSchemaCollections = keyed;
        }
    } = [];

    private Dictionary<ObjectName, XmlSchemaCollection> xmlSchemaCollections = [];

    /// <summary>The XML schema collection with this name, or null.</summary>
    public XmlSchemaCollection? FindXmlSchemaCollection(ObjectName name) => xmlSchemaCollections.GetValueOrDefault(name);

    /// <summary>
    /// Every extended property the source sets, in the order read: what the descriptions are found in, once the config file
    /// has said which names are descriptions (<see cref="Descriptions"/>).
    /// </summary>
    public EquatableArray<ExtendedProperty> ExtendedProperties { get; init; } = [];

    /// <summary>
    /// The descriptions found in <see cref="ExtendedProperties"/>; none until the reader that knows the config file's names
    /// finds them (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
    /// </summary>
    public Descriptions Descriptions { get; init; } = Descriptions.None;

    /// <summary>
    /// The parts of the database the source refers to but which weren't read (a referenced package that wasn't found): a name
    /// that binds to nothing may be defined in one, so a reader saying it is missing can say where it may be. Empty when the
    /// source is whole as far as it knows.
    /// </summary>
    public EquatableArray<UnreadPart> Unread { get; init; } = [];

    /// <summary>
    /// The other databases the source refers to and holds the schema of, by the name a three-part name in a body writes the
    /// database by: a name (<c>master</c>) or a SQLCMD variable (<c>$(Ledger)</c>), compared ignoring case. Empty when there are
    /// none; a database a body names that isn't here is unknown.
    /// </summary>
    public IReadOnlyDictionary<string, DatabaseModel> Databases
    {
        get;
        init => field = new Dictionary<string, DatabaseModel>(value ?? throw new ArgumentNullException(nameof(value)), StringComparer.OrdinalIgnoreCase);
    } = new Dictionary<string, DatabaseModel>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tables, in source order.</summary>
    public IReadOnlyList<Table> Tables { get; }

    /// <summary>The alias, CLR and table types, in source order.</summary>
    public IReadOnlyList<UserType> UserTypes { get; }

    /// <summary>The procedures, in source order.</summary>
    public IReadOnlyList<Procedure> Procedures { get; }

    /// <summary>The user-defined functions, in source order.</summary>
    public IReadOnlyList<Function> Functions { get; }

    /// <summary>The views, in source order.</summary>
    public IReadOnlyList<View> Views { get; }

    /// <summary>The synonyms, in source order.</summary>
    public IReadOnlyList<Synonym> Synonyms { get; }

    /// <summary>The DML triggers, in source order; a name may appear more than once (see the constructor).</summary>
    public IReadOnlyList<Trigger> Triggers { get; }

    /// <summary>The foreign keys whose actions change their table's rows, in the order they were read.</summary>
    public IReadOnlyList<CascadingKey> CascadingKeys { get; }

    /// <summary>The statements that change a table in a way the model does not apply, in the order they were read.</summary>
    public IReadOnlyList<Redefinition> TableRedefinitions { get; }

    /// <summary>The table with this name, or null.</summary>
    public Table? FindTable(ObjectName name) => tables.GetValueOrDefault(name);

    /// <summary>The alias, CLR or table type with this name, or null.</summary>
    public UserType? FindUserType(ObjectName name) => userTypes.GetValueOrDefault(name);

    /// <summary>The procedure with this name, or null.</summary>
    public Procedure? FindProcedure(ObjectName name) => procedures.GetValueOrDefault(name);

    /// <summary>The function with this name, or null.</summary>
    public Function? FindFunction(ObjectName name) => functions.GetValueOrDefault(name);

    /// <summary>
    /// The view, synonym or function this name means in a FROM clause, besides a table: a <see cref="View"/>, a
    /// <see cref="Synonym"/> or a <see cref="Function"/>, or null when it is none of them. Of two with one name, which only a
    /// script that doesn't deploy can have, the first in source order.
    /// </summary>
    public SchemaObject? FindReadable(ObjectName name) => readable.GetValueOrDefault(name);

    /// <summary>
    /// Every view, synonym and function a name in a FROM clause may mean besides a table, one for each name (see
    /// <see cref="FindReadable"/>), in source order.
    /// </summary>
    public IEnumerable<SchemaObject> Readable => readable.Values;

    /// <summary>The statement that changes the table with this name after its definition, or null.</summary>
    public Redefinition? FindTableRedefinition(ObjectName name) => tableRedefinitions.GetValueOrDefault(name);

    /// <summary>The DML triggers on this table or view, in source order.</summary>
    public IEnumerable<Trigger> TriggersOn(ObjectName parent) => triggersOn[parent];

    /// <summary>The foreign keys with actions that reference this table: deleting or updating its rows changes theirs.</summary>
    public IEnumerable<CascadingKey> CascadingKeysTo(ObjectName referenced) => keysTo[referenced];

    private static T[] Keyed<T>(IEnumerable<T> objects, Dictionary<ObjectName, T> into)
        where T : SchemaObject
    {
        ArgumentNullException.ThrowIfNull(objects);
        var ordered = InSourceOrder(objects);
        foreach (var named in ordered)
            into.Add(named.Name, named);
        return ordered;
    }

    /// <summary>The objects in source order; a sort that keeps the order of two that start at the same place.</summary>
    private static T[] InSourceOrder<T>(IEnumerable<T> objects)
        where T : SchemaObject => [.. objects.OrderBy(o => o.Span, SourceSpan.InSourceOrder)];
}

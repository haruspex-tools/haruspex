namespace Haruspex.Ir;

// The IR v0 model. It is closed: every type node is one of the records below, so an emitter can handle
// all of it. See README.md in this folder for the format and the reasons behind it.

/// <summary>The inferred shapes of a set of database objects.</summary>
/// <param name="Source">Where the shapes were inferred from.</param>
/// <param name="Objects">The objects typed: procedures, in this version.</param>
/// <param name="Types">The named object types, referenced from <see cref="IrRef"/>.</param>
/// <param name="Unknowns">Everything that could not be typed, with the reason.</param>
public sealed record IrDocument(
    IrSource Source,
    IReadOnlyList<IrObject> Objects,
    IReadOnlyDictionary<string, IrObjectType> Types,
    IReadOnlyList<IrUnknown> Unknowns)
{
    /// <summary>
    /// The IR format version. It is versioned separately from the tool, because the IR is a file that other
    /// programs read: a change that would break a reader changes this.
    /// </summary>
    public const string CurrentVersion = "0.1";
}

/// <param name="Dialect">The SQL dialect of the source, such as <c>tsql</c>.</param>
public sealed record IrSource(string Dialect);

/// <summary>A typed database object.</summary>
/// <param name="Id">The object's name in the database, such as <c>sales.get_order</c>.</param>
/// <param name="Name">The name settled for generated code, such as <c>GetOrder</c>. Emitters use it as is.</param>
/// <param name="Kind">What kind of object it is.</param>
/// <param name="At">Where it is defined, as <c>path:line:column</c> relative to the source root.</param>
/// <param name="Parameters">The declared parameters, in order.</param>
/// <param name="Results">The result sets in the order the object returns them, or null when they cannot be listed.</param>
public sealed record IrObject(
    string Id,
    string Name,
    IrObjectKind Kind,
    string At,
    IReadOnlyList<IrParameter> Parameters,
    IReadOnlyList<IrResult>? Results);

public enum IrObjectKind
{
    Procedure,
}

/// <param name="Name">The parameter's name as declared, including any sigil such as <c>@</c>.</param>
/// <param name="Type">The declared type.</param>
/// <param name="HasDefault">Whether the caller may omit it.</param>
/// <param name="IsOutput">Whether it is an output parameter.</param>
public sealed record IrParameter(string Name, IrType Type, bool HasDefault, bool IsOutput);

/// <summary>One result set.</summary>
/// <param name="Encoding">How the result is delivered: as rows, or as JSON text to be parsed.</param>
/// <param name="Type">The shape of the result: for a rowset, an array of row objects.</param>
/// <param name="AbsentBecause">
/// Why the result can arrive with no value at all. A top-level <c>FOR JSON</c> query that matches no rows
/// returns no row, not an empty array or object. Readers follow one convention, stated in README.md: an
/// absent array result reads as <c>[]</c>, and any other absent result as <c>null</c>.
/// </param>
public sealed record IrResult(IrEncoding Encoding, IrType Type, IReadOnlyList<IrNullReason> AbsentBecause)
{
    public bool MayBeAbsent => AbsentBecause.Count > 0;
}

public enum IrEncoding
{
    /// <summary>Rows and columns, typed by the database driver.</summary>
    Rowset,

    /// <summary>A JSON document produced by the database, which the caller parses.</summary>
    Json,
}

/// <summary>A named object type.</summary>
public sealed record IrObjectType(IReadOnlyList<IrField> Fields);

/// <summary>A field of an object type.</summary>
/// <param name="Name">The key or column name.</param>
/// <param name="Type">The value's type when it is not null.</param>
/// <param name="Presence">Whether the key is always there, or left out when the value is null.</param>
/// <param name="NullableBecause">
/// Why the value can be null. Empty means it is proved never null; that is the only way to say non-null,
/// so a nullable flag can never disagree with its reasons.
/// </param>
public sealed record IrField(string Name, IrType Type, IrPresence Presence, IReadOnlyList<IrNullReason> NullableBecause)
{
    public bool Nullable => NullableBecause.Count > 0;
}

/// <summary>
/// Whether a key is present. Kept apart from nullability because each construct decides it separately:
/// <c>FOR JSON</c> leaves a null key out unless asked to keep it, while a rowset always has every column.
/// </summary>
public enum IrPresence
{
    Always,
    OmittedWhenNull,
}

/// <param name="Rule">One of <see cref="IrNullRules"/>.</param>
/// <param name="At">What the rule applies to: a column (<c>schema.table.column</c>) or a source location.</param>
public sealed record IrNullReason(string Rule, string At);

/// <summary>Something that could not be typed.</summary>
/// <param name="Object">The id of the object it belongs to.</param>
/// <param name="Path">Where in the object, such as <c>results[0].lines[].name</c>.</param>
/// <param name="Reason">One of <see cref="IrUnknownReasons"/>.</param>
/// <param name="Detail">A human-readable explanation.</param>
/// <param name="At">The source location of the construct, as <c>path:line:column</c>.</param>
public sealed record IrUnknown(string Object, string Path, string Reason, string Detail, string At);

/// <summary>A type node.</summary>
public abstract record IrType;

/// <summary>A scalar with its logical kind and the SQL type it came from.</summary>
/// <param name="Kind">The logical kind, which every emitter understands.</param>
/// <param name="Format">A refinement of the kind (see <see cref="IrFormats"/>), or null.</param>
/// <param name="Sql">The source's own type, as written, such as <c>decimal(19,2)</c>. Informational.</param>
public sealed record IrScalar(IrScalarKind Kind, string? Format, string Sql) : IrType;

/// <summary>An array of items.</summary>
public sealed record IrArray(IrType Items) : IrType;

/// <summary>A reference to a named object type in <see cref="IrDocument.Types"/>.</summary>
public sealed record IrRef(string Name) : IrType;

/// <summary>
/// A string that holds JSON. Distinct from embedded JSON: once JSON is stored or returned as a column, a
/// consumer receives text and must parse it.
/// </summary>
public sealed record IrJsonText : IrType;

/// <summary>A value whose type is not known; the document's unknowns say why.</summary>
/// <param name="Reason">One of <see cref="IrUnknownReasons"/>.</param>
public sealed record IrUnknownType(string Reason) : IrType;

/// <summary>
/// Logical scalar kinds. Dates, times and identifiers are kinds of their own, not strings, because how they
/// arrive differs by encoding (a JSON string in a known format, or a driver's native value) and an emitter
/// can only validate the format if it knows which it is.
/// </summary>
public enum IrScalarKind
{
    String,
    Number,
    Boolean,

    /// <summary>A calendar date, with no time and no time zone.</summary>
    Date,

    /// <summary>A time of day, with no date and no time zone.</summary>
    Time,

    /// <summary>A date and time; <see cref="IrFormats.Local"/> or <see cref="IrFormats.Offset"/> says whether it has an offset.</summary>
    Datetime,

    /// <summary>A 128-bit identifier written as 8-4-4-4-12 hex digits; not necessarily an RFC 9562 UUID.</summary>
    Uuid,
}

/// <summary>Format refinements of scalar kinds. Dialect-neutral: they describe values, not SQL types.</summary>
public static class IrFormats
{
    public const string Uint8 = "uint8";
    public const string Int16 = "int16";
    public const string Int32 = "int32";

    /// <summary>A 64-bit integer: more than a JavaScript number holds exactly.</summary>
    public const string Int64 = "int64";

    /// <summary>An exact decimal, such as <c>decimal(19,2)</c>.</summary>
    public const string Decimal = "decimal";

    /// <summary>A binary floating-point number.</summary>
    public const string Float = "float";

    /// <summary>A date and time without an offset from UTC.</summary>
    public const string Local = "local";

    /// <summary>A date and time with an offset from UTC.</summary>
    public const string Offset = "offset";
}

/// <summary>The reasons a value can be null.</summary>
public static class IrNullRules
{
    /// <summary>The column it reads is declared nullable (or not declared <c>NOT NULL</c>).</summary>
    public const string NullableColumn = "nullable-column";

    /// <summary>It comes from the optional side of an outer join, which has no row when nothing matches.</summary>
    public const string OuterJoin = "outer-join";

    /// <summary>It is a subquery, which is null when it returns no rows.</summary>
    public const string EmptySubquery = "empty-subquery";

    /// <summary>It is a nested object that is left out when every one of its fields is null.</summary>
    public const string AllFieldsNull = "all-fields-null";

    /// <summary>It is a query result, which returns nothing when no rows match.</summary>
    public const string NoRows = "no-rows";

    /// <summary>Its type is unknown, so nothing is proved about it.</summary>
    public const string Unknown = "unknown";
}

/// <summary>The reasons a value or a result cannot be typed.</summary>
public static class IrUnknownReasons
{
    /// <summary>A statement that is not modelled yet, which may return results of its own.</summary>
    public const string UnsupportedStatement = "unsupported-statement";

    /// <summary>Results produced by code outside the project, such as a CLR procedure.</summary>
    public const string ExternalCode = "external-code";

    /// <summary>
    /// An object defined more than once by statements whose order decides which applies, such as a CREATE
    /// and an ALTER PROCEDURE, or a definition followed by a DROP.
    /// </summary>
    public const string Redefined = "redefined";

    /// <summary>A query form that is not modelled yet, such as a CTE or <c>UNION</c>.</summary>
    public const string UnsupportedQuery = "unsupported-query";

    /// <summary>An expression or function that is not modelled yet.</summary>
    public const string UnsupportedExpression = "unsupported-expression";

    /// <summary>A table source or join that is not modelled yet.</summary>
    public const string UnsupportedSource = "unsupported-source";

    /// <summary>A SQL type that is not modelled yet.</summary>
    public const string UnsupportedSqlType = "unsupported-sql-type";

    /// <summary>A table that is not defined in the project.</summary>
    public const string UnresolvedTable = "unresolved-table";

    /// <summary>A column that cannot be found, or is ambiguous, in the tables in scope.</summary>
    public const string UnresolvedColumn = "unresolved-column";

    /// <summary>A result column without a name.</summary>
    public const string UnnamedColumn = "unnamed-column";

    /// <summary>Two result columns that claim the same key.</summary>
    public const string DuplicateColumn = "duplicate-column";

    /// <summary>JSON whose shape is not visible here, such as JSON text read from a column.</summary>
    public const string JsonShapeUnknown = "json-shape-unknown";
}

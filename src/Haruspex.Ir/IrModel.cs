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
    public const string CurrentVersion = "0.15";

    /// <summary>
    /// The claims made by the config file rather than inferred: a nullability, a type or a value set an override
    /// declared. Empty when nothing was overridden. Kept so that what the database proves and what a person asserts stay
    /// distinguishable in the file, though an emitter needs neither: the types already carry the result.
    /// </summary>
    public IReadOnlyList<IrOverride> Overrides { get; init; } = [];
}

/// <param name="Dialect">The SQL dialect of the source, such as <c>tsql</c>.</param>
public sealed record IrSource(string Dialect);

/// <summary>A typed database object.</summary>
/// <param name="Id">The object's name in the database, such as <c>sales.get_order</c>: its canonical identity.</param>
/// <param name="Name">
/// The name settled for generated code, such as <c>GetOrder</c>, from the config file's naming rules and overrides.
/// Emitters use it as is. A front end leaves the id here until the names are settled.
/// </param>
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

/// <summary>What kind of database object an <see cref="IrObject"/> is; each kind is called in its own way.</summary>
public enum IrObjectKind
{
    /// <summary>A stored procedure: called with parameters, it returns zero or more result sets.</summary>
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
    /// <summary>Whether the result can arrive with no value at all, which <see cref="AbsentBecause"/> explains.</summary>
    public bool MayBeAbsent => AbsentBecause.Count > 0;

    /// <summary>
    /// How many rows the result's query returns when it runs: a rowset's rows, or the objects of a FOR JSON result (the
    /// items of its array, or its one object without the array wrapper). Whether the result set may be missing is
    /// <see cref="AbsentBecause"/>, not this. <see cref="IrCardinality.Many"/> claims nothing.
    /// </summary>
    public IrCardinality Cardinality { get; init; } = IrCardinality.Many;
}

/// <summary>How many rows a query returns: what a generated client needs to return one row or a list.</summary>
public enum IrCardinality
{
    /// <summary>Any number, none included: nothing is proved.</summary>
    Many,

    /// <summary>None or one: <c>TOP (1)</c>, <c>FETCH NEXT 1</c>, or a filter that fixes every column of a key.</summary>
    AtMostOne,

    /// <summary>Exactly one: an aggregate query without <c>GROUP BY</c> or <c>HAVING</c>, or a query without <c>FROM</c> or a filter.</summary>
    One,
}

/// <summary>How a result reaches the caller, which decides how a reader must decode its values.</summary>
public enum IrEncoding
{
    /// <summary>Rows and columns, typed by the database driver.</summary>
    Rowset,

    /// <summary>A JSON document produced by the database, which the caller parses.</summary>
    Json,
}

/// <summary>A named object type.</summary>
/// <param name="Fields">Its fields, in order.</param>
public sealed record IrObjectType(IReadOnlyList<IrField> Fields)
{
    /// <summary>
    /// Where the type comes from in database terms, independent of its generated name: an object and the path to the
    /// type within it. Names are generated from config and can change; this cannot. Null only in a document built by
    /// hand (a test's).
    /// </summary>
    public IrTypeIdentity? Identity { get; init; }

    /// <summary>
    /// Whether the type refers to itself, directly or through other types (a tree a function builds by calling itself).
    /// A reader that expands references must stop at a recursive type, or it never ends. Set by whoever builds the
    /// document; a reader may also find cycles itself, by following references with the names it is expanding.
    /// </summary>
    public bool Recursive { get; init; }
}

/// <summary>Where an object type comes from: the database object it belongs to, and the path to it there.</summary>
/// <param name="Owner">What kind of object it belongs to.</param>
/// <param name="Object">The object's name in the database, such as <c>sales.get_order</c> or <c>bom.part_tree</c>.</param>
/// <param name="Path">
/// The path to the type in the IR's path syntax (<see cref="IrPath"/>): <c>results[0][]</c> for a procedure's rows,
/// <c>&lt;bom.part_tree&gt;</c> for a recursive function's node and <c>&lt;bom.part_tree&gt;.part</c> inside it. The
/// variants of a union share their path, as their unknowns do, so two types may have the same identity.
/// </param>
public sealed record IrTypeIdentity(IrTypeOwner Owner, string Object, string Path);

/// <summary>What kind of database object a type belongs to.</summary>
public enum IrTypeOwner
{
    /// <summary>A procedure in the document's objects: the type is part of its results.</summary>
    Procedure,

    /// <summary>
    /// A scalar function that calls itself: the type is its node, or inside it, and is shared by every object whose
    /// results reach it.
    /// </summary>
    Function,
}

/// <summary>A claim the config file made rather than inference: see <see cref="IrDocument.Overrides"/>.</summary>
/// <param name="Object">The id of the object it applies to.</param>
/// <param name="Path">Where in the object, in the IR's path syntax, as an unknown's path is written.</param>
/// <param name="Setting">One of <see cref="IrOverrideSettings"/>.</param>
/// <param name="Why">The reason given in the config file, or null when none was needed.</param>
public sealed record IrOverride(string Object, string Path, string Setting, string? Why)
{
    /// <summary>
    /// The procedure whose declaration the claim rests on, when it is not <see cref="Object"/> itself: a caller's results
    /// that include the declared results of a procedure it calls (<see cref="IrOverrideSettings.Results"/>). Null when the
    /// claim is about the object's own declaration.
    /// </summary>
    public string? Via { get; init; }
}

/// <summary>The settings an override can make.</summary>
public static class IrOverrideSettings
{
    /// <summary>The value is declared never null, which the database does not prove.</summary>
    public const string NotNull = "not-null";

    /// <summary>The value's type is declared to be a schema the application already has (<see cref="IrImport"/>).</summary>
    public const string Type = "type";

    /// <summary>A value set the database compares under its collation is declared to hold exactly its items.</summary>
    public const string ExactValues = "exact-values";

    /// <summary>
    /// The results are declared, not inferred: a procedure whose result sets cannot be known from the project (dynamic SQL,
    /// say) is declared to return these, or none. On a caller, with <see cref="IrOverride.Via"/>, its results include the
    /// declared results of a procedure it calls.
    /// </summary>
    public const string Results = "results";
}

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
    /// <summary>Whether the value can be null: true exactly when <see cref="NullableBecause"/> gives a reason.</summary>
    public bool Nullable => NullableBecause.Count > 0;
}

/// <summary>
/// Whether a key is present. Kept apart from nullability because each construct decides it separately:
/// <c>FOR JSON</c> leaves a null key out unless asked to keep it, while a rowset always has every column.
/// </summary>
public enum IrPresence
{
    /// <summary>The key is always there, with a null value when the value is null: every column of a rowset.</summary>
    Always,

    /// <summary>The key is left out when its value is null, as <c>FOR JSON</c> does without <c>INCLUDE_NULL_VALUES</c>.</summary>
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
/// <param name="Sql">
/// The source's own type, as written, such as <c>decimal(19,2)</c>. Informational. Null when the value has no SQL type: in
/// results the config file declares (the <c>declared</c> null reason marks them), whose declaration is a JSON Schema.
/// </param>
/// <param name="Facets">
/// What the database guarantees about the value beyond its kind: lengths, digits, bounds and value sets, each with
/// where it came from. Null when nothing more is known, which claims nothing.
/// </param>
public sealed record IrScalar(IrScalarKind Kind, string? Format, string? Sql, IrFacets? Facets = null) : IrType;

/// <summary>
/// Facts about a scalar's non-null values, each with its source. Every one is a guarantee, never a guess: a fact
/// that is not proved is left out, which only leaves the type wider. None of them says anything about null;
/// nullability is the field's.
/// </summary>
/// <param name="Length">The string's length: a maximum, or a fixed length.</param>
/// <param name="MinLength">The string's minimum length in UTF-16 code units.</param>
/// <param name="Digits">The decimal's precision and scale.</param>
/// <param name="Min">The number's lower bound.</param>
/// <param name="Max">The number's upper bound.</param>
/// <param name="Values">A set the value is drawn from.</param>
public sealed record IrFacets(
    IrLength? Length = null,
    IrMinLength? MinLength = null,
    IrDigits? Digits = null,
    IrBound? Min = null,
    IrBound? Max = null,
    IrValueSet? Values = null)
{
    /// <summary>Whether no fact is known.</summary>
    public bool IsEmpty => Length is null && MinLength is null && Digits is null && Min is null && Max is null && Values is null;
}

/// <summary>A string's length.</summary>
/// <param name="Value">The maximum, or the fixed length.</param>
/// <param name="IsFixed">Whether every value has exactly this length (padded), rather than at most it.</param>
/// <param name="Unit">What <paramref name="Value"/> counts.</param>
/// <param name="Encoding">
/// For <see cref="IrLengthUnit.Bytes"/>, the encoding the bytes are in, when it is known. In a single-byte encoding
/// a byte is a UTF-16 code unit, so the length is exact; in UTF-8 or a double-byte encoding a character can take
/// several bytes, so a maximum is only an upper bound on the UTF-16 length, and a fixed length is not one at all.
/// </param>
/// <param name="From">Where it came from.</param>
public sealed record IrLength(int Value, bool IsFixed, IrLengthUnit Unit, IrByteEncoding Encoding, IrFactSource From);

/// <summary>What a length counts.</summary>
public enum IrLengthUnit
{
    /// <summary>UTF-16 code units, as JavaScript's <c>length</c> counts them.</summary>
    Utf16,

    /// <summary>Bytes of the stored encoding (see <see cref="IrLength.Encoding"/>).</summary>
    Bytes,
}

/// <summary>The encoding of a length counted in bytes.</summary>
public enum IrByteEncoding
{
    /// <summary>Not known, or not applicable (a length in UTF-16 code units).</summary>
    Unknown,

    /// <summary>One byte per character, each a single UTF-16 code unit.</summary>
    SingleByte,

    /// <summary>UTF-8: one to four bytes per character.</summary>
    Utf8,

    /// <summary>A double-byte character set: one or two bytes per character.</summary>
    DoubleByte,
}

/// <param name="Value">The minimum length, in UTF-16 code units.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrMinLength(int Value, IrFactSource From);

/// <summary>A decimal's digits: at most <paramref name="Precision"/> in all, <paramref name="Scale"/> of them after the point.</summary>
public sealed record IrDigits(int Precision, int Scale, IrFactSource From);

/// <summary>A bound on a number.</summary>
/// <param name="Value">The bound as exact decimal text, such as <c>-99999999999999999.99</c>; never rounded to a double.</param>
/// <param name="Inclusive">Whether the bound itself is allowed.</param>
/// <param name="From">Where it came from.</param>
public sealed record IrBound(string Value, bool Inclusive, IrFactSource From);

/// <summary>A set the value is drawn from.</summary>
/// <param name="Items">The values, in a stable order. Numbers are exact decimal text.</param>
/// <param name="Match">How a value relates to the items.</param>
/// <param name="From">Every source that narrowed the set.</param>
public sealed record IrValueSet(IReadOnlyList<IrValue> Items, IrValueMatch Match, IReadOnlyList<IrFactSource> From);

/// <summary>One item of a value set.</summary>
/// <param name="Text">A string's value, or a number's exact decimal text.</param>
/// <param name="IsString">Whether it is a string; otherwise a number.</param>
public sealed record IrValue(string Text, bool IsString);

/// <summary>How a value relates to the items of its set.</summary>
public enum IrValueMatch
{
    /// <summary>Every value is one of the items, exactly.</summary>
    Exact,

    /// <summary>
    /// Every value equals one of the items under the database's string comparison, which may ignore case, accents
    /// and trailing spaces: <c>'Open '</c> can pass a check for <c>'open'</c>. True of the database, but not an
    /// exact set, so an emitter must not treat it as one.
    /// </summary>
    Collation,

    /// <summary>
    /// Every value is one of the items followed by any number of spaces (U+0020): exact but for the trailing spaces
    /// SQL Server ignores in comparisons, as under a binary collation.
    /// </summary>
    Padded,
}

/// <summary>Where a fact came from, for the origins graph.</summary>
/// <param name="Kind">One of <see cref="IrFactSources"/>.</param>
/// <param name="Ref">The type's name for a type, or the constraint's name (or its location, when unnamed) for a check.</param>
public sealed record IrFactSource(string Kind, string Ref);

/// <summary>The kinds of <see cref="IrFactSource"/>.</summary>
public static class IrFactSources
{
    /// <summary>The value's type, such as <c>nvarchar(40)</c> or an alias type's name.</summary>
    public const string Type = "type";

    /// <summary>A check constraint.</summary>
    public const string Check = "check";

    /// <summary>
    /// The engine itself: a value it writes from a closed set, such as <c>$action</c> in a <c>MERGE</c>'s <c>OUTPUT</c>. The
    /// ref names the value.
    /// </summary>
    public const string Engine = "engine";

    /// <summary>
    /// An expression of the procedure whose every result is a literal: a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> gives one of
    /// them, exactly as written, and so does a column of a set operation each of whose queries gives a literal (0.13). The ref
    /// is the expression's (or the literal's) location.
    /// </summary>
    public const string Expression = "expression";
}

/// <summary>An array of items.</summary>
/// <param name="Items">The items' type when they are not null.</param>
public sealed record IrArray(IrType Items) : IrType
{
    /// <summary>
    /// Why an item can be null. Empty, the default, means no item is ever null: FOR JSON and the constructors' default
    /// <c>ABSENT ON NULL</c> leave nulls out of arrays, so only <c>NULL ON NULL</c> puts them in.
    /// </summary>
    public IReadOnlyList<IrNullReason> ItemsNullableBecause { get; init; } = [];

    /// <summary>Whether an item can be null: true exactly when <see cref="ItemsNullableBecause"/> gives a reason.</summary>
    public bool ItemsNullable => ItemsNullableBecause.Count > 0;

    /// <summary>The fewest items a non-null array has, when more than none is proved.</summary>
    public int? MinItems { get; init; }

    /// <summary>The most items it has, when that is bounded.</summary>
    public int? MaxItems { get; init; }
}

/// <summary>
/// An array of a fixed length whose every position has its own type, as <c>json_array</c> builds one: each element is
/// there, possibly null under <c>NULL ON NULL</c>.
/// </summary>
public sealed record IrTuple(IReadOnlyList<IrElement> Items) : IrType;

/// <summary>One position of a <see cref="IrTuple"/>.</summary>
/// <param name="Type">The element's type when it is not null.</param>
/// <param name="NullableBecause">Why it can be null; empty when it is proved never null.</param>
public sealed record IrElement(IrType Type, IReadOnlyList<IrNullReason> NullableBecause)
{
    /// <summary>Whether the element can be null: true exactly when <see cref="NullableBecause"/> gives a reason.</summary>
    public bool Nullable => NullableBecause.Count > 0;
}

/// <summary>
/// An object whose keys are data, not a fixed set, as <c>json_objectagg</c> builds one: every key is a string, and
/// every value has one type.
/// </summary>
/// <param name="Values">The values' type when they are not null.</param>
/// <param name="ValuesNullableBecause">Why a value can be null; empty when no value is ever null.</param>
public sealed record IrRecord(IrType Values, IReadOnlyList<IrNullReason> ValuesNullableBecause) : IrType
{
    /// <summary>Whether a value can be null: true exactly when <see cref="ValuesNullableBecause"/> gives a reason.</summary>
    public bool ValuesNullable => ValuesNullableBecause.Count > 0;
}

/// <summary>
/// A value that has one of several types, such as an item of a <c>json_array</c> whose elements differ in type. At
/// least two variants, none of them the same.
/// </summary>
public sealed record IrUnion(IReadOnlyList<IrType> Variants) : IrType;

/// <summary>A reference to a named object type in <see cref="IrDocument.Types"/>.</summary>
public sealed record IrRef(string Name) : IrType;

/// <summary>
/// A string that holds JSON. Distinct from embedded JSON: once JSON is stored or returned as a column, passes through
/// a scalar function or is built <c>WITHOUT_ARRAY_WRAPPER</c>, a consumer receives text and must parse it.
/// </summary>
/// <param name="Content">
/// What the text parses to, when it was built here: a non-null string holds JSON of this type. Null when the JSON is
/// data (a <c>json</c> column, a parameter) or its shape is unknown, which claims nothing beyond "JSON".
/// </param>
public sealed record IrJsonText(IrType? Content = null) : IrType;

/// <summary>
/// A value declared by the config file to be of a type the application already defines: a schema named
/// <paramref name="Name"/>, exported by the module <paramref name="From"/>. The emitter imports and uses it as it is,
/// so the value is still checked at runtime, by the application's own schema.
/// </summary>
/// <param name="Name">The exported name, an identifier (<see cref="IrIdentifiers"/>), such as <c>OrderSettingsSchema</c>.</param>
/// <param name="From">The module to import it from, as the generated module should write it.</param>
public sealed record IrImport(string Name, string From) : IrType;

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
    /// <summary>Text, such as SQL Server's character types give.</summary>
    String,

    /// <summary>A number; <see cref="IrScalar.Format"/> says which (an integer of some width, a decimal, a float).</summary>
    Number,

    /// <summary>True or false: SQL Server's <c>bit</c>.</summary>
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
    /// <summary>An integer from 0 to 255, such as SQL Server's <c>tinyint</c>.</summary>
    public const string Uint8 = "uint8";

    /// <summary>A signed 16-bit integer.</summary>
    public const string Int16 = "int16";

    /// <summary>A signed 32-bit integer, which a JavaScript number holds exactly.</summary>
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

    /// <summary>
    /// It comes from the optional side of an outer join, which has no row when nothing matches: a left join's right side, or
    /// either side of a full join (0.13).
    /// </summary>
    public const string OuterJoin = "outer-join";

    /// <summary>
    /// It comes from the right side of an <c>OUTER APPLY</c>, which has no row for an outer row when the applied query returns
    /// none. An applied query that always returns a row (an aggregate without <c>GROUP BY</c>, a query without <c>FROM</c>, a
    /// <c>VALUES</c> list) never gives it.
    /// </summary>
    public const string OuterApply = "outer-apply";

    /// <summary>It is a subquery, which is null when it returns no rows.</summary>
    public const string EmptySubquery = "empty-subquery";

    /// <summary>It is a nested object that is left out when every one of its fields is null.</summary>
    public const string AllFieldsNull = "all-fields-null";

    /// <summary>It is an aggregate (<c>MIN</c>, <c>MAX</c>, <c>json_arrayagg</c>, <c>json_objectagg</c>) without GROUP BY, which is null over no rows.</summary>
    public const string EmptyAggregate = "empty-aggregate";

    /// <summary>It is a scalar function's result, typed from the declared return type only, so it may be NULL.</summary>
    public const string FunctionResult = "function-result";

    /// <summary>It is a function's parameter, which a caller may pass as NULL.</summary>
    public const string Parameter = "parameter";

    /// <summary>
    /// It is a call of a function declared <c>WITH RETURNS NULL ON NULL INPUT</c> with an argument that may be null: such
    /// a call is NULL without running the body.
    /// </summary>
    public const string NullInput = "null-input";

    /// <summary>It is a query result, which returns nothing when no rows match.</summary>
    public const string NoRows = "no-rows";

    /// <summary>
    /// It is a result whose statement may not run (it is in a branch, or after a <c>RETURN</c> that may be taken), so the
    /// result set may be missing altogether rather than empty. Only a result after which every result may be missing too
    /// has it, so the result sets a client receives are always the first ones listed.
    /// </summary>
    public const string NotRun = "not-run";

    /// <summary>It is a procedure's variable, which holds null until a value that is never null is assigned on every path.</summary>
    public const string Variable = "variable";

    /// <summary>
    /// It is a <c>COUNT</c>, an <c>int</c>, which over more than 2,147,483,647 rows overflows: an error by default, but NULL
    /// when the caller's session has <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COUNT_BIG</c> does not.
    /// </summary>
    public const string CountOverflow = "count-overflow";

    /// <summary>
    /// It is arithmetic, a <c>SUM</c> or <c>AVG</c>, a numeric function (<c>ROUND</c>, <c>ABS</c>, <c>POWER</c>, <c>EXP</c>, …) or a
    /// conversion that can overflow its type or divide by zero: an error by default, but NULL when the caller's session has
    /// <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off, which a procedure cannot prevent.
    /// <c>COALESCE</c> or <c>ISNULL</c> with a value never null removes it.
    /// </summary>
    public const string ArithmeticOverflow = "arithmetic-overflow";

    /// <summary>It is <c>TRY_CAST</c> or <c>TRY_CONVERT</c> of a value that may not convert, which gives NULL instead of an error.</summary>
    public const string FailedConversion = "failed-conversion";

    /// <summary>
    /// It is a grouped column of a query grouped by <c>ROLLUP</c>, <c>CUBE</c> or <c>GROUPING SETS</c>, whose super-aggregate
    /// rows hold NULL in the columns they roll up, whatever the filters proved of them.
    /// </summary>
    public const string GroupingSet = "grouping-set";

    /// <summary>
    /// It is a column of a <c>MERGE</c>'s <c>OUTPUT</c> that has no value for some of the actions the statement takes: a
    /// <c>deleted</c> column for the rows it inserts, an <c>inserted</c> column for the rows it deletes, and a column of its
    /// source for the rows it changes because they are not matched by the source.
    /// </summary>
    public const string MergeAction = "merge-action";

    /// <summary>It is a <c>CASE</c> without <c>ELSE</c>, which is NULL when no <c>WHEN</c> holds.</summary>
    public const string NoElse = "no-else";

    /// <summary>
    /// A result of a <c>CASE</c>, <c>IIF</c> or <c>CHOOSE</c> it may give is the <c>NULL</c> literal, or it is (or reads) the
    /// <c>NULL</c> literal itself, as a bare <c>NULL</c> select item (an <c>int</c>) or <c>CAST(NULL AS date)</c> (0.14).
    /// </summary>
    public const string NullLiteral = "null-literal";

    /// <summary>It is a <c>CHOOSE</c> whose index may be below 1 or above its count of values, which gives NULL.</summary>
    public const string OutOfRange = "out-of-range";

    /// <summary>
    /// It is a <c>COALESCE</c> one of whose arguments, a subquery or a call, is tested and then read again: the second read may
    /// be NULL (another statement changed the rows between them, or the call gives another value). Before 0.12 the argument's
    /// own reasons were given instead. No seed reaches it; it is observed.
    /// </summary>
    public const string ReadTwice = "read-twice";

    /// <summary>It is a <c>NULLIF</c>, NULL when its two arguments compare equal.</summary>
    public const string NullIf = "nullif";

    /// <summary>
    /// It is a column <c>OPENJSON</c> reads from JSON (0.13), or a <c>JSON_VALUE</c> (0.14): NULL where the JSON holds
    /// <c>null</c> there, under a <c>strict</c> path too, and, under a lax path, where nothing is there, or an <c>AS JSON</c>
    /// column finds a scalar, or a scalar column or <c>JSON_VALUE</c> an object or array (or, for <c>JSON_VALUE</c>, a string
    /// longer than 4,000 characters). (A number too large for the column is NULL as well under the caller's
    /// <c>ANSI_WARNINGS</c> and <c>ARITHABORT</c> off.)
    /// </summary>
    public const string JsonValue = "json-value";

    /// <summary>
    /// It is a window function over a frame that may hold no row (0.14): an aggregate, <c>FIRST_VALUE</c> or
    /// <c>LAST_VALUE</c> whose <c>ROWS</c> frame may leave out the current row (<c>ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING</c>
    /// has nothing before the first row) is NULL there, over a column never null too. The default frame, and any frame that
    /// holds the current row, is never empty.
    /// </summary>
    public const string EmptyFrame = "empty-frame";

    /// <summary>
    /// It is <c>LAG</c> or <c>LEAD</c> without a default (0.14), NULL where the row it reads is past the partition's edge.
    /// </summary>
    public const string WindowEdge = "window-edge";

    /// <summary>
    /// It is a math function that may be given a value outside its domain (0.15): <c>SQRT</c> of a negative, <c>LOG</c> or
    /// <c>LOG10</c> of 0 or less or to a base of 0 or 1, <c>POWER</c> of a negative to a fractional power. That is an invalid
    /// floating point operation: an error by default, but NULL when the caller's session has <c>ANSI_WARNINGS</c> and
    /// <c>ARITHABORT</c> off, which a procedure cannot prevent, as for <see cref="ArithmeticOverflow"/>.
    /// </summary>
    public const string DomainError = "domain-error";

    /// <summary>
    /// It is a sample statistic, <c>STDEV</c> or <c>VAR</c> (0.15), which is NULL over fewer than two values that are not NULL:
    /// a group or a window's frame of one row, with <c>GROUP BY</c> too. The population forms, <c>STDEVP</c> and <c>VARP</c>,
    /// are 0 over one value.
    /// </summary>
    public const string TooFewValues = "too-few-values";

    /// <summary>Its type is unknown, so nothing is proved about it.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The config file declares it may be null, or absent: in results declared for a procedure whose results cannot be
    /// inferred. <see cref="IrNullReason.At"/> is the procedure declared.
    /// </summary>
    public const string Declared = "declared";
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

    /// <summary>
    /// Functions that call themselves, directly or through others, whose shape reached no fixed point in the bounded
    /// number of steps, or that never return a value (every return calls itself again).
    /// </summary>
    public const string UnresolvedRecursion = "unresolved-recursion";

    /// <summary>JSON whose shape is not visible here, such as JSON text read from a column.</summary>
    public const string JsonShapeUnknown = "json-shape-unknown";

    /// <summary>
    /// A temporary table the procedure does not create on every path before it reads it: a table of that name created by
    /// the caller, if there is one, whose shape depends on who calls.
    /// </summary>
    public const string CallerTempTable = "caller-temp-table";

    /// <summary>
    /// Result sets that cannot be listed because which are returned depends on the path through the procedure in a way the
    /// list cannot express: a result inside a loop, or results of different encodings at one position.
    /// </summary>
    public const string VaryingResults = "varying-results";

    /// <summary>Dynamic SQL (<c>EXEC (@sql)</c>, <c>sp_executesql</c>): what it runs is a string built when the procedure runs.</summary>
    public const string DynamicSql = "dynamic-sql";

    /// <summary><c>EXEC @name</c>: the procedure is chosen when the call runs.</summary>
    public const string ProcedureVariable = "procedure-variable";

    /// <summary>
    /// A system procedure (<c>sp_…</c>, <c>xp_…</c>), whose results are SQL Server's own. A procedure of the project with such a
    /// name may be one too: SQL Server runs its own procedure of that name instead, if it has one.
    /// </summary>
    public const string SystemProcedure = "system-procedure";

    /// <summary>A procedure that is not in the project: in another database, on a linked server, or nowhere.</summary>
    public const string MissingProcedure = "missing-procedure";

    /// <summary>An unqualified procedure name that more than one schema has: which runs depends on the caller's default schema.</summary>
    public const string AmbiguousProcedure = "ambiguous-procedure";

    /// <summary>
    /// A statement fires a trigger of the project that may return result sets of its own, which arrive among the
    /// procedure's. SQL Server allows it unless the server's <c>disallow results from triggers</c> option is on, which it is
    /// not by default.
    /// </summary>
    public const string TriggerResults = "trigger-results";

    /// <summary>
    /// A result set carries a column the server marks hidden, which some drivers list among the columns and others leave
    /// out, so its keys depend on the client: <c>FETCH</c> without <c>INTO</c>, whose row has <c>ROWSTAT</c>.
    /// </summary>
    public const string HiddenColumn = "hidden-column";
}

using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>
/// The codes of the problems found in a config file (docs/decisions/0036-diagnostics-have-stable-codes.md), each explained on
/// its page in <c>docs/diagnostics</c>: reading it (HAR30xx), the JSON Schemas it declares results with (HAR31xx), and
/// applying it to what was inferred (HAR40xx selection, HAR41xx overrides, HAR42xx declared results, HAR43xx naming).
/// </summary>
public static class ConfigCodes
{
    private const DiagnosticSeverity Error = DiagnosticSeverity.Error;
    private const DiagnosticSeverity Warning = DiagnosticSeverity.Warning;

    /// <summary>The config file, or a schema file it names, can't be read.</summary>
    public static DiagnosticCode Unreadable { get; } = new(3001, Error, "A config file can't be read");

    /// <summary>The text isn't JSON, with the comments and trailing commas the file allows.</summary>
    public static DiagnosticCode NotJson { get; } = new(3002, Error, "The config file isn't JSON");

    /// <summary>An object names one key twice, so one of the two would be ignored.</summary>
    public static DiagnosticCode KeyTwice { get; } = new(3003, Error, "A key is written twice in one object");

    /// <summary>A setting haruspex doesn't know, which would otherwise do nothing in silence.</summary>
    public static DiagnosticCode UnknownSetting { get; } = new(3004, Error, "An unknown setting");

    /// <summary>A setting whose value is of the wrong kind: a number for a string, an empty list, a word that isn't a choice.</summary>
    public static DiagnosticCode WrongKind { get; } = new(3005, Error, "A value of the wrong kind");

    /// <summary>A pattern of procedure names that isn't well formed.</summary>
    public static DiagnosticCode BadPattern { get; } = new(3006, Error, "A pattern isn't well formed");

    /// <summary>A procedure's key, which names one procedure, uses a wildcard.</summary>
    public static DiagnosticCode WildcardProcedure { get; } = new(3007, Error, "A procedure's key uses a wildcard");

    /// <summary>A schema with two blocks, whose names differ only in case.</summary>
    public static DiagnosticCode SchemaTwice { get; } = new(3008, Error, "A schema has two blocks");

    /// <summary>An exclude in a block without an include, which can never exclude anything.</summary>
    public static DiagnosticCode ExcludeWithoutInclude { get; } = new(3009, Error, "An exclude without an include");

    /// <summary>A naming replacement whose pattern is not a regular expression haruspex can use.</summary>
    public static DiagnosticCode BadRegex { get; } = new(3010, Error, "A regular expression haruspex can't use");

    /// <summary>A claim (declared results, <c>nullable</c>, exact values) without the <c>why</c> that says what proves it.</summary>
    public static DiagnosticCode ClaimWithoutWhy { get; } = new(3011, Error, "A claim without why");

    /// <summary>A module's name that can't name its files, is the shared file's, or differs from another's only in case.</summary>
    public static DiagnosticCode BadModuleName { get; } = new(3024, Error, "A module that can't be named so");

    /// <summary>A schema a split lists in two modules, whose procedures would then be in both.</summary>
    public static DiagnosticCode SchemaInTwoModules { get; } = new(3025, Error, "A schema in two modules");

    /// <summary>A setting that needs another beside it: a replacement's pattern and with, an override's type and import.</summary>
    public static DiagnosticCode NeedsAnother { get; } = new(3012, Error, "A setting needs another beside it");

    /// <summary>A <c>why</c> with no claim beside it to explain.</summary>
    public static DiagnosticCode WhyWithoutClaim { get; } = new(3013, Error, "A why with no claim to explain");

    /// <summary>Path overrides beside declared results, which already say everything.</summary>
    public static DiagnosticCode PathsWithResults { get; } = new(3014, Error, "Paths beside declared results");

    /// <summary>A declared result that is neither a rowset nor JSON, or both.</summary>
    public static DiagnosticCode RowsetOrJson { get; } = new(3015, Error, "A result is neither a rowset nor JSON, or both");

    /// <summary><c>mayBeAbsent</c> on a rowset, which is there even with no rows.</summary>
    public static DiagnosticCode AbsentRowset { get; } = new(3016, Error, "A rowset declared as maybe absent");

    /// <summary>An override's path that isn't well formed.</summary>
    public static DiagnosticCode BadPath { get; } = new(3017, Error, "A path isn't well formed");

    /// <summary><c>"nullable": true</c>, which declares nothing, since a value is nullable unless proved otherwise.</summary>
    public static DiagnosticCode NullableTrue { get; } = new(3018, Error, "A value declared nullable");

    /// <summary>A generated name, or an imported schema's, that isn't a valid identifier.</summary>
    public static DiagnosticCode BadName { get; } = new(3019, Error, "A name isn't a valid identifier");

    /// <summary>
    /// A procedure's <c>sets</c> that can't name its result sets: empty, or a name that isn't a camel-case identifier, is given
    /// twice, or is a key its function already returns (<c>output</c>, <c>returnValue</c>).
    /// </summary>
    public static DiagnosticCode BadSetNames { get; } = new(3023, Error, "Set names that can't be used");

    /// <summary>A path override that sets nothing.</summary>
    public static DiagnosticCode EmptyOverride { get; } = new(3020, Error, "An override says nothing");

    /// <summary>
    /// A folder in <c>references</c> that isn't there: a typo would otherwise show only as a referenced DACPAC not found, which
    /// blames the package.
    /// </summary>
    public static DiagnosticCode NoSuchFolder { get; } = new(3021, Error, "A references folder isn't there");

    /// <summary>
    /// An entry of <c>queries</c> that reaches outside the config file's folder: a query file belongs to the module whose folder
    /// holds it, so that each file has one module.
    /// </summary>
    public static DiagnosticCode QueriesOutside { get; } = new(3022, Error, "A queries entry outside the config file's folder");

    /// <summary>A rowset declared by something other than the schema of one row, an object with properties.</summary>
    public static DiagnosticCode RowsetShape { get; } = new(3101, Error, "A rowset isn't declared by one row's schema");

    /// <summary>A rowset's <c>required</c> that leaves a column out, though every column is always there.</summary>
    public static DiagnosticCode RowsetRequired { get; } = new(3102, Error, "A rowset's required leaves a column out");

    /// <summary>A rowset's column that isn't a scalar.</summary>
    public static DiagnosticCode RowsetColumn { get; } = new(3103, Error, "A rowset's column isn't a scalar");

    /// <summary>A JSON result's document declared nullable, which it never is.</summary>
    public static DiagnosticCode NullDocument { get; } = new(3104, Error, "A JSON document declared nullable");

    /// <summary>A schema without a type, or with only <c>null</c>.</summary>
    public static DiagnosticCode NoType { get; } = new(3105, Error, "A schema without a type");

    /// <summary>Retired: a schema of several types is read as a union of them.</summary>
    public static DiagnosticCode SeveralTypes { get; } = new(3106, Error, "A schema of several types") { Retired = true };

    /// <summary>A type that JSON Schema doesn't have.</summary>
    public static DiagnosticCode NotAType { get; } = new(3107, Error, "Not a JSON Schema type");

    /// <summary>Retired: an object schema without properties is read as an object whose keys are data, every value any JSON.</summary>
    public static DiagnosticCode NoProperties { get; } = new(3108, Error, "An object schema without properties") { Retired = true };

    /// <summary>A <c>required</c> that names a key <c>properties</c> doesn't have.</summary>
    public static DiagnosticCode RequiredUnknownKey { get; } = new(3109, Error, "A required key that isn't a property");

    /// <summary>A key that may be both left out and null, which can't be expressed yet.</summary>
    public static DiagnosticCode OptionalAndNullable { get; } = new(3110, Error, "A key both optional and nullable");

    /// <summary>Retired: an array schema without items is read as an array of any JSON.</summary>
    public static DiagnosticCode NoItems { get; } = new(3111, Error, "An array schema without items") { Retired = true };

    /// <summary>A <c>$ref</c> outside the schema's own definitions.</summary>
    public static DiagnosticCode ForeignReference { get; } = new(3112, Error, "A reference outside the schema");

    /// <summary>A <c>$ref</c> to a definition the schema doesn't have.</summary>
    public static DiagnosticCode MissingReference { get; } = new(3113, Error, "A reference to nothing");

    /// <summary>A schema that refers to itself, which isn't supported yet.</summary>
    public static DiagnosticCode RecursiveSchema { get; } = new(3114, Error, "A recursive schema");

    /// <summary>A column's declared JSON allows the JSON null at its top, which the IR's JSON text can't say yet.</summary>
    public static DiagnosticCode NullColumnJson { get; } = new(3115, Error, "A column's JSON declared null at its top");

    /// <summary>A <c>format</c> haruspex doesn't check, so the generated schema doesn't either.</summary>
    public static DiagnosticCode UncheckedFormat { get; } = new(3150, Warning, "A format haruspex doesn't check");

    /// <summary>A JSON Schema keyword haruspex doesn't support yet, so the generated schema doesn't check it.</summary>
    public static DiagnosticCode UncheckedKeyword { get; } = new(3151, Warning, "A keyword haruspex doesn't check");

    /// <summary>A keyword beside <c>$ref</c>, which ignores it.</summary>
    public static DiagnosticCode IgnoredBesideReference { get; } = new(3152, Warning, "A keyword ignored beside a reference");

    /// <summary>A schema key that names no schema of the project.</summary>
    public static DiagnosticCode NoSuchSchema { get; } = new(4001, Error, "A schema the project doesn't have");

    /// <summary>An include pattern that selects nothing in its schema.</summary>
    public static DiagnosticCode IncludeMatchesNothing { get; } = new(4002, Error, "An include matches nothing");

    /// <summary>An exclude pattern that removes nothing the includes selected.</summary>
    public static DiagnosticCode ExcludeMatchesNothing { get; } = new(4003, Error, "An exclude matches nothing");

    /// <summary>A schema block whose exclude removes everything its include selects.</summary>
    public static DiagnosticCode SelectsNothing { get; } = new(4004, Error, "A schema selects nothing");

    /// <summary>A command line <c>--include</c> that matches no procedure.</summary>
    public static DiagnosticCode CommandLineIncludeMatchesNothing { get; } = new(4005, Error, "An --include matches nothing");

    /// <summary>A command line <c>--include</c> whose every match the config file's excludes leave out.</summary>
    public static DiagnosticCode CommandLineIncludeExcluded { get; } = new(4006, Error, "An --include selects only excluded procedures");

    /// <summary>
    /// An entry of <c>queries</c> that names no query file of this module: a typo would otherwise drop the functions of the files it
    /// meant without anyone noticing. A file below a folder with a config file of its own is that module's, not this one's.
    /// </summary>
    public static DiagnosticCode QueriesMatchNothing { get; } = new(4007, Error, "A queries entry names no query file");

    /// <summary>A selected procedure whose schema no module of the split lists, or a query file naming a module the split doesn't have.</summary>
    public static DiagnosticCode NoModule { get; } = new(4008, Error, "An object in no module");

    /// <summary>A query file's module comment when the config file doesn't split its output, where it does nothing.</summary>
    public static DiagnosticCode ModuleUnsplit { get; } = new(4009, Warning, "A module comment where the output isn't split");

    /// <summary>An override of a procedure the project doesn't have.</summary>
    public static DiagnosticCode NoSuchProcedure { get; } = new(4101, Error, "An override of a procedure the project doesn't have");

    /// <summary>An override of a procedure that isn't selected.</summary>
    public static DiagnosticCode NotSelected { get; } = new(4102, Error, "An override of a procedure that isn't selected");

    /// <summary>Two overrides of one procedure, whose names differ only in case or brackets.</summary>
    public static DiagnosticCode OverriddenTwice { get; } = new(4103, Error, "Two overrides of one procedure");

    /// <summary>An override's path that leads nowhere in the procedure's results.</summary>
    public static DiagnosticCode NoSuchPath { get; } = new(4104, Error, "A path that doesn't exist");

    /// <summary>An override's path through a value of several types, between which it can't choose.</summary>
    public static DiagnosticCode PathThroughUnion { get; } = new(4105, Error, "A path through a value of several types");

    /// <summary>An override of the recursive type a function builds, which every procedure that reaches it shares.</summary>
    public static DiagnosticCode SharedType { get; } = new(4106, Error, "An override of a shared recursive type");

    /// <summary><c>"nullable": false</c> on what JSON text holds, which is never null itself.</summary>
    public static DiagnosticCode NullableJsonContent { get; } = new(4107, Error, "Nullability declared for JSON text's content");

    /// <summary><c>"values": "exact"</c> on a value with no value set.</summary>
    public static DiagnosticCode NoValueSet { get; } = new(4108, Error, "Exact values declared without a value set");

    /// <summary>A name given to a value that isn't an object type.</summary>
    public static DiagnosticCode NameNotObject { get; } = new(4109, Error, "A name for a value that isn't an object");

    /// <summary><c>"nullable": false</c> where the inference already proves it.</summary>
    public static DiagnosticCode NullableAgrees { get; } = new(4110, Warning, "A nullability the inference already proves");

    /// <summary><c>"values": "exact"</c> where the inference already proves the set exact.</summary>
    public static DiagnosticCode ExactAgrees { get; } = new(4111, Warning, "Exact values the inference already proves");

    /// <summary>
    /// A procedure's <c>sets</c> that don't name its result sets one each: more or fewer names than the sets it is known to
    /// return, or results that can't be listed, so there is nothing to name.
    /// </summary>
    public static DiagnosticCode SetsMiscounted { get; } = new(4112, Error, "Set names that don't match the result sets");

    /// <summary>A column declared to hold JSON that can't: not a table's, computed, not of a string type, or not in the project.</summary>
    public static DiagnosticCode ColumnNotDeclarable { get; } = new(4113, Error, "A column that can't hold declared JSON");

    /// <summary>Declared results that disagree with what haruspex infers.</summary>
    public static DiagnosticCode DeclarationDisagrees { get; } = new(4201, Error, "Declared results disagree with the inference");

    /// <summary>Declared results the inference already gives.</summary>
    public static DiagnosticCode DeclarationAgrees { get; } = new(4202, Warning, "Declared results the inference already gives");

    /// <summary>Declared results of a procedure that isn't selected and that no selected procedure calls.</summary>
    public static DiagnosticCode DeclarationUnused { get; } = new(4203, Warning, "Declared results nothing uses");

    /// <summary>A selected procedure whose result sets only a declaration can give.</summary>
    public static DiagnosticCode ResultsToDeclare { get; } = new(4204, Warning, "Result sets only a declaration can give");

    /// <summary>Values a call that isn't followed leaves unknown, which only a path override can type.</summary>
    public static DiagnosticCode UnknownAfterCall { get; } = new(4205, Warning, "Values unknown after a call that isn't followed");

    /// <summary>A procedure the naming rules can't name.</summary>
    public static DiagnosticCode ProcedureUnnamed { get; } = new(4301, Error, "A procedure that can't be named");

    /// <summary>A type a function builds that the naming rules can't name.</summary>
    public static DiagnosticCode TypeUnnamed { get; } = new(4302, Error, "A type that can't be named");

    /// <summary>Two generated names that are the same.</summary>
    public static DiagnosticCode NamesClash { get; } = new(4303, Error, "Generated names clash");

    /// <summary>A name the naming rules already give.</summary>
    public static DiagnosticCode NameAgrees { get; } = new(4304, Warning, "A name the naming rules already give");
}

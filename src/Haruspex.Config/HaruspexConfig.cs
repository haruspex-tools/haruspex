using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>A config file, read and checked: what <c>haruspex.json</c> says, with where each part was written.</summary>
public sealed record HaruspexConfig
{
    /// <summary>The name the file is looked for by.</summary>
    public const string FileName = "haruspex.json";

    /// <summary>The file, as found or named. Problems are reported against it.</summary>
    public required string File { get; init; }

    /// <summary>The file as a whole, for a problem with no place in it; null when there is no file.</summary>
    public FileLocation? Location => File.Length == 0 ? null : new FileLocation(File);

    /// <summary>The folder holding the file, which its relative paths are relative to.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// The folder of <c>.sql</c> files, the project file or the DACPAC, resolved against <see cref="Directory"/>; or null when the file
    /// doesn't say, or when <c>source</c> lists several folders (<see cref="SourceFolders"/>).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// The folders of <c>.sql</c> files <c>source</c> lists, when it lists more than one, as written (relative to
    /// <see cref="Directory"/>, unless absolute), in the order written, so that a message names each as the user wrote it: read together as one database (docs/decisions/0127-a-source-may-be-several-folders.md). Empty otherwise, and
    /// then <see cref="Source"/> names the one source.
    /// </summary>
    public EquatableArray<string> SourceFolders { get; init; } = [];

    /// <summary>
    /// What the application already running was built against (<c>baseline</c>): a folder of <c>.sql</c> files, a project file, a
    /// DACPAC or an IR file, resolved against <see cref="Directory"/>; or null when the file doesn't say. <c>haruspex check</c>
    /// judges whether the source fits within it (docs/decisions/0069-check-with-a-baseline-judges-the-deploy-window.md).
    /// </summary>
    public string? Baseline { get; init; }

    /// <summary>
    /// The folders to look for the DACPACs a DACPAC or a project file refers to (<c>references</c>), resolved against
    /// <see cref="Directory"/>, in order, after the folder of the package that refers to them or where a project's build takes them
    /// from; empty when the file names none.
    /// </summary>
    public EquatableArray<string> References { get; init; } = [];

    /// <summary>
    /// The DACPACs, by file name, declared built from a project's scripts (<c>builds</c>), though what made them isn't known: their
    /// primary keys and unique constraints are then taken as enforced, which is the user's assumption
    /// (docs/decisions/0120-an-extract-s-key-constraints-are-no-key.md). The IR records each package read as one (<c>source.builds</c>,
    /// docs/decisions/0122-the-ir-records-the-packages-declared-builds.md). Empty when the file names none.
    /// </summary>
    public EquatableArray<string> Builds { get; init; } = [];

    /// <summary>
    /// The names of the extended properties read as descriptions (<c>descriptions</c>), compared ignoring case:
    /// <c>MS_Description</c> unless the file says otherwise, and none for an empty array
    /// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
    /// </summary>
    public EquatableArray<string> Descriptions { get; init; } = ["MS_Description"];

    /// <summary>
    /// The database's default collation (<c>collation</c>), for a source that doesn't record one, or null when the file doesn't say:
    /// the collation a column without <c>COLLATE</c> takes (<c>DatabaseModel.WithDeclaredCollation</c>).
    /// </summary>
    public string? Collation { get; init; }

    /// <summary>
    /// The application's query files (<c>queries</c>): folders, files and patterns relative to <see cref="Directory"/>, in the order
    /// written; empty when the file names none. <see cref="QuerySelection"/> finds the files.
    /// </summary>
    public EquatableArray<QueryPattern> Queries { get; init; } = [];

    /// <summary>The folder to write to, resolved against <see cref="Directory"/>, or null when the file doesn't say.</summary>
    public string? Out { get; init; }

    /// <summary>
    /// How the output is split into modules (<c>split</c>), or null for one module, as without the setting: every file then is
    /// what it always was.
    /// </summary>
    public ModuleSplit? Split { get; init; }

    /// <summary>The settings of each schema (<c>schemas</c>), in the order written: selection, prefix and naming.</summary>
    public EquatableArray<SchemaSettings> Schemas { get; init; } = [];

    /// <summary>
    /// The <c>SET</c> options the callers' connections set (<c>session</c>), each not known unless the file declares it: what
    /// an overflow gives, and string <c>+</c> with a NULL, depend on them.
    /// </summary>
    public SessionOptions Session { get; init; } = SessionOptions.NotKnown;

    /// <summary>
    /// The columns declared to hold JSON of a schema (<c>columns</c>), each translated into the IR, in the order written; empty when
    /// the file declares none.
    /// </summary>
    public EquatableArray<ColumnDeclaration> Columns { get; init; } = [];

    /// <summary>
    /// The external REST endpoints declared to send a response of a schema (<c>endpoints</c>), each translated into the IR, in the
    /// order written; empty when the file declares none.
    /// </summary>
    public EquatableArray<EndpointDeclaration> Endpoints { get; init; } = [];

    /// <summary>
    /// The procedures' and functions' parameters declared to hold JSON of a schema (<c>parameters</c>), each translated into the IR,
    /// in the order written; empty when the file declares none.
    /// </summary>
    public EquatableArray<ParameterDeclaration> Parameters { get; init; } = [];

    /// <summary>The warnings the file acknowledges (<c>acknowledge</c>), each by its code and subject, in the order written.</summary>
    public EquatableArray<Acknowledgement> Acknowledged { get; init; } = [];

    /// <summary>What <c>haruspex check</c> does about unknowns in the module (<c>check.unknowns</c>).</summary>
    public CheckUnknowns CheckUnknowns { get; init; } = CheckUnknowns.Report;

    /// <summary>How result objects treat keys their type does not list.</summary>
    public ObjectsPolicy Objects { get; init; } = ObjectsPolicy.Strip;

    /// <summary>
    /// Which fields the generated code explains in a comment (<c>comments</c>): the surprises unless the file says otherwise
    /// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
    /// </summary>
    public IrComments Comments { get; init; } = IrComments.Surprises;

    /// <summary>
    /// What is written beside the IR (<c>emit</c>), in the order the file names them, each once: the Zod module unless the file
    /// says otherwise.
    /// </summary>
    public EquatableArray<Emitter> Emitters { get; init; } = [Emitter.Zod];

    /// <summary>
    /// Whether the functions file starts with <c>import 'server-only'</c> (<c>serverOnly</c>), for a Next.js application, whose
    /// build then fails where browser code imports it. Off by default: haruspex assumes no framework.
    /// </summary>
    public bool ServerOnly { get; init; }

    /// <summary>The naming rules every schema starts from (the top-level <c>naming</c>).</summary>
    public NamingRules Naming { get; init; } = NamingRules.Default;

    /// <summary>The words the generated code's language reserves, which no name settled for it may be; handed in by the CLI.</summary>
    public ReservedWords Reserved { get; init; } = ReservedWords.None;

    /// <summary>Where the top-level <c>naming</c> is written, or null.</summary>
    public JsonLocation? NamingAt { get; init; }

    /// <summary>
    /// The setting that decides how objects of <paramref name="schema"/> are named, for a problem with a name: the schema's
    /// own <c>naming</c>, else the top-level one, else the schema's block. Null when the file says nothing about it.
    /// </summary>
    public JsonLocation? NamingLocation(string schema) => Schema(schema) is { } settings ? settings.Naming?.At ?? NamingAt ?? settings.At : NamingAt;

    /// <summary>Whether any schema has an include, without which no procedure is selected.</summary>
    public bool SelectsProcedures => Schemas.Any(s => s.Include is not null);

    /// <summary>Whether anything is selected: a schema's procedures, or query files.</summary>
    public bool SelectsAnything => SelectsProcedures || Queries.Count > 0;

    /// <summary>The settings of the schema named <paramref name="schema"/> (ignoring case, as SQL Server does), or null.</summary>
    public SchemaSettings? Schema(string schema) => Schemas.FirstOrDefault(s => string.Equals(s.Name, schema, StringComparison.OrdinalIgnoreCase));

    /// <summary>The naming rules for objects of <paramref name="schema"/>: the top-level rules with the schema's over them.</summary>
    public NamingRules NamingFor(string schema) => Schema(schema) is { } settings ? Naming.For(settings.Naming, settings.Prefix) : Naming;

    /// <summary>The per-procedure overrides, in the order written.</summary>
    public EquatableArray<ProcedureOverride> Procedures { get; init; } = [];

    /// <summary>
    /// Whether the file is applied to a baseline, the database before a change, rather than to the source it was written for.
    /// Its <c>sets</c> name the source's result sets, so a procedure that returns another number of them in the baseline isn't
    /// an error: the baseline's sets take the list's first names where it returns fewer, and none where it returns more. Set
    /// by the command line, never read from the file. Why:
    /// docs/decisions/0073-generate-with-a-baseline-writes-types-that-hold-against-both-databases.md.
    /// </summary>
    public bool AppliedToBaseline { get; init; }

    /// <summary>The settings that apply when there is no config file: nothing selected, every default.</summary>
    public static HaruspexConfig None(string directory, ReservedWords? reserved = null) =>
        new() { File = "", Directory = directory, Reserved = reserved ?? ReservedWords.None };
}

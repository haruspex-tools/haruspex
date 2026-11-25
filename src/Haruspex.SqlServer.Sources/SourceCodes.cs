using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources;

/// <summary>The codes of the problems found reading a folder of scripts, a DACPAC or a project (HAR1xxx), each explained on its page in <c>docs/diagnostics</c>.</summary>
public static class SourceCodes
{
    /// <summary>A script that doesn't parse: nothing in it is read.</summary>
    public static DiagnosticCode ParseError { get; } = new(1001, DiagnosticSeverity.Error, "The SQL doesn't parse");

    /// <summary>An object created twice, so which definition applies depends on the order the scripts are run in.</summary>
    public static DiagnosticCode DefinedTwice { get; } = new(1002, DiagnosticSeverity.Error, "An object is defined more than once");

    /// <summary>
    /// A DACPAC a package refers to that is found nowhere looked: the objects it would give are missing from the model, and a name
    /// that binds to nothing says it may be defined there. One warning for the package, not one for each name.
    /// </summary>
    public static DiagnosticCode ReferenceNotFound { get; } = new(1003, DiagnosticSeverity.Warning, "A referenced DACPAC isn't found");

    /// <summary>Elements of a DACPAC that haruspex doesn't model, counted by type, so that none is passed over in silence.</summary>
    public static DiagnosticCode NotModelled { get; } = new(1004, DiagnosticSeverity.Note, "Elements of a DACPAC that aren't modelled");

    /// <summary>A file given or referred to as a DACPAC that can't be read as one: nothing in it is read.</summary>
    public static DiagnosticCode UnreadablePackage { get; } = new(1005, DiagnosticSeverity.Error, "A file that can't be read as a DACPAC");

    /// <summary>
    /// A database project a project refers to, of the same database, that isn't found: the objects it would give are missing from
    /// the model, and a name that binds to nothing says it may be defined there. One warning for the project, as for a package.
    /// </summary>
    public static DiagnosticCode ProjectNotFound { get; } = new(1007, DiagnosticSeverity.Warning, "A referenced project isn't found");

    /// <summary>
    /// Part of a project file that haruspex doesn't evaluate or understand, and that touches what it reads: the scripts, a script's
    /// settings, or the database's; that part isn't read, never guessed (decision 0044).
    /// </summary>
    public static DiagnosticCode PartNotRead { get; } = new(1008, DiagnosticSeverity.Warning, "Part of a project file isn't read");

    /// <summary>A script a project builds that isn't on disk: a build would stop there, and nothing in it is read.</summary>
    public static DiagnosticCode ScriptNotFound { get; } = new(1009, DiagnosticSeverity.Error, "A script a project builds isn't found");

    /// <summary>A file given or referred to as a database project that can't be read as one: nothing in it is read.</summary>
    public static DiagnosticCode UnreadableProject { get; } = new(1010, DiagnosticSeverity.Error, "A file that can't be read as a database project");

    /// <summary>
    /// Retired: a reference a project made that wasn't read yet (another database, a DACPAC, a package). Each is read
    /// now, as a DACPAC's are, but for one to a database on another server, which <see cref="OtherServer"/> reports.
    /// </summary>
    public static DiagnosticCode ReferenceNotRead { get; } = new(1011, DiagnosticSeverity.Note, "A project's reference isn't read yet") { Retired = true };

    /// <summary>
    /// A reference, from a project or a DACPAC, to a database on another server (<c>ServerSqlCmdVariable</c>): a body reaches it only
    /// by a four-part name, which nothing binds, so it isn't read, and a name in it is unknown as any name in another server is.
    /// </summary>
    public static DiagnosticCode OtherServer { get; } = new(1012, DiagnosticSeverity.Note, "A reference to a database on another server isn't read");

    /// <summary>
    /// A table whose <c>CREATE TABLE</c> lists no columns, a FileTable's or one made from a query (<c>AS SELECT</c>): its columns
    /// aren't read from the definition, so it is passed over, and a name in it is unresolved, as a package's FileTable is.
    /// </summary>
    public static DiagnosticCode ColumnsNotListed { get; } = new(1013, DiagnosticSeverity.Note, "A table whose definition lists no columns isn't read");

    /// <summary>
    /// A query file that isn't one batch: it holds no statement, or <c>GO</c> splits it into several, where <c>sp_executesql</c>
    /// runs one batch and <c>GO</c> is no part of SQL.
    /// </summary>
    public static DiagnosticCode QueryNotOneBatch { get; } = new(1014, DiagnosticSeverity.Error, "A query file isn't one batch");

    /// <summary>
    /// A parameter's declaration comment in a query file (<c>-- @param @order_id int</c>) that can't be taken: it doesn't read, it
    /// names a parameter twice, or it names a variable the file declares itself or doesn't use.
    /// </summary>
    public static DiagnosticCode BadParameterDeclaration { get; } = new(1015, DiagnosticSeverity.Error, "A query file's parameter declaration can't be taken");

    /// <summary>
    /// A query file that uses a variable before its <c>DECLARE</c>: SQL Server refuses the batch (error 137), so the file can't run,
    /// though a body's declarations otherwise hold for the whole of it.
    /// </summary>
    public static DiagnosticCode UsedBeforeDeclared { get; } = new(1016, DiagnosticSeverity.Error, "A query file uses a variable before declaring it");

    /// <summary>
    /// A query file with a <c>RETURN</c> that has a value, which SQL Server refuses in a batch run through <c>sp_executesql</c>
    /// (error 178, observed), so every call of its function would fail.
    /// </summary>
    public static DiagnosticCode QueryReturnsValue { get; } = new(1017, DiagnosticSeverity.Error, "A query file returns a value");

    /// <summary>
    /// A query file's sets comment (<c>-- @sets order, lines</c>) that can't be taken: it names no set, leaves a name out between
    /// commas, or is written a second time.
    /// </summary>
    public static DiagnosticCode BadSetsDeclaration { get; } = new(1018, DiagnosticSeverity.Error, "A query file's sets comment can't be taken");

    /// <summary>
    /// A query file's module comment (<c>-- @module reports</c>) that can't be taken: it names no module, names more than one, or
    /// is written a second time.
    /// </summary>
    public static DiagnosticCode BadModuleDeclaration { get; } = new(1019, DiagnosticSeverity.Error, "A query file's module comment can't be taken");

    /// <summary>
    /// A script or a folder of scripts that can't be read: a file not there (a link to nothing), or one or a folder the user may
    /// not open. Nothing in it is read, so the model may lack what it holds.
    /// </summary>
    public static DiagnosticCode UnreadableScript { get; } = new(1020, DiagnosticSeverity.Error, "A script or folder can't be read");

    /// <summary>
    /// A pre- or post-deployment script that can't be read in full: every table, procedure, function and view whose name it holds
    /// is doubted, since what it does to each isn't known (<c>UnreadDeployment</c>).
    /// </summary>
    public static DiagnosticCode DeploymentScriptNotRead { get; } = new(1021, DiagnosticSeverity.Warning, "A deployment script can't be read in full");

    /// <summary>
    /// Two names in one namespace that differ only in case (<c>Code</c> and <c>code</c>), which only a server or database that
    /// compares names by case allows: haruspex matches names without case, so it can't tell them apart (<c>CaseVariants</c>).
    /// </summary>
    public static DiagnosticCode CaseVariants { get; } = new(1022, DiagnosticSeverity.Error, "Names that differ only in case");
}

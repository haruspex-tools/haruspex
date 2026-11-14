namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The system's views, which every database has and <c>master.dacpac</c> lists: a two-part name in <c>sys</c> or
/// <c>INFORMATION_SCHEMA</c> that the project doesn't define is looked for in the <c>master</c> database the source holds. That
/// package lists each view's columns by name but types none of them (observed: every column of its 616 <c>sys</c> views is an
/// untyped <c>SqlComputedColumn</c>), so a column read from one is unknown, for that reason.
/// </summary>
public static class SystemViews
{
    /// <summary>The name a reference gives the master database (<c>DatabaseVariableLiteralValue="master"</c>).</summary>
    public const string Master = "master";

    /// <summary>Whether a schema is one of the system's, whose views every database has.</summary>
    public static bool IsSystemSchema(string schema) =>
        (schema ?? throw new ArgumentNullException(nameof(schema))).Equals("sys", StringComparison.OrdinalIgnoreCase) || schema.Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase);
}

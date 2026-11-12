namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The catalogue of SQL Server's built-in functions that haruspex reads, by form: each one's name and its stable code. The
/// functions ScriptDom parses as syntax of their own (<c>CAST</c>, <c>COALESCE</c>, <c>LEFT</c>, <c>OPENJSON</c>) are here too,
/// so that every function has one entry and one code, however it is written. A name is compared as a string only in
/// <see cref="Named"/>. What each function gives is inference's (its table of typing rules, keyed by these entries); this says
/// which functions there are. The guide's chapter <c>docs/internals/08-the-sql-server-type-system-as-data.md</c>, "Functions",
/// explains it.
/// </summary>
/// <remarks>
/// Not every built-in function SQL Server has: those inference reads, or that Semantics does (<c>LEN</c> in a CHECK constraint,
/// <c>GENERATE_SERIES</c> as a table source). A function not listed is not modelled, and a call of it is unknown.
/// </remarks>
public static class BuiltInFunctions
{
    /// <summary>Every function in the catalogue: the scalar functions, the aggregates, the window functions and the rowsets.</summary>
    public static IReadOnlyList<BuiltInFunction> All { get; } = [.. ScalarFunctions.All, .. AggregateFunctions.All, .. WindowFunctions.All, .. RowsetFunctions.All];

    /// <summary>
    /// The built-in function of this name and form, in any case; null for any other. The one place a function's name is
    /// compared.
    /// </summary>
    public static BuiltInFunction? Named(string name, FunctionForm form)
    {
        ArgumentNullException.ThrowIfNull(name);
        return All.FirstOrDefault(f => f.Form == form && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

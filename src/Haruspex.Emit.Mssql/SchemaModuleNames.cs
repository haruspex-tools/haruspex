namespace Haruspex.Emit.Mssql;

/// <summary>
/// The names a schema module declares for a procedure named <c>GetOrder</c>, which the functions import: the contract between
/// the database module's two files. The Zod emitter declares them so (its <c>ZodModule</c>), and a second TypeScript schema
/// emitter would declare the same, so that the functions, which check values through Standard Schema rather than through Zod,
/// stay the same whichever validator wrote the schemas. The emit check compiles both files together, which fails when the two
/// sides disagree.
/// </summary>
public static class SchemaModuleNames
{
    /// <summary>
    /// A parameter's key in the parameters' type and in the <c>OUTPUT</c> parameters' type: its declared name without its <c>@</c>
    /// (<c>@order_id</c> is <c>order_id</c>), as a result's keys are its columns' names as SQL Server sends them
    /// (<c>docs/decisions/0057-parameter-keys-are-their-declared-names.md</c>).
    /// </summary>
    public static string Key(string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.StartsWith('@') ? parameter[1..] : parameter;
    }

    /// <summary>The type of the procedure's results: <c>GetOrder</c>, its own name.</summary>
    public static string Results(string procedure) => procedure;

    /// <summary>The schema of the procedure's results: <c>GetOrderSchema</c>.</summary>
    public static string ResultsSchema(string procedure) => procedure + "Schema";

    /// <summary>The type of the procedure's parameters: <c>GetOrderParams</c>.</summary>
    public static string Parameters(string procedure) => procedure + "Params";

    /// <summary>The schema of the procedure's parameters: <c>GetOrderParamsSchema</c>.</summary>
    public static string ParametersSchema(string procedure) => Parameters(procedure) + "Schema";

    /// <summary>The type of what the procedure's <c>OUTPUT</c> parameters hold when it ends: <c>GetOrderOutput</c>.</summary>
    public static string Output(string procedure) => procedure + "Output";

    /// <summary>The schema of what the procedure's <c>OUTPUT</c> parameters hold: <c>GetOrderOutputSchema</c>.</summary>
    public static string OutputSchema(string procedure) => Output(procedure) + "Schema";
}

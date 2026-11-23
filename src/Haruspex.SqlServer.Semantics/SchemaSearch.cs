using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Where SQL Server looks for an object named without a schema, which depends on where the name is written.
/// </summary>
/// <remarks>
/// <para>In a module's definition (a procedure's, function's, view's or trigger's parameters, return type and body), it looks in
/// the module's own schema and then in <c>dbo</c>, whoever calls the module and whoever created it, for a table, view, synonym
/// or function, a write's target and an alias or table type (observed: BindingObservationTests'
/// A_name_in_a_procedure_is_looked_for_in_the_procedures_schema_whoever_calls and the tests beside it). A synonym's target
/// written without a schema is looked for in the synonym's schema and then in <c>dbo</c> in the same way
/// (A_synonyms_target_is_looked_for_in_the_synonyms_schema_then_dbo). So such a name means one object of the project, or
/// none, and never another schema's.</para>
/// <para>Everywhere else the caller's default schema and then <c>dbo</c> decide, and the project can't say who calls: in a
/// query file, in dynamic SQL, and in an <c>EXEC</c> even inside a module
/// (An_exec_or_dynamic_sql_in_a_procedure_follows_the_caller). A table's or table type's column type follows whoever creates it
/// (An_alias_type_in_a_tables_column_follows_whoever_creates_it), which the project can't say either.</para>
/// </remarks>
public sealed record SchemaSearch
{
    private SchemaSearch(string? own)
    {
        Own = own;
    }

    /// <summary>The caller's default schema, then <c>dbo</c>.</summary>
    public static SchemaSearch Caller { get; } = new((string?)null);

    /// <summary>The schema of the module or synonym whose definition the name is written in, then <c>dbo</c>; null for the caller's.</summary>
    public string? Own { get; }

    /// <summary>The search in a module's definition, or a synonym's target: its own schema, then <c>dbo</c>.</summary>
    /// <param name="named">The module or synonym; a trigger's name is in its table's schema.</param>
    public static SchemaSearch Within(SchemaObject named)
    {
        ArgumentNullException.ThrowIfNull(named);
        return Within(named.Name.Schema);
    }

    /// <summary>The search in a definition in <paramref name="schema"/>: that schema, then <c>dbo</c>.</summary>
    /// <param name="schema">The schema of the module or synonym.</param>
    public static SchemaSearch Within(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new(schema);
    }

    /// <summary>The search for a type's name: in the module it is written in (<see cref="SqlType.ModuleSchema"/>), or else the caller's.</summary>
    /// <param name="type">The type as declared.</param>
    public static SchemaSearch Of(SqlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.ModuleSchema is { } schema ? Within(schema) : Caller;
    }
}

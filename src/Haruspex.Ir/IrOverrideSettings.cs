namespace Haruspex.Ir;

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

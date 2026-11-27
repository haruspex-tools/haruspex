namespace Haruspex.Ir;

/// <summary>A setting an override can make. Its name in the IR's JSON is in <see cref="IrWireNames"/>.</summary>
public enum IrOverrideSetting
{
    /// <summary>The value is declared never null, which the database does not prove.</summary>
    NotNull,

    /// <summary>The value's type is declared to be a schema the application already has (<see cref="IrImport"/>).</summary>
    Type,

    /// <summary>A value set the database compares under its collation is declared to hold exactly its items.</summary>
    ExactValues,

    /// <summary>
    /// The results are declared, not inferred: a procedure whose result sets cannot be known from the project (dynamic SQL,
    /// say) is declared to return these, or none. On a caller, with <see cref="IrOverride.Via"/>, its results include the
    /// declared results of a procedure it calls.
    /// </summary>
    Results,

    /// <summary>
    /// The object reads a column the config file declares to hold JSON of a schema (0.28), named by <see cref="IrOverride.Via"/>,
    /// so what it returns of that JSON rests on the declaration, not on the database
    /// (<see cref="IrDocument.Columns"/>).
    /// </summary>
    ColumnJson,

    /// <summary>
    /// The object reads the response the config file declares an external REST endpoint to send (0.36), named by
    /// <see cref="IrOverride.Via"/> as <see cref="IrEndpointDeclaration.Endpoint"/> gives it, so what it returns of that response
    /// rests on the declaration, not on the database (<see cref="IrDocument.Endpoints"/>).
    /// </summary>
    EndpointJson,
}

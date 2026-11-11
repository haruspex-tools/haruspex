namespace Haruspex.Ir;

/// <summary>How a result reaches the caller, which decides how a reader must decode its values.</summary>
public enum IrEncoding
{
    /// <summary>Rows and columns, typed by the database driver.</summary>
    Rowset,

    /// <summary>A JSON document produced by the database, which the caller parses.</summary>
    Json,
}

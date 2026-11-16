namespace Haruspex.Ir;

/// <summary>What kind of database object an <see cref="IrObject"/> is; each kind is called in its own way.</summary>
public enum IrObjectKind
{
    /// <summary>A stored procedure: called with parameters, it returns zero or more result sets.</summary>
    Procedure,

    /// <summary>
    /// A query file of the application (0.22): one batch, run with its parameters, whose <see cref="IrObject.Id"/> is the file's
    /// path and whose <see cref="IrObject.Text"/> is what it runs. Its parameters are those its text uses without declaring.
    /// </summary>
    Query,
}

namespace Haruspex.Ir;

/// <summary>What kind of database object an <see cref="IrObject"/> is; each kind is called in its own way.</summary>
public enum IrObjectKind
{
    /// <summary>A stored procedure: called with parameters, it returns zero or more result sets.</summary>
    Procedure,
}

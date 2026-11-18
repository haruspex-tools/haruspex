namespace Haruspex.Ir;

/// <summary>What an application built against one document does about a difference in the database it meets (<see cref="IrFit"/>).</summary>
public enum IrFitVerdict
{
    /// <summary>It fails: its schemas refuse a value the database may return, or the database refuses a call it may make.</summary>
    Refused,

    /// <summary>It carries on, setting the difference aside: a key or a result set it doesn't know, a parameter it doesn't send.</summary>
    Ignored,
}

namespace Haruspex.SqlServer.Model;

/// <summary>
/// What a procedure, a scalar or multi-statement function, or a trigger runs: T-SQL statements (<see cref="SqlBody"/>), a
/// method of a .NET assembly (<see cref="ClrBody"/>), nothing at all (<see cref="NoBody"/>), or text the source doesn't hold
/// (<see cref="EncryptedBody"/>).
/// </summary>
/// <remarks>A closed set: the four kinds here are all there are (decision 0031).</remarks>
public abstract record ModuleBody
{
    /// <summary>Creates a body; only the kinds in this assembly can.</summary>
    private protected ModuleBody()
    {
    }
}

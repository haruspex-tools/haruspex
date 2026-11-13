namespace Haruspex.SqlServer.Model;

/// <summary>
/// What a procedure, a scalar or multi-statement function, or a trigger runs: T-SQL statements (<see cref="SqlBody"/>), a
/// method of a .NET assembly (<see cref="ClrBody"/>), or nothing at all (<see cref="NoBody"/>).
/// </summary>
/// <remarks>A closed set: the three kinds here are all there are (decision 0031).</remarks>
public abstract record ModuleBody
{
    /// <summary>Creates a body; only the kinds in this assembly can.</summary>
    private protected ModuleBody()
    {
    }
}

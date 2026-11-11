namespace Haruspex.SqlServer.Model;

/// <summary>The data-modifying statements a trigger can fire for.</summary>
[Flags]
public enum DmlEvents
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary><c>INSERT</c>.</summary>
    Insert = 1,

    /// <summary><c>UPDATE</c>.</summary>
    Update = 2,

    /// <summary><c>DELETE</c>.</summary>
    Delete = 4,
}

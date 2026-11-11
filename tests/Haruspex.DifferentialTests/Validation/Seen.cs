namespace Haruspex.DifferentialTests.Validation;

/// <summary>What was seen at a path, so a run can show its data reached every case the IR allows.</summary>
public enum Seen
{
    /// <summary>The value was null, or its key was left out because it was null.</summary>
    Null,

    /// <summary>A value that is not null.</summary>
    Value,

    /// <summary>A result that was absent: a FOR JSON query that returned no row.</summary>
    Absent,
}

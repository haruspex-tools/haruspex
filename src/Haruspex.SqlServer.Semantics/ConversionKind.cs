namespace Haruspex.SqlServer.Semantics;

/// <summary>Whether SQL Server converts a value of one type to another, as its conversion chart says.</summary>
public enum ConversionKind
{
    /// <summary>Without being asked, wherever the types meet: an assignment, <c>ISNULL</c>'s second argument, a comparison.</summary>
    Implicit,

    /// <summary>Only when asked, by <c>CAST</c> or <c>CONVERT</c>; meeting implicitly, the two types are an error (257).</summary>
    Explicit,

    /// <summary>Never: even <c>CAST</c> is an error (529), and so is meeting implicitly (206).</summary>
    Refused,
}

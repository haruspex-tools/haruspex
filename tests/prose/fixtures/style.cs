// Vale reads the comments in C#, and not its strings or identifiers: in this file only the comments are flagged.
namespace Haruspex.Fixture;

/// <summary>A seamless type that obviously works.</summary>
public static class Haruspex
{
    // Haruspex reads this comment — twice.
    public static string Simply => "powerful, just, e.g. — Haruspex!";

    /* A block comment, e.g. this one. */
    public static int Value(int? x) => x!.Value;
}

// Vale reads the comments in C#, and not its strings or identifiers: in this file only the comments are flagged.
namespace Haruspex.Fixture;

/// <summary>A seamless type that obviously works.</summary>
public static class Haruspex
{
    // Haruspex reads this comment — twice.
    public static string Simply => "powerful, just, e.g. — Haruspex!";

    /* A block comment, e.g. this one, which would realize nothing. */
    public static int Value(int? x) => x!.Value;

    // Slice 6f: the common expressions.
    public static string Slice => "slice 6f".Substring(0, 5);

    // What the column holds is not known here (K1).
    public static string Window => "OVER w1 WINDOW w1 AS (w2), w2 AS (W1)";

    /// <summary>The example is the brief's.</summary>
    public static void The_CM5_split_places_it_by_reference() { }

    public static void Typed_as_K1c_said() { }

    public static async ValueTask<int> As_the_brief_said_async() => await Task.FromResult(1);

    public static void Read_by_CM5_s_rule<T>() { }

    public static void Read_in_brief_by_the_window_w1_with_HAR4001_s_message() { }
}

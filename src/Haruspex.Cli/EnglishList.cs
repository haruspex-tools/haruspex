namespace Haruspex.Cli;

/// <summary>Items written as an English list in the command line's messages: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
internal static class EnglishList
{
    /// <summary>The items as one phrase.</summary>
    public static string Of(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}

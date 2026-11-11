namespace Haruspex.DifferentialTests.Server;

/// <summary>What a call returned: each result set as <c>columns: rows</c>, and the error that ended it, if any.</summary>
/// <param name="Sets">Each result set a client received, in order.</param>
/// <param name="Error">The number of the error the call failed with, or null when it succeeded.</param>
/// <param name="Messages">Errors handed to the client as messages (see <see cref="ObservedCall.Run"/>), by number.</param>
public sealed record CallOutcome(IReadOnlyList<string> Sets, int? Error, IReadOnlyList<int> Messages)
{
    public override string ToString() =>
        string.Join(" | ", Sets) + (Error is { } e ? $" ! {e}" : "") + (Messages.Count > 0 ? " ~ " + string.Join(",", Messages) : "");
}

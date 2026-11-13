using Haruspex.Diagnostics;

namespace Haruspex.TestSupport;

/// <summary>A test's view of a step's outcome: its value, which the test expects it to have.</summary>
internal static class Outcomes
{
    /// <summary>The value of a step that succeeded; fails the test, listing what was found, when it failed.</summary>
    public static T Expected<T>(this Outcome<T> outcome) => outcome switch
    {
        Succeeded<T> succeeded => succeeded.Value,
        Failed<T> failed => throw new Xunit.Sdk.XunitException("The step failed: " + string.Join("; ", failed.Diagnostics)),
    };

    /// <summary>The value of a step that succeeded, or null when it failed.</summary>
    public static T? OrDefault<T>(this Outcome<T> outcome) => outcome is Succeeded<T> succeeded ? succeeded.Value : default;
}

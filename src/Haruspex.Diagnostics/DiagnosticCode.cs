using System.Globalization;

namespace Haruspex.Diagnostics;

/// <summary>
/// The stable name of one kind of problem, such as <c>HAR1001</c>, the SQL that doesn't parse: what a page of the
/// documentation explains, what a suppression or a SARIF rule names, and what stays the same when a message is reworded.
/// </summary>
/// <remarks>
/// Each layer declares the codes of the problems it finds, in one class of its own. A code is <c>HAR</c> and four digits;
/// the first digit is the step the code was born in (1 reading the SQL, 2 inference, 3 reading the config file, 4 applying
/// it, 5 emitting, 6 the command line and files), and stays when a check moves. A code is never renumbered and never
/// given out twice: one that is no longer reported stays declared, <see cref="Retired"/>
/// (docs/decisions/0036-diagnostics-have-stable-codes.md).
/// </remarks>
public sealed record DiagnosticCode
{
    /// <summary>Declares a code.</summary>
    /// <param name="number">The number after <c>HAR</c>, from 1000 to 9999.</param>
    /// <param name="severity">How serious every problem of this kind is.</param>
    /// <param name="title">What the problem is, in a few words: the heading of its page.</param>
    public DiagnosticCode(int number, DiagnosticSeverity severity, string title)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 9999);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Number = number;
        Severity = severity;
        Title = title;
    }

    /// <summary>The number after <c>HAR</c>.</summary>
    public int Number { get; }

    /// <summary>How serious every problem of this kind is.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>What the problem is, in a few words.</summary>
    public string Title { get; }

    /// <summary>Whether haruspex no longer reports it. A retired code keeps its number and its page, so it is never reused.</summary>
    public bool Retired { get; init; }

    /// <summary>A problem of this kind: <paramref name="message"/>, at <paramref name="at"/>.</summary>
    /// <param name="at">Where, or null when it has no place.</param>
    /// <param name="message">What is wrong, and what to do about it.</param>
    public Diagnostic At(DiagnosticLocation? at, string message) => new(this, message, at);

    /// <summary>The code as it is written, <c>HAR1001</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"HAR{Number:D4}");
}

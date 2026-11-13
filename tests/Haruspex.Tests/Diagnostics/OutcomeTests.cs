using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Tests.Diagnostics;

/// <summary>
/// What a step gives: its value and what it found, or what it found and no value. How steps compose: in sequence, where a
/// failure skips what needs its value; and side by side, where both are reported.
/// </summary>
public class OutcomeTests
{
    private static readonly Diagnostic Error = new(new DiagnosticCode(9001, DiagnosticSeverity.Error, "Broken"), "broken", null);
    private static readonly Diagnostic OtherError = new(new DiagnosticCode(9002, DiagnosticSeverity.Error, "Also broken"), "also broken", null);
    private static readonly Diagnostic Warning = new(new DiagnosticCode(9003, DiagnosticSeverity.Warning, "Doubtful"), "doubtful", null);
    private static readonly Diagnostic Note = new(new DiagnosticCode(9004, DiagnosticSeverity.Note, "Unknown"), "unknown", null);

    [Fact]
    public void A_success_holds_no_error_and_a_failure_holds_at_least_one()
    {
        Assert.Throws<ArgumentException>(() => new Succeeded<int>(1, [Warning, Error]));
        Assert.Throws<ArgumentException>(() => new Failed<int>([Warning, Note]));
        Assert.Throws<ArgumentException>(() => new Failed<int>([]));
    }

    [Fact]
    public void Of_fails_when_anything_found_is_an_error()
    {
        Assert.Equal(new Succeeded<int>(1, [Warning]), Outcome.Of(1, [Warning]));
        Assert.Equal(new Failed<int>([Warning, Error]), Outcome.Of(1, [Warning, Error]));
    }

    [Fact]
    public void Then_runs_the_next_step_on_the_value_and_keeps_what_both_found_in_order()
    {
        var outcome = new Succeeded<int>(2, [Warning]).Then(n => new Succeeded<string>(new string('x', n), [Note]));

        Assert.Equal(new Succeeded<string>("xx", [Warning, Note]), outcome);
    }

    [Fact]
    public void Then_after_a_failure_does_not_run_the_next_step_and_keeps_what_was_found()
    {
        var ran = false;
        var outcome = new Failed<int>([Warning, Error]).Then(n =>
        {
            ran = true;
            return new Succeeded<int>(n, []);
        });

        Assert.False(ran);
        Assert.Equal(new Failed<int>([Warning, Error]), outcome);
    }

    [Fact]
    public void A_step_that_fails_after_a_success_keeps_the_earlier_warnings_first() =>
        Assert.Equal(new Failed<int>([Warning, Error]), new Succeeded<int>(1, [Warning]).Then(_ => new Failed<int>([Error])));

    [Fact]
    public void Select_maps_a_value_and_leaves_a_failure_alone()
    {
        Assert.Equal(new Succeeded<int>(3, [Note]), new Succeeded<int>(2, [Note]).Select(n => n + 1));
        Assert.Equal(new Failed<int>([Error]), new Failed<int>([Error]).Select(n => n + 1));
    }

    [Fact]
    public void Combine_reports_what_both_sides_found_even_when_the_first_failed()
    {
        Assert.Equal(new Failed<(int, int)>([Error, OtherError]), Outcome.Combine(new Failed<int>([Error]), new Failed<int>([OtherError])));
        Assert.Equal(new Failed<(int, int)>([Error, Warning]), Outcome.Combine(new Failed<int>([Error]), new Succeeded<int>(2, [Warning])));
        Assert.Equal(new Succeeded<(int, int)>((1, 2), [Note, Warning]), Outcome.Combine(new Succeeded<int>(1, [Note]), new Succeeded<int>(2, [Warning])));
    }

    [Fact]
    public void All_gives_every_value_in_order_or_every_problem()
    {
        Assert.Equal(
            new Succeeded<EquatableArray<int>>([1, 2], [Note]),
            Outcome.All([new Succeeded<int>(1, [Note]), new Succeeded<int>(2)]));
        Assert.Equal(
            new Failed<EquatableArray<int>>([Error, Warning, OtherError]),
            Outcome.All([new Failed<int>([Error]), new Succeeded<int>(2, [Warning]), new Failed<int>([OtherError])]));
    }

    [Fact]
    public void Outcomes_compare_by_their_value_and_what_they_found() =>
        EqualityLaws.Hold<Outcome<EquatableArray<int>>>([
            new Succeeded<EquatableArray<int>>([1, 2], [Warning]),
            new Succeeded<EquatableArray<int>>([1, 2], [Warning]),
            new Succeeded<EquatableArray<int>>([1, 2], []),
            new Failed<EquatableArray<int>>([Error]),
            new Failed<EquatableArray<int>>([Error]),
        ]);
}

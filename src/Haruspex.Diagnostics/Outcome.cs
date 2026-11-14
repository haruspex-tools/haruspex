using Haruspex.Collections;

namespace Haruspex.Diagnostics;

/// <summary>
/// What a step gives: its value and what it found on the way (<see cref="Succeeded{T}"/>), or, when it found an error, what
/// it found and no value (<see cref="Failed{T}"/>). A step that can't give its value because of the input reports it here,
/// rather than throwing; an exception is kept for a fault, a bug or a failure of the machine
/// (docs/decisions/0037-steps-return-outcomes.md).
/// </summary>
/// <remarks>
/// <para>
/// Steps are composed in two ways. <see cref="Then{TNext}"/> runs the next step on this one's value, so a step that needs a
/// value is never run without it, and a failure skips what follows while keeping everything found so far: the railway of
/// railway-oriented programming. <see cref="Outcome.Combine{TFirst, TSecond}"/> joins two steps that don't need each other's
/// values and reports what both found, so independent errors are all reported, as a compiler reports every error it can
/// rather than stopping at the first. The guide's chapter docs/internals/12-from-input-to-output.md explains both.
/// </para>
/// <para>
/// A closed hierarchy (docs/decisions/0031-the-model-is-closed-hierarchies-of-records.md): a switch over an outcome names both
/// cases, and the build checks it does.
/// </para>
/// </remarks>
/// <typeparam name="T">The step's value.</typeparam>
public abstract record Outcome<T>
{
    private protected Outcome(EquatableArray<Diagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    /// <summary>Everything found, in the order it was found.</summary>
    public EquatableArray<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Runs <paramref name="next"/> on the value, and gives its outcome after what this one found; on a failure, doesn't run
    /// it, and gives the failure.
    /// </summary>
    public Outcome<TNext> Then<TNext>(Func<T, Outcome<TNext>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return this switch
        {
            Succeeded<T> succeeded => next(succeeded.Value).After(Diagnostics),
            Failed<T> => new Failed<TNext>(Diagnostics),
        };
    }

    /// <summary>The value turned into another by <paramref name="map"/>, which can't fail; a failure stays one.</summary>
    public Outcome<TNext> Select<TNext>(Func<T, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return this switch
        {
            Succeeded<T> succeeded => new Succeeded<TNext>(map(succeeded.Value), Diagnostics),
            Failed<T> => new Failed<TNext>(Diagnostics),
        };
    }

    /// <summary>The same outcome with <paramref name="earlier"/> found before what it found.</summary>
    internal Outcome<T> After(EquatableArray<Diagnostic> earlier) =>
        earlier.Count == 0 ? this : this switch
        {
            Succeeded<T> succeeded => new Succeeded<T>(succeeded.Value, [.. earlier, .. Diagnostics]),
            Failed<T> => new Failed<T>([.. earlier, .. Diagnostics]),
        };
}

/// <summary>Builds outcomes and joins them.</summary>
public static class Outcome
{
    /// <summary>
    /// <paramref name="value"/> with what was found, or a failure when any of it is an error: for a step that checks
    /// everything before deciding.
    /// </summary>
    public static Outcome<T> Of<T>(T value, IEnumerable<Diagnostic> diagnostics)
    {
        EquatableArray<Diagnostic> found = [.. diagnostics];
        return found.Any(d => d.IsError) ? new Failed<T>(found) : new Succeeded<T>(value, found);
    }

    /// <summary>
    /// Both values, when both steps succeeded; otherwise a failure with what both found, in order. The steps don't depend
    /// on each other, so an error in the first is no reason to keep quiet about the second.
    /// </summary>
    public static Outcome<(TFirst First, TSecond Second)> Combine<TFirst, TSecond>(Outcome<TFirst> first, Outcome<TSecond> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        EquatableArray<Diagnostic> found = [.. first.Diagnostics, .. second.Diagnostics];
        return (first, second) switch
        {
            (Succeeded<TFirst> a, Succeeded<TSecond> b) => new Succeeded<(TFirst, TSecond)>((a.Value, b.Value), found),
            _ => new Failed<(TFirst, TSecond)>(found),
        };
    }

    /// <summary>Every value, in order, when every step succeeded; otherwise a failure with what they all found.</summary>
    public static Outcome<EquatableArray<T>> All<T>(IEnumerable<Outcome<T>> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var list = outcomes.ToList();
        EquatableArray<Diagnostic> found = [.. list.SelectMany(o => o.Diagnostics)];
        return list.All(o => o is Succeeded<T>)
            ? new Succeeded<EquatableArray<T>>([.. list.OfType<Succeeded<T>>().Select(s => s.Value)], found)
            : new Failed<EquatableArray<T>>(found);
    }
}

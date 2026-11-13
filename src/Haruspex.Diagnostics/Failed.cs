using Haruspex.Collections;

namespace Haruspex.Diagnostics;

/// <summary>A step found at least one error, so it has no value; what it found says why.</summary>
/// <typeparam name="T">The value the step would have given.</typeparam>
public sealed record Failed<T> : Outcome<T>
{
    /// <summary>What was found, at least one of it an error.</summary>
    /// <exception cref="ArgumentException">None of <paramref name="diagnostics"/> is an error: a fault of the step that built this.</exception>
    public Failed(EquatableArray<Diagnostic> diagnostics)
        : base(diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!diagnostics.Any(d => d.IsError))
            throw new ArgumentException("A step fails only with an error; one with none has succeeded.", nameof(diagnostics));
    }
}

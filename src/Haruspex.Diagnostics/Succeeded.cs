using Haruspex.Collections;

namespace Haruspex.Diagnostics;

/// <summary>A step gave its value; what it found is warnings and notes, never an error.</summary>
/// <typeparam name="T">The step's value.</typeparam>
public sealed record Succeeded<T> : Outcome<T>
{
    /// <summary>The value, and what was found on the way.</summary>
    /// <exception cref="ArgumentException">One of <paramref name="diagnostics"/> is an error: a fault of the step that built this.</exception>
    public Succeeded(T value, EquatableArray<Diagnostic> diagnostics)
        : base(diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Any(d => d.IsError))
            throw new ArgumentException("A step that found an error has failed: build a Failed outcome.", nameof(diagnostics));
        Value = value;
    }

    /// <summary>The value, with nothing found.</summary>
    public Succeeded(T value)
        : this(value, [])
    {
    }

    /// <summary>The step's value.</summary>
    public T Value { get; }
}

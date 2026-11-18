using Haruspex.Collections;

namespace Haruspex.Emit.Mssql;

/// <summary>
/// What a procedure's generated function hands back (docs/decisions/0060-a-call-returns-its-one-result-itself.md): its one
/// result itself, an object of named fields, or nothing. Callers are written against it, so a change of shape is a change to
/// every caller, which <c>haruspex check --baseline</c> warns of.
/// </summary>
/// <param name="IsResultItself">Whether the function returns its one result itself rather than an object naming it.</param>
/// <param name="Fields">
/// What it hands back, by name, in order (each named set's name, or <c>result</c>, or <c>result1</c>, <c>result2</c>; then
/// <c>output</c> and <c>returnValue</c>); empty when it returns nothing, and <c>result</c> alone when it returns the result itself.
/// </param>
public sealed record MssqlReturnShape(bool IsResultItself, EquatableArray<string> Fields)
{
    /// <summary>The shape in words: <c>the result itself</c>, <c>{ result1, result2 }</c>, <c>nothing</c>.</summary>
    public string Description => IsResultItself ? "the result itself" : Fields.Count == 0 ? "nothing" : "{ " + string.Join(", ", Fields) + " }";

    /// <summary>
    /// Whether code written against <paramref name="earlier"/> still reads this shape as it did: the same result itself, or an
    /// object with every field the earlier one had. A function that returned nothing had no callers reading it.
    /// </summary>
    public bool KeepsCallersOf(MssqlReturnShape earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        return earlier switch
        {
            { IsResultItself: true } => IsResultItself,
            { Fields.Count: 0 } => true,
            _ => !IsResultItself && earlier.Fields.All(Fields.Contains),
        };
    }
}

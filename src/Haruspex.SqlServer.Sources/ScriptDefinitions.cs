using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// What one script's statements leave of each procedure, function or view it defines, alters or drops, played in batch order as
/// sqlcmd runs it. Within a script the order is known, so the definition that stands is the last one that runs: a stub created
/// and then altered is its <c>ALTER</c>, one dropped and created again its last <c>CREATE</c>
/// (<c>docs/decisions/0080-the-last-definition-in-a-script-stands.md</c>).
/// </summary>
/// <remarks>
/// Each statement does what the server does with it (observed: <c>ExecObservationTests.Definitions.cs</c>). A <c>CREATE</c>
/// of a name that stands already is refused (2714), so the first stands and the second is reported as a definition twice, as
/// across scripts. <c>CREATE OR ALTER</c> and <c>ALTER</c> replace the whole definition; an <c>ALTER</c> of a name that doesn't
/// exist is refused (208) and leaves none. A <c>DROP</c>, <c>DROP … IF EXISTS</c> or a drop under <c>IF OBJECT_ID(name) IS NOT
/// NULL</c> leaves none. A drop under any other condition may not run, so a <c>CREATE</c> after it may be refused or not: which
/// definition stands is in doubt, and the doubt is recorded. A statement that names an object this script hasn't defined yet is
/// about what stands before the script, which only the folder's other scripts can say; <see cref="DefinitionsInEffect"/> settles
/// that once every script is read.
/// </remarks>
/// <typeparam name="T">A procedure, a function, or a view's definition.</typeparam>
/// <param name="diagnostics">Where a second <c>CREATE</c> in the script is reported.</param>
/// <param name="sameKind">
/// Whether a definition may replace another in place. A function's kind (scalar, inline or multi-statement) can't change by
/// <c>ALTER</c> or <c>CREATE OR ALTER</c>: the server refuses it (2010) and the earlier definition stands (observed:
/// A_function_s_kind_cannot_change_by_alter). A procedure or a view has no kind.
/// </param>
internal sealed class ScriptDefinitions<T>(List<Diagnostic> diagnostics, Func<T, T, bool> sameKind)
    where T : class
{
    /// <summary>For a procedure or a view, which any definition may replace.</summary>
    public ScriptDefinitions(List<Diagnostic> diagnostics)
        : this(diagnostics, (_, _) => true)
    {
    }

    /// <summary>Each object the script names, in the order it first names it.</summary>
    private readonly Dictionary<ObjectName, State> objects = [];

    /// <summary>A <c>CREATE</c>, or with <paramref name="orAlter"/> a <c>CREATE OR ALTER</c>, of <paramref name="name"/>.</summary>
    public void Create(ObjectName name, T definition, SourceLocation at, bool orAlter)
    {
        var state = Of(name);
        if (state.Standing is null || (orAlter && sameKind(state.Standing, definition)))
        {
            Set(name, state with { Standing = definition, At = at, Established = true, Dropped = false, Doubt = null, MaybeDropped = null });
        }
        else if (state.MaybeDropped is { } maybe)
        {
            // Dropped, the CREATE stands; not, it is refused and the earlier one does. The model keeps the earlier, with the doubt.
            Set(name, state with { Established = true, Doubt = maybe, MaybeDropped = null });
        }
        else if (!state.Established)
        {
            // Only altered so far: if the ALTER ran, the CREATE is refused; if it didn't, the CREATE stands.
            Set(name, state with { Established = true, Doubt = state.Why });
        }
        else if (!orAlter)
        {
            diagnostics.Add(new Diagnostic(SourceCodes.DefinedTwice, $"{name} is defined more than once.", at));
        }

        // Otherwise a CREATE OR ALTER that would change a function's kind, which is refused: the earlier definition stands.
    }

    /// <summary>An <c>ALTER</c> of <paramref name="name"/>, which <paramref name="why"/> records.</summary>
    public void Alter(ObjectName name, T definition, SourceLocation at, Redefinition why)
    {
        var state = Of(name);

        // After a drop it is refused, as it is when it would change a function's kind: either way, what stood stands.
        if (state.Dropped || (state.Standing is { } standing && !sameKind(standing, definition)))
            return;

        // Whatever stood, or may have, the ALTER replaces it; if nothing did, it is refused and there is none. So it stands.
        Set(name, state with { Standing = definition, At = at, Why = state.Why ?? why, Doubt = null, MaybeDropped = null });
    }

    /// <summary>
    /// A drop of <paramref name="name"/>, which <paramref name="why"/> records: <paramref name="certain"/> when it runs whenever the
    /// object exists, false when it is under a condition that may be false.
    /// </summary>
    public void Drop(ObjectName name, Redefinition why, bool certain)
    {
        if (certain)
        {
            var state = Of(name);
            Set(name, state with { Standing = null, Dropped = true, Established = true, Why = state.Why ?? why, Doubt = null, MaybeDropped = null });
        }
        else if (objects.TryGetValue(name, out var state) && state.Standing is not null)
        {
            // Of a name this script hasn't defined, a drop that may not run is not followed, as no conditional statement is.
            Set(name, state with { MaybeDropped = why });
        }
    }

    /// <summary>What the script leaves of each object it names, in the order it first names them.</summary>
    public IEnumerable<ScriptOutcome<T>> Outcomes =>
        objects.Select(o => new ScriptOutcome<T>(o.Key, o.Value.Standing, o.Value.At, o.Value.Established, o.Value.Doubt ?? (o.Value.Established && o.Value.Standing is not null ? null : o.Value.Why)));

    private State Of(ObjectName name) => objects.GetValueOrDefault(name) ?? new State(null, null, false, false, null, null, null);

    private void Set(ObjectName name, State state) => objects[name] = state;

    /// <summary>One object's state as the script has run so far.</summary>
    /// <param name="Standing">The definition that stands, or null for none.</param>
    /// <param name="At">Where <paramref name="Standing"/> is.</param>
    /// <param name="Established">
    /// Whether the script itself made what stands, by a <c>CREATE</c>, a <c>CREATE OR ALTER</c> or a drop, rather than only altering
    /// what stood before it.
    /// </param>
    /// <param name="Dropped">Whether the script's last say was a drop, after which an <c>ALTER</c> is refused.</param>
    /// <param name="Why">The script's first <c>ALTER</c> or drop of it, which the folder's pass records when another script changes it too.</param>
    /// <param name="Doubt">The statement that leaves which definition stands in doubt, or null when none does.</param>
    /// <param name="MaybeDropped">A drop under a condition that may be false since the last definition, or null.</param>
    private sealed record State(T? Standing, SourceLocation? At, bool Established, bool Dropped, Redefinition? Why, Redefinition? Doubt, Redefinition? MaybeDropped);
}

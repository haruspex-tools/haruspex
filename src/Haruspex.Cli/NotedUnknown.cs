using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// An unknown in the module, with the note it is reported by: so a report can count unknowns by reason, group their notes by
/// object, and say which are new since the last run, from one list.
/// </summary>
/// <param name="Unknown">The unknown, as the IR holds it.</param>
/// <param name="Note">
/// Its note, with its code, place and reason; null for one only a declaration can resolve, which the config layer reports as a
/// warning instead, saying what to declare (<c>InferenceCodes.Of</c>).
/// </param>
internal sealed record NotedUnknown(IrUnknown Unknown, Diagnostic? Note);

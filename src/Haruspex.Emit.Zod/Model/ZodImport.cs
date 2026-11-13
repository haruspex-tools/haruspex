using Haruspex.Collections;

namespace Haruspex.Emit.Zod.Model;

/// <summary>An import of schemas the application defines: <c>import { A, B } from '…'</c>.</summary>
/// <param name="From">The ES module specifier, as it is written.</param>
/// <param name="Names">The names imported, in the order written.</param>
internal sealed record ZodImport(string From, EquatableArray<string> Names);

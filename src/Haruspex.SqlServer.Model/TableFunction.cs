using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A function whose rows are a table it declares and its body fills (<c>RETURNS @t TABLE (…)</c>), or, for a CLR function,
/// the table it declares its rows to be.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Parameters">Its parameters, in declaration order.</param>
/// <param name="Returns">
/// The table it returns, as declared. Its body fills it as a table variable, so it is named by its variable (<c>@t</c>), as a
/// body's table variable is (<see cref="DeclaredTable"/>). A CLR function declares its table with no variable, so its table is
/// named as the function is.
/// </param>
/// <param name="Body">What it runs.</param>
public sealed record TableFunction(ObjectName Name, SourceSpan Span, IReadOnlyList<Parameter> Parameters, DeclaredTable Returns, ModuleBody Body)
    : Function(Name, Span, Parameters);

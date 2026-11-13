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
/// The table it returns, as declared; named by its variable (<c>@t</c>) with no schema, or with an empty name for a CLR
/// function, which declares no variable.
/// </param>
/// <param name="Body">What it runs.</param>
public sealed record TableFunction(ObjectName Name, SourceSpan Span, IReadOnlyList<Parameter> Parameters, Table Returns, ModuleBody Body)
    : Function(Name, Span, Parameters);

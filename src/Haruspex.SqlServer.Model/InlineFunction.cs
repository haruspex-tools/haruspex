using Haruspex.Collections;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A function whose rows are one query over its parameters (<c>RETURNS TABLE AS RETURN SELECT …</c>).</summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its definition is.</param>
/// <param name="Parameters">Its parameters, in declaration order.</param>
/// <param name="Query">The query, as ScriptDom parsed it.</param>
public sealed record InlineFunction(ObjectName Name, SourceSpan Span, EquatableArray<Parameter> Parameters, SelectStatement Query)
    : Function(Name, Span, Parameters);

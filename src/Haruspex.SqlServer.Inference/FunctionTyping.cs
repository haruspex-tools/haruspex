using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// How inference types one built-in function (an entry of <see cref="BuiltInFunctions"/>): a row of the function table,
/// <c>Typer.FunctionTable.cs</c>, which is the one path a call is typed by. The guide's chapter
/// <c>docs/internals/08-the-sql-server-type-system-as-data.md</c>, "Functions", explains it.
/// </summary>
/// <param name="Function">The function, whose code (<see cref="BuiltInFunction.Code"/>) an explanation of a value cites.</param>
/// <param name="Arity">The numbers of arguments read, least and most (null for no most); null where the syntax fixes them.</param>
/// <param name="OtherForms">What a call with another number gives.</param>
/// <param name="Rule">The typing rule, in words: what type it gives.</param>
/// <param name="MayBeNullBecause">The reasons it may be NULL that it adds to its arguments' (CatalogueSweepTests holds the sweep to these).</param>
/// <param name="ObservedIn">The observation tests that establish the rule, each of which calls the function.</param>
/// <param name="ReadsOnlyItsArguments">Whether it gives the same value when read again in one row, reading nothing but its arguments.</param>
/// <param name="Type">The typing method; null for a rowset, which the relation readers type (<c>GenerateSeries</c>, <c>OpenJsonRelation</c>).</param>
internal sealed record FunctionTyping(
    BuiltInFunction Function,
    (int Least, int? Most)? Arity,
    OtherForms OtherForms,
    string Rule,
    EquatableArray<IrNullRule> MayBeNullBecause,
    EquatableArray<string> ObservedIn,
    bool ReadsOnlyItsArguments,
    Func<Typer, ScalarExpression, ReadScope, Value>? Type);

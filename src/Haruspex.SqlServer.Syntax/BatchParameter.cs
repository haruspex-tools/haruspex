using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Syntax;

/// <summary>A variable a batch uses without declaring: a parameter of it, when <c>sp_executesql</c> runs it (<see cref="BatchVariables"/>).</summary>
/// <param name="Name">Its name, with its <c>@</c>, as the batch first writes it.</param>
/// <param name="First">Where the batch first uses it.</param>
public sealed record BatchParameter(string Name, VariableReference First);

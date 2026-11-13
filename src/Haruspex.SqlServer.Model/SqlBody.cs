using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A body of T-SQL statements.</summary>
/// <param name="Statements">The statements, as ScriptDom parsed them; positions in them are in their module's file.</param>
public sealed record SqlBody(StatementList Statements) : ModuleBody;

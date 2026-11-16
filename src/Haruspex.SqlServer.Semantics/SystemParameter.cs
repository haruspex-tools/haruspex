namespace Haruspex.SqlServer.Semantics;

/// <summary>A parameter of a system procedure, as <c>sys.all_parameters</c> describes it.</summary>
/// <param name="Name">Its name, with its <c>@</c>, as the server spells it.</param>
/// <param name="Type">Its type as T-SQL writes it: <c>nvarchar(255)</c>, <c>sysname</c>.</param>
/// <param name="IsOutput">Whether it is an <c>OUTPUT</c> parameter.</param>
public sealed record SystemParameter(string Name, string Type, bool IsOutput = false);

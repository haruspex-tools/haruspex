namespace Haruspex.SqlServer.Model;

/// <summary>
/// A definition with nothing after its <c>AS</c>: no statements and no <c>EXTERNAL NAME</c>. ScriptDom accepts
/// <c>CREATE PROCEDURE p AS</c> alone, which SQL Server refuses, so there is nothing to read.
/// </summary>
public sealed record NoBody : ModuleBody;

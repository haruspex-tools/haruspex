namespace Haruspex.SqlServer.Model;

/// <summary>
/// A body created <c>WITH ENCRYPTION</c> whose text the source doesn't hold. SQL Server gives no one an encrypted module's
/// text, so a DACPAC extracted from a database keeps a placeholder in its place
/// (<c>BEGIN --The script body was encrypted and cannot be reproduced here. RETURN END</c>, observed with SqlPackage 170.2.70),
/// which, read as a body, would claim the module returns nothing. What it does is unknown.
/// </summary>
/// <remarks>A DACPAC built from a project keeps the project's text, encrypted or not, and its body is a <see cref="SqlBody"/>.</remarks>
public sealed record EncryptedBody : ModuleBody;
